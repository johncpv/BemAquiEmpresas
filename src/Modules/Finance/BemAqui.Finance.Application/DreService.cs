using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>Valor com sinal de uma conta num dia, de qualquer fonte do DRE.</summary>
internal sealed record DreLedgerEntry(Guid AccountId, DateOnly Date, decimal Value);

/// <summary>
/// DRE gerencial mensal (FIN-08, R-FIN-13, P-17 competência), calculado na leitura, sem tabela de saldos: consultas do EF
/// agregadas no banco por conta e dia (sempre com parâmetros, ADR 0006), montagem pura no <see cref="DreBuilder"/>.
/// <list type="bullet">
/// <item>Receita: <see cref="RevenueRecognitionRouter"/> (pelo título, "provisório", até as notas reais; o sinal como adiantamento).</item>
/// <item>Títulos a pagar não cancelados, não previstos e fora da implantação, pelo valor bruto, na competência ou na data do documento.</item>
/// <item>Lançamentos bancários sem OP não estornados, pela data e com o sinal do tipo; transferências não entram.</item>
/// <item>Juros, multa e descontos das baixas não estornadas, nas contas dos parâmetros, no mês da baixa.</item>
/// </list>
/// </summary>
public sealed partial class DreService(
    IFinanceDb db, RevenueRecognitionRouter revenue, DreParametersService parameters, IParties parties, IPermissionChecker permissions, ICurrentUser currentUser, TimeProvider clock)
{
    public const int MaxMonths = 24;
    public const string ViewMarkIndex = "ix_dre_view_marks_user_id";

    /// <summary>Contas "A classificar" do seed (CA 10, R-PRP-5).</summary>
    public static readonly string[] ClassifyCodes = ["1.9", "3.9"];

    public async Task<Result<DreReport>> BuildAsync(DreQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var period = Period(query.From, query.To);
        if (period.Error is { } error)
        {
            return error;
        }

        var (from, to, months) = (period.From, period.To, period.Months);
        var (accounts, classify) = await AccountsAsync(cancellationToken);
        var (entries, routed) = await LedgerAsync(from, to, cancellationToken);
        var advances = months.Select(routed.AdvanceBalance).ToList();
        var statement = DreBuilder.Build(months, accounts, entries.Select(e => new DreAmount(e.AccountId, DreBuilder.MonthOf(e.Date), e.Value)), advances);

        DateTimeOffset? lastViewed = currentUser.UserId is { } me
            ? await db.DreViewMarks.AsNoTracking().Where(m => m.UserId == me).Select(m => (DateTimeOffset?)m.ViewedAt).FirstOrDefaultAsync(cancellationToken)
            : null;
        DreChangeSummary? changes = null;
        if (lastViewed is { } since)
        {
            var found = await ChangeListAsync(since, from, to, cancellationToken);
            if (found.Count > 0)
            {
                changes = new DreChangeSummary(since, found.Count, found.Months);
            }
        }

        var changed = changes?.Months.ToHashSet() ?? [];
        var monthViews = months.Select(m => new DreMonth(m, routed.IsProvisional(m), changed.Contains(m))).ToList();
        var notices = classify.Select(id => (Id: id, Row: statement.Rows.FirstOrDefault(r => r.Key == DreBuilder.AccountKey(id))))
            .Where(x => x.Row is { Total: not 0 }).Select(x => new DreClassifyNotice(x.Id, x.Row!.Code ?? string.Empty, x.Row.Label, x.Row.Total)).ToList();
        return new DreReport(from, to, monthViews, statement.Rows, monthViews.Any(m => m.IsProvisional), entries.Any(e => e.Value != 0) || advances.Any(a => a != 0),
            notices, changes, lastViewed, await parameters.PendingCountAsync(cancellationToken));
    }

    /// <summary>"A marca da consulta é atualizada ao abrir" (CA 9): a tela chama depois de mostrar o aviso da consulta anterior.</summary>
    public async Task MarkViewedAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } me)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var mark = await db.DreViewMarks.FirstOrDefaultAsync(m => m.UserId == me, cancellationToken);
        if (mark is null)
        {
            db.DreViewMarks.Add(new DreViewMark { UserId = me, ViewedAt = now });
        }
        else
        {
            mark.ViewedAt = now;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, ViewMarkIndex))
        {
            // Duas abas abriram juntas: a outra já gravou a marca, que vale igual.
        }
    }

    internal sealed record PeriodResult(DateOnly From, DateOnly To, IReadOnlyList<DateOnly> Months, Error? Error);

    /// <summary>Meses inteiros (CA 2): padrão os 12 meses até o mês corrente; de 1 a 24 meses.</summary>
    internal PeriodResult Period(DateOnly? fromQuery, DateOnly? toQuery)
    {
        var today = BusinessTime.Today(clock);
        var toMonth = DreBuilder.MonthOf(toQuery ?? today);
        var from = DreBuilder.MonthOf(fromQuery ?? toMonth.AddMonths(-11));
        if (from > toMonth)
        {
            return new PeriodResult(from, toMonth, [], ReceivableService.Invalid("from", "O início do período precisa ser antes do fim."));
        }

        var months = DreBuilder.MonthsBetween(from, toMonth);
        if (months.Count > MaxMonths)
        {
            return new PeriodResult(from, toMonth, [], ReceivableService.Invalid("from", $"Escolha um período de até {MaxMonths} meses."));
        }

        return new PeriodResult(from, toMonth.AddMonths(1).AddDays(-1), months, null);
    }

    /// <summary>Plano de contas inteiro (inclusive inativas e excluídas: o documento antigo fica no mês dele) e as contas "A classificar".</summary>
    internal async Task<(List<DreAccount> Accounts, List<Guid> Classify)> AccountsAsync(CancellationToken ct)
    {
        var rows = await db.Accounts.IgnoreQueryFilters().AsNoTracking().Select(a => new { a.Id, a.Code, a.Name, a.ParentId, a.DreGroup, a.SeedKey, a.IsDeleted }).ToListAsync(ct);
        var accounts = rows.Select(a => new DreAccount(a.Id, a.Code, a.Name, a.ParentId, a.DreGroup)).ToList();
        var classify = rows.Where(a => a.SeedKey is { } key ? ClassifyCodes.Contains(key) : !a.IsDeleted && ClassifyCodes.Contains(a.Code)).Select(a => a.Id).ToList();
        return (accounts, classify);
    }

    /// <summary>Todas as entradas do DRE de <paramref name="from"/> a <paramref name="to"/>, agregadas por conta e dia.</summary>
    internal async Task<(List<DreLedgerEntry> Entries, RoutedRevenue Revenue)> LedgerAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var routed = await revenue.RecognizeAsync(from, to, null, ct);
        var entries = routed.Amounts.Select(a => new DreLedgerEntry(a.AccountId, a.Date, a.Amount)).ToList();

        // Títulos a pagar (CA 6): valor bruto, na competência (recorrente) ou na data do documento; dois ramos para usar os índices.
        var payables = await db.Payables.AsNoTracking()
            .Where(p => !DreRules.ExcludedPayableStatuses.Contains(p.Status) && p.Origin != DocumentOrigin.Opening
                && ((p.Competence != null && p.Competence >= from && p.Competence <= to) || (p.Competence == null && p.IssueDate >= from && p.IssueDate <= to)))
            .GroupBy(p => new { p.AccountId, Date = p.Competence ?? p.IssueDate })
            .Select(g => new { g.Key.AccountId, g.Key.Date, Sum = g.Sum(p => p.Amount) }).ToListAsync(ct);
        entries.AddRange(payables.Select(p => new DreLedgerEntry(p.AccountId, p.Date, -p.Sum)));

        // Lançamentos bancários sem OP (CA 7): o valor já tem o sinal do tipo; estornados não entram.
        var bank = await db.BankEntries.AsNoTracking().Where(e => e.ReversedAt == null && e.Date >= from && e.Date <= to)
            .GroupBy(e => new { e.AccountId, e.Date }).Select(g => new { g.Key.AccountId, g.Key.Date, Sum = g.Sum(e => e.Amount) }).ToListAsync(ct);
        entries.AddRange(bank.Select(e => new DreLedgerEntry(e.AccountId, e.Date, e.Sum)));

        // Baixas (CA 8): juros, multa e descontos, no mês da baixa; estornadas não entram.
        var accounts = await parameters.SettlementAccountsAsync(ct);
        var received = await db.ReceivableSettlements.AsNoTracking()
            .Where(s => s.ReversedAt == null && s.Date >= from && s.Date <= to && (s.Interest != 0 || s.Fine != 0 || s.Discount != 0))
            .GroupBy(s => s.Date).Select(g => new { Date = g.Key, Charges = g.Sum(s => s.Interest + s.Fine), Discount = g.Sum(s => s.Discount) }).ToListAsync(ct);
        foreach (var s in received)
        {
            entries.AddRange(DreRules.ReceivableSettlement(s.Charges, s.Discount, accounts).Select(x => new DreLedgerEntry(x.AccountId, s.Date, x.Value)));
        }

        var paid = await db.PayableSettlements.AsNoTracking()
            .Where(s => s.ReversedAt == null && s.Date >= from && s.Date <= to && (s.Interest != 0 || s.Fine != 0 || s.Discount != 0))
            .GroupBy(s => s.Date).Select(g => new { Date = g.Key, Charges = g.Sum(s => s.Interest + s.Fine), Discount = g.Sum(s => s.Discount) }).ToListAsync(ct);
        foreach (var s in paid)
        {
            entries.AddRange(DreRules.PayableSettlement(s.Charges, s.Discount, accounts).Select(x => new DreLedgerEntry(x.AccountId, s.Date, x.Value)));
        }

        return (entries, routed);
    }

    private static string Month(DateOnly month) => month.ToString("MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
}
