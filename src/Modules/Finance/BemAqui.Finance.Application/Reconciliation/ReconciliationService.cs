using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Application.Statements;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application.Reconciliation;

/// <summary>
/// Conciliação bancária (BNK-03b, R-FIN-8): o extrato do banco (BNK-03a) lado a lado com as transações do sistema, uma linha por par, com
/// a sugestão do <see cref="ReconciliationSuggester"/>; conciliar 1×N ou N×1 com a soma igual; lançar sem OP a partir da linha pelo
/// <see cref="BankMovementService"/> (FIN-04) e baixar o título a receber pelo <see cref="ReceivableService"/> (FIN-01), na mesma transação;
/// desfazer sem estornar; fechar o período com <c>finance.approve</c>. A conciliação nunca cria pagamento nem transação: só vincula ou
/// chama a baixa de recebimento e o lançamento sem OP (R-FIN-5, CA 8; teste de arquitetura). Quem importou o OFX não confirma a conciliação
/// das linhas dele (D13, CA 5).
/// </summary>
public sealed partial class ReconciliationService(
    IFinanceDb db,
    BankMovementService movements,
    ReceivableService receivables,
    CashFlowService balances,
    IChartOfAccounts chart,
    IParties parties,
    IPermissionChecker permissions,
    ICurrentUser currentUser,
    IUserDirectory users,
    INotificationService notifications,
    IFinanceParameters parameters,
    ReconciliationPeriodGuard period,
    TimeProvider clock)
{
    public const string Route = "/app/financeiro/conciliacao";
    public const string SegregationCode = "segregation_required";
    public const string SegregationMessage = "Quem importou o extrato não confirma a conciliação dele. Peça a outra pessoa do Financeiro.";
    public const string NotificationType = "finance.reconciliation";

    /// <summary>Transação sem linha do banco há mais que isto entra em alerta (CA 12).</summary>
    public const int StaleDays = 5;

    public static string Link(Guid bankAccountId) => $"{Route}?conta={bankAccountId}";

    private DateOnly Today => BusinessTime.Today(clock);

    /// <summary>
    /// Tela da conciliação (CA 1 a 3, 9, 11 e 12): pares sugeridos, linhas sem par com a ação sugerida, transações sem linha do banco, as
    /// conciliações do período e os totais. A contraparte sai inteira só com <c>finance.edit</c>.
    /// </summary>
    public async Task<Result<ReconciliationView>> ViewAsync(Guid accountId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var account = await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        var start = from ?? new DateOnly(Today.Year, Today.Month, 1);
        var end = to ?? start.AddMonths(1).AddDays(-1);
        if (end < start || end.DayNumber - start.DayNumber > 400)
        {
            return ReceivableService.Invalid("to", "Escolha um período de até 400 dias, com o fim depois do início.");
        }

        var full = await permissions.HasPermissionAsync("finance", StandardActions.Edit, cancellationToken);
        var lines = await db.BankStatementLines.AsNoTracking()
            .Where(l => l.BankAccountId == accountId && l.Date >= start && l.Date <= end && l.Status != StatementLineStatus.Undone)
            .OrderBy(l => l.Date).ThenBy(l => l.Sequence).ToListAsync(cancellationToken);
        var pending = lines.Where(l => l.Status == StatementLineStatus.Pending).ToList();
        var open = await db.FinancialTransactions.AsNoTracking()
            .Where(t => t.BankAccountId == accountId && !t.Reconciled && t.Date >= start.AddDays(-ReconciliationSuggester.WindowDays) && t.Date <= end.AddDays(ReconciliationSuggester.WindowDays))
            .OrderBy(t => t.Date).ThenBy(t => t.CreatedAt).ToListAsync(cancellationToken);
        var context = await ContextAsync(open, cancellationToken);
        var suggestions = ReconciliationSuggester.Suggest(pending.Select(ToSuggestion), open.Select(context.ToSuggestion));
        var lineViews = await LineViewsAsync(lines, full, cancellationToken);
        var byId = open.ToDictionary(t => t.Id);
        var pairs = suggestions.Select(s => new ReconPair(lineViews[s.LineId], context.View(byId[s.TransactionId]), s.Confidence, s.Reason)).ToList();
        var paired = suggestions.Select(s => s.LineId).ToHashSet();
        var suggestedTransactions = suggestions.Select(s => s.TransactionId).ToHashSet();

        var rules = await db.BankStatementRules.AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.CreatedAt).ToListAsync(cancellationToken);
        var unpaired = pending.Where(l => !paired.Contains(l.Id)).ToList();
        var actions = await ActionsAsync(unpaired, rules, cancellationToken);
        var repeatedKinds = unpaired.GroupBy(l => (l.Kind, l.Amount < 0)).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var bankOnly = unpaired.Select(l =>
        {
            var recognized = ReconciliationRules.EntryKindFor(l.Description, l.Kind, l.Amount, rules);
            var canEntry = l.Amount > 0 || recognized is not null;
            var suggestRule = recognized is { Rule: null } || (recognized is null && repeatedKinds.Contains((l.Kind, l.Amount < 0)) && l.Kind != StatementLineKind.Other);
            return new ReconUnpairedLine(lineViews[l.Id], actions.GetValueOrDefault(l.Id), canEntry, suggestRule);
        }).ToList();

        var systemOnly = open.Where(t => t.Date >= start && t.Date <= end && !suggestedTransactions.Contains(t.Id))
            .Select(t =>
            {
                var days = Math.Max(0, Today.DayNumber - t.Date.DayNumber);
                return new ReconUnpairedTransaction(context.View(t), days, days > StaleDays);
            }).ToList();

        var closedUntil = await period.ClosedUntilAsync(accountId, cancellationToken);
        var done = await DoneAsync(accountId, lines.Where(l => l.Status == StatementLineStatus.Reconciled).Select(l => l.Id).ToList(), closedUntil, full, cancellationToken);
        var totals = await TotalsAsync(accountId, lines, end, cancellationToken);
        var hasStatement = lines.Count > 0 || await db.BankStatementLines.AnyAsync(l => l.BankAccountId == accountId && l.Status != StatementLineStatus.Undone, cancellationToken);
        var reconciledUntil = await ReconciledUntilAsync(accountId, closedUntil, cancellationToken);
        var cap = (await parameters.LimitsAsync(cancellationToken)).OfxBankEntryCap;
        return new ReconciliationView(account.Id, account.Name, start, end, hasStatement, pairs, bankOnly, systemOnly, done, totals, reconciledUntil, closedUntil, cap, !full);
    }

    /// <summary>
    /// Transações para escolher à mão (CA 4): não conciliadas da conta, até 30 dias antes ou depois da data, do mesmo sinal; com
    /// <paramref name="paymentsOnly"/>, só pagamentos de OP ("Vincular a uma OP paga", CA 8).
    /// </summary>
    public async Task<Result<IReadOnlyList<ReconTransaction>>> CandidatesAsync(Guid accountId, DateOnly date, bool outflow, bool paymentsOnly, CancellationToken cancellationToken = default)
    {
        if (!await db.BankAccounts.AnyAsync(a => a.Id == accountId, cancellationToken))
        {
            return NotFound();
        }

        var query = db.FinancialTransactions.AsNoTracking()
            .Where(t => t.BankAccountId == accountId && !t.Reconciled && t.Date >= date.AddDays(-30) && t.Date <= date.AddDays(30) && (outflow ? t.Amount < 0 : t.Amount > 0));
        if (paymentsOnly)
        {
            query = query.Where(t => t.PayableId != null && t.ReversesTransactionId == null);
        }

        var rows = await query.OrderBy(t => t.Date).ThenBy(t => t.CreatedAt).Take(200).ToListAsync(cancellationToken);
        var context = await ContextAsync(rows, cancellationToken);
        return rows.Select(context.View).ToList();
    }

    /// <summary>Linhas pendentes para escolher à mão (N linhas para 1 transação): até 30 dias da data, do mesmo sinal.</summary>
    public async Task<Result<IReadOnlyList<ReconLine>>> PendingLinesAsync(Guid accountId, DateOnly date, bool outflow, CancellationToken cancellationToken = default)
    {
        if (!await db.BankAccounts.AnyAsync(a => a.Id == accountId, cancellationToken))
        {
            return NotFound();
        }

        var rows = await db.BankStatementLines.AsNoTracking()
            .Where(l => l.BankAccountId == accountId && l.Status == StatementLineStatus.Pending && l.Date >= date.AddDays(-30) && l.Date <= date.AddDays(30) && (outflow ? l.Amount < 0 : l.Amount > 0))
            .OrderBy(l => l.Date).ThenBy(l => l.Sequence).Take(200).ToListAsync(cancellationToken);
        var full = await permissions.HasPermissionAsync("finance", StandardActions.Edit, cancellationToken);
        var views = await LineViewsAsync(rows, full, cancellationToken);
        return rows.Select(r => views[r.Id]).ToList();
    }

    private static SuggestionLine ToSuggestion(BankStatementLine l) => new(l.Id, l.Date, l.Amount, l.Description, l.CounterpartyDocument, l.ExternalId, l.BankVerified);

    /// <summary>
    /// "Sem par no banco" (CA 2, 3 e 8): entrada de cliente com título em aberto do mesmo valor → baixar o título; tarifa, IOF, juros ou
    /// rendimento (regra ou tipo do banco) → lançar sem OP com a conta do plano; outra saída → só o caminho da OP.
    /// </summary>
    private async Task<Dictionary<Guid, LineAction>> ActionsAsync(List<BankStatementLine> unpaired, List<BankStatementRule> rules, CancellationToken ct)
    {
        var result = new Dictionary<Guid, LineAction>();
        var credits = unpaired.Where(l => l.Amount > 0 && BrazilianDocuments.Normalize(l.CounterpartyDocument).Length is 11 or 14).ToList();
        if (credits.Count > 0)
        {
            var amounts = credits.Select(l => l.Amount).Distinct().ToList();
            var titles = await db.Receivables.AsNoTracking()
                .Where(r => (r.Status == ReceivableStatus.Open || r.Status == ReceivableStatus.PartiallyPaid) && amounts.Contains(r.Amount - r.PaidAmount))
                .OrderBy(r => r.DueDate).ThenBy(r => r.Number).Take(500).ToListAsync(ct);
            var customers = (await parties.FindManyAsync(titles.Select(t => t.PartyId).Distinct().ToList(), ct)).ToDictionary(p => p.Id);
            var taken = new HashSet<Guid>();
            foreach (var line in credits)
            {
                var document = BrazilianDocuments.Normalize(line.CounterpartyDocument);
                var title = titles.FirstOrDefault(t => !taken.Contains(t.Id) && t.Balance == line.Amount
                    && customers.TryGetValue(t.PartyId, out var c) && BrazilianDocuments.Normalize(c.Document) == document);
                if (title is not null)
                {
                    taken.Add(title.Id);
                    var name = customers[title.PartyId].TradeName ?? customers[title.PartyId].LegalName;
                    result[line.Id] = new LineAction(LineActionKind.Settle, $"Baixar o título {title.Number} de {name}", null, null, null, null, title.Id, title.Number, name, title.Balance, title.Version);
                }
            }
        }

        var accounts = new Dictionary<BankEntryKind, (Guid? Id, string? Name)>();
        foreach (var line in unpaired.Where(l => !result.ContainsKey(l.Id)))
        {
            if (ReconciliationRules.EntryKindFor(line.Description, line.Kind, line.Amount, rules) is { } recognized)
            {
                (Guid? Id, string? Name) planAccount;
                if (recognized.Rule?.AccountId is { } ruleAccount)
                {
                    planAccount = (ruleAccount, await chart.FindAsync([ruleAccount], ct) is { Count: > 0 } found ? found[0].Name : null);
                }
                else if (!accounts.TryGetValue(recognized.Kind, out planAccount))
                {
                    planAccount = await MostUsedAccountAsync(recognized.Kind, ct);
                    accounts[recognized.Kind] = planAccount;
                }

                result[line.Id] = new LineAction(LineActionKind.BankEntry, $"Lançar como {BankMovementService.KindName(recognized.Kind)} sem OP", recognized.Kind, planAccount.Id, planAccount.Name,
                    recognized.Rule?.Id, null, null, null, null, null);
            }
            else if (line.Amount < 0)
            {
                result[line.Id] = new LineAction(LineActionKind.PaymentOrder, "Vincular a uma OP paga ou abrir OP para regularizar", null, null, null, null, null, null, null, null, null);
            }
        }

        return result;
    }

    /// <summary>Conta do plano mais usada nos lançamentos sem OP do tipo (R-PRP-1); sem histórico, a sugerida pelo tipo (FIN-04 CA 11).</summary>
    private async Task<(Guid? Id, string? Name)> MostUsedAccountAsync(BankEntryKind kind, CancellationToken ct)
    {
        var used = await db.BankEntries.AsNoTracking().Where(e => e.Kind == kind && e.ReversedAt == null)
            .GroupBy(e => e.AccountId).OrderByDescending(g => g.Count()).Select(g => (Guid?)g.Key).FirstOrDefaultAsync(ct);
        var options = await chart.PostableAsync(BankEntry.IsOutflow(kind) ? AccountType.Expense : AccountType.Revenue, ct);
        var option = used is { } id ? options.FirstOrDefault(o => o.Id == id) : null;
        option ??= options.FirstOrDefault(o => o.Code == BankEntry.SuggestedAccountCode(kind));
        return option is null ? (null, null) : (option.Id, option.Name);
    }

    private async Task<ReconTotals> TotalsAsync(Guid accountId, List<BankStatementLine> lines, DateOnly end, CancellationToken ct)
    {
        var balance = await db.BankStatementBalances.AsNoTracking().Where(b => b.BankAccountId == accountId && b.Date <= end)
            .OrderByDescending(b => b.Date).ThenBy(b => b.Source == StatementSource.Api ? 0 : 1).FirstOrDefaultAsync(ct);
        decimal? system = balance is null ? null : await balances.BankBalanceAsync([accountId], balance.Date, ct);
        return new ReconTotals(lines.Count(l => l.Status == StatementLineStatus.Pending), lines.Count(l => l.Status == StatementLineStatus.Reconciled),
            lines.Count(l => l.Status == StatementLineStatus.Ignored), balance?.Date, balance?.Balance, system, balance is null ? null : balance.Balance - system);
    }

    /// <summary>Linhas na tela: quem importou o OFX, a segregação (CA 5) e a contraparte mascarada para quem só vê.</summary>
    private async Task<Dictionary<Guid, ReconLine>> LineViewsAsync(IReadOnlyCollection<BankStatementLine> lines, bool full, CancellationToken ct)
    {
        var batchIds = lines.Select(l => l.BatchId).Distinct().ToList();
        var importers = await db.BankStatementBatches.AsNoTracking().Where(b => batchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.ImportedBy, ct);
        var names = await NamesAsync(importers.Values, ct);
        var me = currentUser.UserId;
        return lines.ToDictionary(l => l.Id, l =>
        {
            var importer = importers.GetValueOrDefault(l.BatchId);
            var blocked = l.Source == StatementSource.Ofx && importer is { } by && by == me;
            return new ReconLine(l.Id, l.Date, l.Amount, l.Description, l.Kind, full ? l.CounterpartyName : CounterpartyMask.Name(l.CounterpartyName),
                full ? l.CounterpartyDocument : CounterpartyMask.Document(l.CounterpartyDocument), l.Source, l.BankVerified, l.Status,
                importer is { } who ? names.GetValueOrDefault(who) : null, blocked, blocked ? SegregationMessage : null, l.Version);
        });
    }

    private async Task<Dictionary<Guid, string?>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string?>();
        foreach (var id in ids.OfType<Guid>().Distinct())
        {
            names[id] = await users.DisplayNameAsync(id, ct);
        }

        return names;
    }

    private static Error NotFound() => Error.NotFound("bank_account_not_found", "Conta não encontrada.");
}
