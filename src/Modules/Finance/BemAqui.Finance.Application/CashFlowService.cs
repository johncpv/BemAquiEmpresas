using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Fluxo de caixa base (FIN-05, 04-fluxos §3.5): saldo atual das contas ativas, realizado pelas transações e projetado pelos
/// títulos em aberto (pelo saldo, no vencimento original; vencidos entram hoje) e pelas recorrentes ainda sem título.
/// Só leitura, consultas do EF com parâmetros e agregação no banco.
/// </summary>
public sealed partial class CashFlowService(IFinanceDb db, IParties parties, TimeProvider clock)
{
    public const int MaxDays = 365;

    private static readonly ReceivableStatus[] OpenReceivable = [ReceivableStatus.Open, ReceivableStatus.PartiallyPaid, ReceivableStatus.Planned];

    private static readonly PayableStatus[] ClosedPayable = [PayableStatus.Paid, PayableStatus.Cancelled];

    public async Task<Result<CashFlowResult>> BuildAsync(CashFlowQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var today = BusinessTime.Today(clock);
        var view = query.View ?? CashFlowSeries.Daily;
        if (!CashFlowSeries.Views.Contains(view))
        {
            return ReceivableService.Invalid("view", "Escolha a visão diária, semanal ou mensal.");
        }

        if (query.Days is < 1 or > MaxDays)
        {
            return ReceivableService.Invalid("days", $"Escolha um horizonte de 1 a {MaxDays} dias.");
        }

        var from = query.From ?? new DateOnly(today.Year, today.Month, 1);
        if (from > today || from < today.AddDays(-MaxDays))
        {
            return ReceivableService.Invalid("from", "O início vai de um ano atrás até hoje.");
        }

        var ids = await AccountIdsAsync(query.BankAccountId, cancellationToken);
        if (ids is null)
        {
            return Error.NotFound("bank_account_not_found", "Conta não encontrada.");
        }

        var to = today.AddDays(query.Days ?? 90);
        var balance = await BankBalanceAsync(ids, from.AddDays(-1), cancellationToken);
        var totals = (await db.FinancialTransactions.AsNoTracking().Where(t => ids.Contains(t.BankAccountId) && t.Date >= from && t.Date <= today)
                .GroupBy(t => t.Date).Select(g => new { g.Key, In = g.Sum(t => t.Amount > 0 ? t.Amount : 0), Out = g.Sum(t => t.Amount < 0 ? -t.Amount : 0) }).ToListAsync(cancellationToken))
            .ToDictionary(r => r.Key, r => new CashFlowDayTotals(r.In, r.Out));
        var current = balance + totals.Values.Sum(t => t.Inflow - t.Outflow);
        var consolidated = query.BankAccountId is null;
        if (consolidated)
        {
            await ProjectAsync(totals, today, to, cancellationToken);
        }

        var days = CashFlowSeries.Days(from, consolidated ? to : today, today, balance, totals, consolidated);
        var (lowest, negative) = CashFlowSeries.Highlights(days);
        var undated = await db.Receivables.AsNoTracking().Where(r => r.Status == ReceivableStatus.Planned && r.DueDate == null)
            .GroupBy(_ => 1).Select(g => new CashFlowUndated(g.Count(), g.Sum(r => r.Amount))).FirstOrDefaultAsync(cancellationToken);
        return new CashFlowResult(today, from, consolidated ? to : today, view, query.BankAccountId, current, consolidated, CashFlowSeries.Aggregate(days, view), lowest, negative, undated ?? new CashFlowUndated(0, 0));
    }

    /// <summary>
    /// Saldo das contas no fim de <paramref name="date"/> (<c>BankBalanceQuery</c>, CA 2): saldo inicial + Σ transações até o dia.
    /// A FIN-04 e o painel reaproveitam.
    /// </summary>
    public async Task<decimal> BankBalanceAsync(IReadOnlyCollection<Guid> accountIds, DateOnly date, CancellationToken cancellationToken = default)
    {
        var opening = await db.BankAccounts.AsNoTracking().Where(a => accountIds.Contains(a.Id)).SumAsync(a => a.OpeningBalance ?? 0, cancellationToken);
        var moved = await db.FinancialTransactions.AsNoTracking().Where(t => accountIds.Contains(t.BankAccountId) && t.Date <= date).SumAsync(t => (decimal?)t.Amount, cancellationToken);
        return opening + (moved ?? 0);
    }

    /// <summary>Contas ativas do filtro; nulo quando a conta pedida não existe ou está inativa.</summary>
    private async Task<List<Guid>?> AccountIdsAsync(Guid? bankAccountId, CancellationToken ct)
    {
        var ids = await db.BankAccounts.AsNoTracking().Where(a => a.IsActive && (bankAccountId == null || a.Id == bankAccountId)).Select(a => a.Id).ToListAsync(ct);
        return bankAccountId is not null && ids.Count == 0 ? null : ids;
    }

    private async Task ProjectAsync(Dictionary<DateOnly, CashFlowDayTotals> totals, DateOnly today, DateOnly to, CancellationToken ct)
    {
        void Add(DateOnly due, decimal inflow, decimal outflow)
        {
            var day = due < today ? today : due;
            var t = totals.GetValueOrDefault(day);
            var overdue = due < today;
            totals[day] = new CashFlowDayTotals(t.Inflow + inflow, t.Outflow + outflow, t.OverdueInflow + (overdue ? inflow : 0), t.OverdueOutflow + (overdue ? outflow : 0));
        }

        var receivables = await db.Receivables.AsNoTracking().Where(r => OpenReceivable.Contains(r.Status) && r.DueDate != null && r.DueDate <= to)
            .GroupBy(r => r.DueDate!.Value).Select(g => new { g.Key, Sum = g.Sum(r => r.Amount - r.PaidAmount) }).ToListAsync(ct);
        foreach (var r in receivables)
        {
            Add(r.Key, r.Sum, 0);
        }

        var payables = await db.Payables.AsNoTracking().Where(p => !ClosedPayable.Contains(p.Status) && p.DueDate != null && p.DueDate <= to)
            .GroupBy(p => p.DueDate!.Value).Select(g => new { g.Key, Sum = g.Sum(p => p.Amount - p.RetentionIss - p.RetentionInss - p.RetentionIr - p.RetentionPcc - p.PaidAmount) }).ToListAsync(ct);
        foreach (var p in payables)
        {
            Add(p.Key, 0, p.Sum);
        }

        foreach (var (_, amount, due) in await OccurrencesAsync(today, to, ct))
        {
            Add(due, 0, amount);
        }
    }

    /// <summary>Recorrentes ativas sem título na competência (CA 4): a mesma regra de vencimento do job.</summary>
    private async Task<List<(Domain.RecurringExpense Expense, decimal Amount, DateOnly Due)>> OccurrencesAsync(DateOnly today, DateOnly to, CancellationToken ct)
    {
        var expenses = await db.RecurringExpenses.AsNoTracking().Where(e => e.IsActive).ToListAsync(ct);
        var ids = expenses.Select(e => e.Id).ToList();
        var since = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var generated = (await db.Payables.AsNoTracking().Where(p => p.RecurringExpenseId != null && ids.Contains(p.RecurringExpenseId.Value) && p.Competence >= since)
                .Select(p => new { Id = p.RecurringExpenseId!.Value, Competence = p.Competence!.Value }).ToListAsync(ct))
            .GroupBy(p => p.Id).ToDictionary(g => g.Key, g => (IReadOnlySet<DateOnly>)g.Select(p => p.Competence).ToHashSet());
        return expenses.SelectMany(e => CashFlowSeries.Occurrences(e.Start, e.End, e.DueDateOf, today, to, generated.GetValueOrDefault(e.Id) ?? new HashSet<DateOnly>())
            .Select(o => (e, e.Amount, o.Due))).ToList();
    }
}
