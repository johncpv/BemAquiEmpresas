using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public sealed partial class CashFlowService
{
    /// <summary>Ordenações aceitas no detalhe do dia (CA 13): fora delas, 422.</summary>
    public static readonly IReadOnlyList<string> DaySorts = ["kind", "amount", "-amount", "description"];

    /// <summary>
    /// O que compõe um dia (CA 10): as transações do dia e, de hoje em diante, os títulos e as recorrentes previstas
    /// (hoje inclui os vencidos em aberto). Cada item tem o link do documento.
    /// </summary>
    public async Task<Result<CashFlowDay>> DayAsync(DateOnly date, Guid? bankAccountId, string? sort, string? scenario = null, CancellationToken cancellationToken = default)
    {
        var today = BusinessTime.Today(clock);
        sort ??= "kind";
        scenario ??= ScenarioBase;
        if (!DayScenarios.Contains(scenario))
        {
            return ReceivableService.Invalid("scenario", "Escolha o cenário base ou ajustado.");
        }

        if (!DaySorts.Contains(sort))
        {
            return ReceivableService.Invalid("sort", "Ordene por tipo, valor ou descrição.");
        }

        if (date < today.AddDays(-MaxDays) || date > today.AddDays(MaxDays))
        {
            return ReceivableService.Invalid("date", "Escolha um dia de até um ano antes ou depois de hoje.");
        }

        var ids = await AccountIdsAsync(bankAccountId, cancellationToken);
        if (ids is null)
        {
            return Error.NotFound("bank_account_not_found", "Conta não encontrada.");
        }

        var items = new List<CashFlowItem>();
        if (date <= today)
        {
            var transactions = await db.FinancialTransactions.AsNoTracking().Where(t => ids.Contains(t.BankAccountId) && t.Date == date).OrderBy(t => t.CreatedAt).ToListAsync(cancellationToken);
            items.AddRange(transactions.Select(t => new CashFlowItem("Transaction", t.Id, null, t.Description, null, t.Amount, t.Date, false, false,
                t.ReceivableId is { } r ? "/app/financeiro/receber/" + r : null)));
        }

        var projected = bankAccountId is null && date >= today;
        var adjusted = projected && scenario == ScenarioAdjusted;
        if (projected)
        {
            await AddTitlesAsync(items, date, today, !adjusted, cancellationToken);
        }

        if (adjusted)
        {
            await AddAdjustedReceivablesAsync(items, date, today, cancellationToken);
        }

        IEnumerable<CashFlowItem> sorted = sort switch
        {
            "amount" => items.OrderBy(i => i.Amount),
            "-amount" => items.OrderByDescending(i => i.Amount),
            "description" => items.OrderBy(i => i.Description, StringComparer.CurrentCulture),
            _ => items.OrderBy(i => i.Kind, StringComparer.Ordinal).ThenBy(i => i.DueDate),
        };
        return new CashFlowDay(date, projected, sorted.ToList(), adjusted ? ScenarioAdjusted : ScenarioBase, items.Where(i => !i.ShiftedOut).Sum(i => i.Amount));
    }

    private async Task AddTitlesAsync(List<CashFlowItem> items, DateOnly date, DateOnly today, bool includeReceivables, CancellationToken ct)
    {
        var isToday = date == today;
        var receivables = await db.Receivables.AsNoTracking()
            .Where(r => includeReceivables && OpenReceivable.Contains(r.Status) && r.DueDate != null && (r.DueDate == date || (isToday && r.DueDate < today)))
            .Select(r => new { r.Id, r.Number, r.Description, r.PartyId, Balance = r.Amount - r.PaidAmount, r.DueDate }).ToListAsync(ct);
        var payables = await db.Payables.AsNoTracking()
            .Where(p => !ClosedPayable.Contains(p.Status) && p.DueDate != null && (p.DueDate == date || (isToday && p.DueDate < today)))
            .Select(p => new { p.Id, p.Number, p.Description, p.PartyId, Balance = p.Amount - p.RetentionIss - p.RetentionInss - p.RetentionIr - p.RetentionPcc - p.PaidAmount, p.DueDate, p.IsEstimate })
            .ToListAsync(ct);
        var recurring = (await OccurrencesAsync(today, date, ct)).Where(o => o.Due == date).ToList();
        var partyIds = receivables.Select(r => r.PartyId).Concat(payables.Select(p => p.PartyId)).Concat(recurring.Select(o => o.Expense.PartyId)).Distinct().ToList();
        var names = (await parties.FindManyAsync(partyIds, ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        items.AddRange(receivables.Select(r => new CashFlowItem("Receivable", r.Id, r.Number, r.Description, names.GetValueOrDefault(r.PartyId), r.Balance, r.DueDate, r.DueDate < today, false, "/app/financeiro/receber/" + r.Id)));
        items.AddRange(payables.Select(p => new CashFlowItem("Payable", p.Id, p.Number, p.Description, names.GetValueOrDefault(p.PartyId), -p.Balance, p.DueDate, p.DueDate < today, p.IsEstimate, "/app/financeiro/pagar/" + p.Id)));
        items.AddRange(recurring.Select(o => new CashFlowItem("Recurring", o.Expense.Id, null, o.Expense.Description, names.GetValueOrDefault(o.Expense.PartyId), -o.Amount, o.Due, false, false, "/app/financeiro/recorrentes")));
    }
}
