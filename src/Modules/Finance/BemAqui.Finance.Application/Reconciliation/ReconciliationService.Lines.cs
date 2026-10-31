using BemAqui.BuildingBlocks.Authorization;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application.Reconciliation;

public sealed partial class ReconciliationService
{
    public const string EntryNotAllowedCode = "bank_entry_not_allowed";
    public const string OfxCapCode = "ofx_bank_entry_cap";
    public const string PaymentNeedsOrder = "Esta saída não é tarifa, IOF nem juros: vincule a uma OP paga ou abra uma OP para regularizar. Nenhum pagamento sai sem OP aprovada.";

    /// <summary>A linha é uma saída? (o endpoint pede o 2FA de novo para lançar sem OP a partir de saída, D6). Nulo: não achou.</summary>
    public async Task<bool?> IsDebitAsync(Guid accountId, Guid lineId, CancellationToken cancellationToken = default) =>
        await db.BankStatementLines.AsNoTracking().Where(l => l.Id == lineId && l.BankAccountId == accountId && l.Status != StatementLineStatus.Undone)
            .Select(l => (bool?)(l.Amount < 0)).FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// "Lançar sem OP" a partir da linha (CA 2, 6 e 8): o lançamento da FIN-04 (conta do plano e motivo; o Proprietário é avisado e entra em
    /// "Movimentos sem OP") com a data e o valor da linha, e a linha conciliada na mesma transação. Saída só como tarifa, IOF ou juros e só
    /// quando a linha é reconhecida como tal (regra ou tipo do banco); o resto é pagamento e exige OP aprovada. Linha de OFX acima do teto
    /// <c>finance.ofx_bank_entry_cap</c> exige <c>finance.approve</c>. A permissão <c>finance.pay</c> e o 2FA de novo na saída ficam no endpoint.
    /// </summary>
    public async Task<Result<ReconcileResult>> BankEntryAsync(Guid accountId, Guid lineId, LineBankEntryInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await PendingLineAsync(accountId, lineId, input.LineVersion, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var line = loaded.Value!;
        if (await SegregationAsync([line], cancellationToken) is { } segregation)
        {
            return segregation;
        }

        var rules = await db.BankStatementRules.AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.CreatedAt).ToListAsync(cancellationToken);
        var recognized = ReconciliationRules.EntryKindFor(line.Description, line.Kind, line.Amount, rules);
        if (!ReconciliationRules.EntryAllowed(input.Kind, line.Amount, recognized))
        {
            var message = line.Amount < 0
                ? (ReconciliationRules.OutflowKinds.Contains(input.Kind) ? PaymentNeedsOrder : "Numa saída, escolha tarifa bancária, IOF ou juros pagos. " + PaymentNeedsOrder)
                : "Numa entrada, escolha juros recebidos, rendimento ou outra entrada.";
            return Error.Validation(EntryNotAllowedCode, message, new Dictionary<string, string[]> { ["kind"] = [message] });
        }

        var cap = (await parameters.LimitsAsync(cancellationToken)).OfxBankEntryCap;
        if (line.Source == StatementSource.Ofx && Math.Abs(line.Amount) > cap && !await CanAsync(StandardActions.Approve, cancellationToken))
        {
            return Error.Forbidden(OfxCapCode, $"Lançamento acima de {ReceivableService.Money(cap)} a partir de linha de OFX: só quem aprova em Caixa e pagamentos pode confirmar.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var created = await movements.CreateEntryAsync(new BankEntryInput(accountId, input.Kind, line.Date, Math.Abs(line.Amount), input.AccountId, input.CostCenterId, input.Reason,
            input.BackdateReason), cancellationToken);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        var entry = await db.BankEntries.FirstAsync(e => e.Id == created.Value.Id, cancellationToken);
        var movement = await db.FinancialTransactions.FirstAsync(t => t.Id == entry.TransactionId, cancellationToken);
        var reason = recognized?.Rule is not null ? "Lançamento sem OP a partir da linha, pela regra do extrato" : "Lançamento sem OP a partir da linha";
        var closedReason = await period.IsClosedAsync(accountId, line.Date, cancellationToken) ? input.BackdateReason?.Trim() : null;
        var reconciliation = Record(accountId, ReconciliationKind.BankEntry, null, reason, closedReason, [line], [movement]);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result(reconciliation, [line], [movement]);
    }

    /// <summary>
    /// "Baixar o título" a partir da linha (CA 3 e 7): a baixa da FIN-01 (limites de desconto, data retroativa, boleto em aberto) com a data e
    /// a conta da linha; o total (principal + juros + multa − desconto) precisa ser o valor da linha; a linha conciliada na mesma transação.
    /// </summary>
    public async Task<Result<ReconcileResult>> SettleAsync(Guid accountId, Guid lineId, LineSettlementInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var loaded = await PendingLineAsync(accountId, lineId, input.LineVersion, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var line = loaded.Value!;
        if (line.Amount <= 0)
        {
            return ReceivableService.Invalid("receivableId", "Só uma entrada do banco baixa um título a receber.");
        }

        if (await SegregationAsync([line], cancellationToken) is { } segregation)
        {
            return segregation;
        }

        var total = ReceivableSettlement.TotalOf(input.Principal, input.Interest, input.Fine, input.Discount);
        if (total != line.Amount)
        {
            return ReceivableService.Invalid("principal", $"O total da baixa ({ReceivableService.Money(total)}) precisa ser o valor da linha ({ReceivableService.Money(line.Amount)}).");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var known = db.FinancialTransactions.Local.Select(t => t.Id).ToHashSet();
        var settled = await receivables.SettleAsync(input.ReceivableId, new SettlementInput(accountId, line.Date, input.Principal, input.Interest, input.Fine, input.Discount,
            input.BackdateReason, input.ReceivableVersion, input.CancelBillingSlip), cancellationToken);
        if (settled.IsFailure)
        {
            return settled.Error!;
        }

        var receipt = db.FinancialTransactions.Local.Single(t => !known.Contains(t.Id) && t.ReceivableId == input.ReceivableId && t.ReversesTransactionId == null);
        var closedReason = await period.IsClosedAsync(accountId, line.Date, cancellationToken) ? input.BackdateReason?.Trim() : null;
        var reconciliation = Record(accountId, ReconciliationKind.Settlement, null, $"Baixa do título {settled.Value.Number} a partir da linha", closedReason, [line], [receipt]);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result(reconciliation, [line], [receipt]);
    }

    private async Task<Result<BankStatementLine>> PendingLineAsync(Guid accountId, Guid lineId, uint version, CancellationToken ct)
    {
        var line = await db.BankStatementLines.FirstOrDefaultAsync(l => l.Id == lineId && l.BankAccountId == accountId && l.Status != StatementLineStatus.Undone, ct);
        if (line is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Linha do extrato não encontrada nesta conta.");
        }

        db.Entry(line).Property(l => l.Version).OriginalValue = version;
        if (line.Version != version)
        {
            return Error.Conflict("concurrency_conflict", "A linha do extrato mudou desde que você abriu. Recarregue.");
        }

        return line.Status == StatementLineStatus.Pending ? line : Error.Conflict("bank_statement_line_not_pending", "A linha já foi conciliada ou ignorada. Recarregue.");
    }
}
