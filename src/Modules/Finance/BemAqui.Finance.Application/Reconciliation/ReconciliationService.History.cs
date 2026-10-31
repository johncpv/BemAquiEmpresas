using BemAqui.BuildingBlocks.Authorization;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application.Reconciliation;

public sealed partial class ReconciliationService
{
    /// <summary>Histórico da linha (CA 15): quem importou, quando, de onde, e cada conciliação (a sugestão aceita ou "manual", quem, o motivo).</summary>
    public async Task<Result<ReconHistory>> LineHistoryAsync(Guid accountId, Guid lineId, CancellationToken cancellationToken = default)
    {
        var line = await db.BankStatementLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lineId && l.BankAccountId == accountId, cancellationToken);
        if (line is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Linha do extrato não encontrada nesta conta.");
        }

        var batch = await db.BankStatementBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == line.BatchId, cancellationToken);
        var importer = batch?.ImportedBy is { } by ? await users.DisplayNameAsync(by, cancellationToken) : null;
        var ids = await db.BankReconciliationLinks.AsNoTracking().Where(k => k.StatementLineId == lineId).Select(k => k.ReconciliationId).Distinct().ToListAsync(cancellationToken);
        return new ReconHistory(importer ?? (batch?.ImportedBy is null ? "o sistema (API do banco)" : null), line.ImportedAt, line.Source, await EntriesAsync(accountId, ids, cancellationToken));
    }

    /// <summary>Histórico da transação (CA 15): as conciliações em que ela entrou.</summary>
    public async Task<Result<ReconHistory>> TransactionHistoryAsync(Guid accountId, Guid transactionId, CancellationToken cancellationToken = default)
    {
        if (!await db.FinancialTransactions.AnyAsync(t => t.Id == transactionId && t.BankAccountId == accountId, cancellationToken))
        {
            return Error.NotFound(Error.Codes.NotFound, "Transação não encontrada nesta conta.");
        }

        var ids = await db.BankReconciliationLinks.AsNoTracking().Where(k => k.TransactionId == transactionId).Select(k => k.ReconciliationId).Distinct().ToListAsync(cancellationToken);
        return new ReconHistory(null, null, null, await EntriesAsync(accountId, ids, cancellationToken));
    }

    private async Task<List<ReconHistoryEntry>> EntriesAsync(Guid accountId, List<Guid> ids, CancellationToken ct)
    {
        var reconciliations = await db.BankReconciliations.AsNoTracking().Where(r => ids.Contains(r.Id) && r.BankAccountId == accountId).OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        var full = await permissions.HasPermissionAsync("finance", StandardActions.Edit, ct);
        var described = (await DescribeAsync(reconciliations, null, full, ct)).ToDictionary(d => d.Id);
        var names = await NamesAsync(reconciliations.Select(r => r.UndoneBy), ct);
        return reconciliations.Select(r => new ReconHistoryEntry(r.Id, r.Kind, r.Confidence, r.Reason, r.ClosedPeriodReason, described[r.Id].CreatedByName, r.CreatedAt,
            r.UndoneBy is { } by ? names.GetValueOrDefault(by) : null, r.UndoneAt, r.UndoReason, described[r.Id].Lines, described[r.Id].Transactions)).ToList();
    }

    /// <summary>Regras de tipo do extrato (CA 2): ativas primeiro; seed vazio.</summary>
    public async Task<IReadOnlyList<BankStatementRuleView>> RulesAsync(CancellationToken cancellationToken = default)
    {
        var rules = await db.BankStatementRules.AsNoTracking().OrderByDescending(r => r.IsActive).ThenBy(r => r.CreatedAt).ToListAsync(cancellationToken);
        var accounts = (await chart.FindAsync(rules.Where(r => r.AccountId != null).Select(r => r.AccountId!.Value).Distinct().ToList(), cancellationToken)).ToDictionary(a => a.Id, a => a.Name);
        return rules.Select(r => RuleView(r, r.AccountId is { } a ? accounts.GetValueOrDefault(a) : null)).ToList();
    }

    /// <summary>Criar regra ("Criar regra a partir desta linha", CA 2) com <c>finance.edit</c>: palavra e/ou tipo do banco, tipo do lançamento e conta.</summary>
    public async Task<Result<BankStatementRuleView>> CreateRuleAsync(BankStatementRuleInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var rule = new BankStatementRule { CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId };
        if (await ApplyRuleAsync(rule, input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        db.BankStatementRules.Add(rule);
        await db.SaveChangesAsync(cancellationToken);
        return await RuleViewAsync(rule, cancellationToken);
    }

    public async Task<Result<BankStatementRuleView>> UpdateRuleAsync(Guid id, BankStatementRuleInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var rule = await db.BankStatementRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rule is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Regra não encontrada.");
        }

        db.Entry(rule).Property(r => r.Version).OriginalValue = input.Version;
        if (await ApplyRuleAsync(rule, input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await RuleViewAsync(rule, cancellationToken);
    }

    private async Task<Error?> ApplyRuleAsync(BankStatementRule rule, BankStatementRuleInput input, CancellationToken ct)
    {
        var keyword = string.IsNullOrWhiteSpace(input.Keyword) ? null : input.Keyword.Trim();
        if (keyword is { Length: > 60 } or { Length: < 3 })
        {
            return ReceivableService.Invalid("keyword", "Informe uma palavra de 3 a 60 caracteres.");
        }

        if (keyword is null && input.LineKind is null)
        {
            return ReceivableService.Invalid("keyword", "Informe a palavra da descrição, o tipo do banco ou os dois.");
        }

        if (!ReconciliationRules.OutflowKinds.Contains(input.EntryKind) && !ReconciliationRules.InflowKinds.Contains(input.EntryKind))
        {
            return ReceivableService.Invalid("entryKind", "Escolha tarifa bancária, IOF, juros pagos, juros recebidos, rendimento ou outra entrada.");
        }

        if (input.AccountId is { } account)
        {
            var options = await chart.PostableAsync(BankEntry.IsOutflow(input.EntryKind) ? AccountType.Expense : AccountType.Revenue, ct);
            if (options.All(o => o.Id != account))
            {
                return ReceivableService.Invalid("accountId", BankEntry.IsOutflow(input.EntryKind) ? "Escolha uma conta de despesa analítica." : "Escolha uma conta de receita analítica.");
            }
        }

        rule.Keyword = keyword;
        rule.LineKind = input.LineKind;
        rule.EntryKind = input.EntryKind;
        rule.AccountId = input.AccountId;
        rule.IsActive = input.IsActive;
        return null;
    }

    private async Task<BankStatementRuleView> RuleViewAsync(BankStatementRule rule, CancellationToken ct) =>
        RuleView(rule, rule.AccountId is { } a && await chart.FindAsync([a], ct) is { Count: > 0 } found ? found[0].Name : null);

    private static BankStatementRuleView RuleView(BankStatementRule r, string? accountName) =>
        new(r.Id, r.Keyword, r.LineKind, r.EntryKind, r.AccountId, accountName, r.IsActive, r.Version);
}
