using BemAqui.BuildingBlocks.Authorization;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application.Reconciliation;

public sealed partial class ReconciliationService
{
    public const string ManualReason = "Escolhida à mão";
    private const int MaxItems = 50;

    /// <summary>
    /// Conciliar (CA 4, 5 e 14): uma linha com uma ou mais transações, ou várias linhas com uma transação, com a soma igual. Linha de OFX
    /// importada por quem confirma: 403 <c>segregation_required</c>. Transação de outra conta: 422. Linha ou transação que mudou: 409 pelo
    /// <c>xmin</c> das duas.
    /// </summary>
    public async Task<Result<ReconcileResult>> ReconcileAsync(Guid accountId, ReconcileInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await LoadAsync(accountId, input.Lines ?? [], input.Transactions ?? [], cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var (lines, transactions) = loaded.Value!;
        if (ReconciliationRules.SumError([.. lines.Select(l => l.Amount)], [.. transactions.Select(t => t.Amount)]) is { } sum)
        {
            return Error.Validation("reconciliation_sum_mismatch", sum, new Dictionary<string, string[]> { ["transactions"] = [sum] });
        }

        if (await SegregationAsync(lines, cancellationToken) is { } segregation)
        {
            return segregation;
        }

        var (kind, confidence, reason) = (ReconciliationKind.Manual, (ReconciliationConfidence?)null, ManualReason);
        if (input.Accepted && lines.Count == 1 && transactions.Count == 1
            && await SuggestionForAsync(lines[0], cancellationToken) is { } suggested && suggested.TransactionId == transactions[0].Id)
        {
            (kind, confidence, reason) = (ReconciliationKind.Suggested, suggested.Confidence, suggested.Reason);
        }

        var reconciliation = Record(accountId, kind, confidence, reason, null, lines, transactions);
        await db.SaveChangesAsync(cancellationToken);
        return Result(reconciliation, lines, transactions);
    }

    /// <summary>
    /// "Aceitar todas as de confiança alta" (CA 4): só os pares que ainda são sugestão Alta; cada par grava sozinho (o que mudou ou é da
    /// segregação volta como recusado, com o motivo).
    /// </summary>
    public async Task<Result<AcceptHighResult>> AcceptHighAsync(Guid accountId, AcceptHighInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Pairs is not { Count: > 0 and <= 500 })
        {
            return ReceivableService.Invalid("pairs", "Escolha de 1 a 500 pares.");
        }

        if (!await db.BankAccounts.AnyAsync(a => a.Id == accountId, cancellationToken))
        {
            return NotFound();
        }

        var lineIds = input.Pairs.Select(p => p.LineId).Distinct().ToList();
        var dates = await db.BankStatementLines.AsNoTracking().Where(l => lineIds.Contains(l.Id) && l.BankAccountId == accountId).Select(l => l.Date).ToListAsync(cancellationToken);
        var suggestions = dates.Count == 0
            ? new Dictionary<Guid, PairSuggestion>()
            : (await SuggestionsAsync(accountId, dates.Min(), dates.Max(), cancellationToken)).ToDictionary(s => s.LineId);
        var refused = new List<RefusedPair>();
        var done = 0;
        foreach (var pair in input.Pairs.DistinctBy(p => p.LineId))
        {
            if (!suggestions.TryGetValue(pair.LineId, out var s) || s.TransactionId != pair.TransactionId || s.Confidence != ReconciliationConfidence.High)
            {
                refused.Add(new RefusedPair(pair.LineId, "O par não é mais uma sugestão de confiança alta. Recarregue."));
                continue;
            }

            var loaded = await LoadAsync(accountId, [new VersionedRef(pair.LineId, pair.LineVersion)], [new VersionedRef(pair.TransactionId, pair.TransactionVersion)], cancellationToken);
            if (loaded.IsFailure)
            {
                refused.Add(new RefusedPair(pair.LineId, loaded.Error!.Message));
                db.ChangeTracker.Clear();
                continue;
            }

            var (lines, transactions) = loaded.Value!;
            if (await SegregationAsync(lines, cancellationToken) is not null || lines[0].Amount != transactions[0].Amount)
            {
                refused.Add(new RefusedPair(pair.LineId, lines[0].Amount != transactions[0].Amount ? "Os valores não batem." : SegregationMessage));
                db.ChangeTracker.Clear();
                continue;
            }

            Record(accountId, ReconciliationKind.Suggested, s.Confidence, s.Reason, null, lines, transactions);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                done++;
            }
            catch (DbUpdateConcurrencyException)
            {
                refused.Add(new RefusedPair(pair.LineId, "A linha ou a transação mudou enquanto você conciliava. Recarregue."));
            }

            db.ChangeTracker.Clear();
        }

        return new AcceptHighResult(done, refused);
    }

    /// <summary>
    /// "Desfazer conciliação" (CA 10): com motivo, as linhas voltam a pendentes e as transações a não conciliadas, sem estornar nada; o
    /// lançamento sem OP e a baixa criados pela conciliação continuam (saem só pelo estorno da FIN-04 ou da FIN-01). No período fechado,
    /// exige <c>finance.approve</c> e avisa o Proprietário (CA 11).
    /// </summary>
    public async Task<Result<ReconcileResult>> UndoAsync(Guid accountId, Guid reconciliationId, UndoReconciliationInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var reconciliation = await db.BankReconciliations.FirstOrDefaultAsync(r => r.Id == reconciliationId && r.BankAccountId == accountId, cancellationToken);
        if (reconciliation is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Conciliação não encontrada nesta conta.");
        }

        db.Entry(reconciliation).Property(r => r.Version).OriginalValue = input.Version;
        if (reconciliation.IsUndone)
        {
            return Error.Conflict("reconciliation_undone", "Esta conciliação já foi desfeita. Recarregue.");
        }

        var reason = input.Reason?.Trim() ?? string.Empty;
        if (reason.Length is 0 or > 200)
        {
            return ReceivableService.Invalid("reason", "Informe o motivo (até 200 caracteres).");
        }

        var links = await db.BankReconciliationLinks.AsNoTracking().Where(k => k.ReconciliationId == reconciliationId).ToListAsync(cancellationToken);
        var lineIds = links.Select(k => k.StatementLineId).Distinct().ToList();
        var transactionIds = links.Select(k => k.TransactionId).Distinct().ToList();
        var lines = await db.BankStatementLines.Where(l => lineIds.Contains(l.Id)).ToListAsync(cancellationToken);
        var transactions = await db.FinancialTransactions.Where(t => transactionIds.Contains(t.Id)).ToListAsync(cancellationToken);
        if (lines.Count > 0 && await period.CheckAsync(accountId, lines.Min(l => l.Date), reason, "reason", "desfazer a conciliação", cancellationToken) is { } closed)
        {
            return closed;
        }

        foreach (var line in lines.Where(l => l.Status == StatementLineStatus.Reconciled))
        {
            line.Unreconcile();
        }

        foreach (var transaction in transactions)
        {
            transaction.Unreconcile();
        }

        reconciliation.Undo(currentUser.UserId, clock.GetUtcNow(), reason);
        await db.SaveChangesAsync(cancellationToken);
        return Result(reconciliation, lines, transactions);
    }

    /// <summary>Linhas pendentes e transações não conciliadas da conta, rastreadas, com a versão que a tela abriu.</summary>
    private async Task<Result<(List<BankStatementLine> Lines, List<FinancialTransaction> Transactions)>> LoadAsync(
        Guid accountId, IReadOnlyList<VersionedRef> lineRefs, IReadOnlyList<VersionedRef> transactionRefs, CancellationToken ct)
    {
        if (lineRefs.Count is 0 or > MaxItems || transactionRefs.Count is 0 or > MaxItems)
        {
            return ReceivableService.Invalid(lineRefs.Count == 0 ? "lines" : "transactions", "Escolha a linha do banco e ao menos uma transação do sistema (até 50).");
        }

        if (!await db.BankAccounts.AnyAsync(a => a.Id == accountId, ct))
        {
            return NotFound();
        }

        var lineIds = lineRefs.Select(r => r.Id).Distinct().ToList();
        var transactionIds = transactionRefs.Select(r => r.Id).Distinct().ToList();
        var lines = await db.BankStatementLines.Where(l => lineIds.Contains(l.Id) && l.BankAccountId == accountId && l.Status != StatementLineStatus.Undone).ToListAsync(ct);
        if (lines.Count != lineIds.Count)
        {
            return Error.NotFound(Error.Codes.NotFound, "Linha do extrato não encontrada nesta conta.");
        }

        var transactions = await db.FinancialTransactions.Where(t => transactionIds.Contains(t.Id)).ToListAsync(ct);
        if (transactions.Count != transactionIds.Count || transactions.Any(t => t.BankAccountId != accountId))
        {
            // IDOR (CA 13): transação de outra conta (ou inexistente) é recusada sem dizer de quem é.
            return ReceivableService.Invalid("transactions", "Escolha transações desta conta.");
        }

        foreach (var line in lines)
        {
            db.Entry(line).Property(l => l.Version).OriginalValue = lineRefs.First(r => r.Id == line.Id).Version;
        }

        foreach (var transaction in transactions)
        {
            db.Entry(transaction).Property(t => t.Version).OriginalValue = transactionRefs.First(r => r.Id == transaction.Id).Version;
        }

        if (lines.Any(l => l.Status != StatementLineStatus.Pending))
        {
            return Error.Conflict("bank_statement_line_not_pending", "A linha já foi conciliada ou ignorada. Recarregue.");
        }

        if (transactions.Any(t => t.Reconciled))
        {
            return Error.Conflict("transaction_reconciled", "A transação já está conciliada. Recarregue.");
        }

        var ordered = lineRefs.Select(r => r.Id).Distinct().Select(id => lines.First(l => l.Id == id)).ToList();
        var orderedTransactions = transactionRefs.Select(r => r.Id).Distinct().Select(id => transactions.First(t => t.Id == id)).ToList();
        return (ordered, orderedTransactions);
    }

    /// <summary>
    /// Segregação (D13, CA 5): linha de OFX importada por quem está confirmando. Linhas da API (importadas pelo sistema ou pelo "Buscar
    /// agora") não têm essa restrição; o Proprietário também não confirma o OFX que ele mesmo importou.
    /// </summary>
    private async Task<Error?> SegregationAsync(IReadOnlyCollection<BankStatementLine> lines, CancellationToken ct)
    {
        var batches = lines.Where(l => l.Source == StatementSource.Ofx).Select(l => l.BatchId).Distinct().ToList();
        if (batches.Count == 0 || currentUser.UserId is not { } me)
        {
            return null;
        }

        return await db.BankStatementBatches.AsNoTracking().AnyAsync(b => batches.Contains(b.Id) && b.ImportedBy == me, ct)
            ? Error.Forbidden(SegregationCode, SegregationMessage)
            : null;
    }

    /// <summary>A sugestão de agora para a linha (a mesma do motor da tela, na janela da linha), para gravar "a sugestão aceita".</summary>
    private async Task<PairSuggestion?> SuggestionForAsync(BankStatementLine line, CancellationToken ct) =>
        (await SuggestionsAsync(line.BankAccountId, line.Date, line.Date, ct)).FirstOrDefault(s => s.LineId == line.Id);

    private async Task<IReadOnlyList<PairSuggestion>> SuggestionsAsync(Guid accountId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var window = ReconciliationSuggester.WindowDays;
        var lines = await db.BankStatementLines.AsNoTracking()
            .Where(l => l.BankAccountId == accountId && l.Status == StatementLineStatus.Pending && l.Date >= from.AddDays(-2 * window) && l.Date <= to.AddDays(2 * window))
            .ToListAsync(ct);
        var open = await db.FinancialTransactions.AsNoTracking()
            .Where(t => t.BankAccountId == accountId && !t.Reconciled && t.Date >= from.AddDays(-3 * window) && t.Date <= to.AddDays(3 * window)).ToListAsync(ct);
        var context = await ContextAsync(open, ct);
        return ReconciliationSuggester.Suggest(lines.Select(ToSuggestion), open.Select(context.ToSuggestion));
    }

    /// <summary>Grava a conciliação, os vínculos (o valor do lado "muitos") e marca as linhas e as transações.</summary>
    private BankReconciliation Record(
        Guid accountId, ReconciliationKind kind, ReconciliationConfidence? confidence, string? reason, string? closedPeriodReason, List<BankStatementLine> lines,
        List<FinancialTransaction> transactions)
    {
        var reconciliation = new BankReconciliation
        {
            BankAccountId = accountId, Kind = kind, Confidence = confidence, Reason = reason is null ? null : Cut(reason, 200), ClosedPeriodReason = closedPeriodReason,
            CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
        };
        db.BankReconciliations.Add(reconciliation);
        if (lines.Count == 1)
        {
            db.BankReconciliationLinks.AddRange(transactions.Select(t => new BankReconciliationLink { ReconciliationId = reconciliation.Id, StatementLineId = lines[0].Id, TransactionId = t.Id, Amount = t.Amount }));
        }
        else
        {
            db.BankReconciliationLinks.AddRange(lines.Select(l => new BankReconciliationLink { ReconciliationId = reconciliation.Id, StatementLineId = l.Id, TransactionId = transactions[0].Id, Amount = l.Amount }));
        }

        foreach (var line in lines)
        {
            line.Reconcile(transactions[0].Id);
        }

        foreach (var transaction in transactions)
        {
            transaction.MarkReconciled(lines[0].Id);
        }

        return reconciliation;
    }

    private static ReconcileResult Result(BankReconciliation r, List<BankStatementLine> lines, List<FinancialTransaction> transactions) =>
        new(r.Id, r.Kind, lines.Count, transactions.Count, lines.Sum(l => l.Amount));

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private async Task<bool> CanAsync(string action, CancellationToken ct) => await permissions.HasPermissionAsync("finance", action, ct);
}
