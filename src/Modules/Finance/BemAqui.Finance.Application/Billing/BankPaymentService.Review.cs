using System.Text.Json.Serialization;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>Linha do filtro "Pagamentos a conferir" (CA 9, tela A receber): título, cliente, valor do boleto, valor pago, data e diferença.</summary>
public sealed record PaymentReviewRow(
    Guid SlipId, Guid ReceivableId, string ReceivableNumber, string PartyName, decimal SlipAmount, decimal PaidAmount, DateOnly? CreditDate, decimal Balance, decimal Difference, string? Reason,
    DateTimeOffset? PaidAt, uint Version);

public sealed record PaymentReviewList(IReadOnlyList<PaymentReviewRow> Items, int Count);

/// <summary>Uma opção do diálogo "Conferir pagamento", com o impacto (saldo antes e depois).</summary>
public sealed record PaymentReviewOption(string Decision, string Label, decimal Principal, decimal Interest, decimal Discount, decimal BalanceBefore, decimal BalanceAfter, bool RequiresApproval, bool Allowed);

/// <summary>Diálogo "Conferir pagamento": as opções e a mais provável já escolhida (propositivo).</summary>
public sealed record PaymentReviewDraft(PaymentReviewRow Payment, IReadOnlyList<PaymentReviewOption> Options, string SuggestedDecision, bool CanApprove, bool ReceivableOpen);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PaymentReviewInput(string Decision, uint Version);

public sealed record PaymentReviewResult(Guid SlipId, Guid ReceivableId, string Decision, string Message);

public sealed partial class BankPaymentService
{
    public const string ReviewedMessage = "Pagamento conferido";
    public const string NotPendingCode = "billing_slip_not_pending_review";

    public async Task<PaymentReviewList> PendingAsync(CancellationToken cancellationToken = default)
    {
        var slips = await db.BillingSlips.AsNoTracking().Where(s => s.Status == BillingSlipStatus.PaidPendingReview).OrderBy(s => s.PaidAt).Take(200).ToListAsync(cancellationToken);
        var rows = await RowsAsync(slips, cancellationToken);
        return new PaymentReviewList(rows, rows.Count);
    }

    public async Task<Result<PaymentReviewDraft>> DraftAsync(Guid slipId, CancellationToken cancellationToken = default)
    {
        var slip = await db.BillingSlips.AsNoTracking().FirstOrDefaultAsync(s => s.Id == slipId && s.Status == BillingSlipStatus.PaidPendingReview, cancellationToken);
        if (slip is null)
        {
            return NotPending();
        }

        var receivable = await db.Receivables.AsNoTracking().FirstAsync(r => r.Id == slip.ReceivableId, cancellationToken);
        var canApprove = await CanApproveAsync(cancellationToken);
        var options = await OptionsAsync(slip, receivable, canApprove, cancellationToken);
        return new PaymentReviewDraft((await RowsAsync([slip], cancellationToken))[0], options, Suggested(slip, options), canApprove, receivable.IsOpen);
    }

    /// <summary>
    /// "Conferir pagamento" (CA 9): <c>finance.edit</c> (no endpoint); desconto acima do limite da FIN-01 só com <c>finance.approve</c>. A
    /// baixa liga a entrada que já estava na conta (o valor não muda); o boleto passa a <c>Paid</c> com a decisão de quem conferiu.
    /// </summary>
    public async Task<Result<PaymentReviewResult>> ReviewAsync(Guid slipId, PaymentReviewInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var slip = await db.BillingSlips.FromSql($"SELECT s.*, s.xmin FROM finance.billing_slips s WHERE s.id = {slipId} FOR UPDATE").FirstOrDefaultAsync(cancellationToken);
        if (slip is null || slip.Status != BillingSlipStatus.PaidPendingReview)
        {
            return NotPending();
        }

        if (slip.Version != input.Version)
        {
            return Error.Conflict(BemAqui.BuildingBlocks.Web.ConcurrencyExceptionHandler.Code, "O pagamento mudou desde que você abriu. Recarregue.");
        }

        var receivable = await db.Receivables.FirstAsync(r => r.Id == slip.ReceivableId, cancellationToken);
        var options = await OptionsAsync(slip, receivable, await CanApproveAsync(cancellationToken), cancellationToken);
        if (options.FirstOrDefault(o => o.Decision == input.Decision && o.Allowed) is not { } option)
        {
            return ReceivableService.Invalid("decision", "Escolha uma das opções do diálogo.");
        }

        Guid? settlementId = null;
        if (option.Principal > 0 && receivable.IsOpen)
        {
            var settled = await receivables.SettleFromBankAsync(receivable, new BankSettlementInput(slip.BankAccountId, slip.CreditDate ?? Today, option.Principal, option.Interest, 0, option.Discount,
                slip.Id, slip.PaidEventId, CheckDiscountLimit: true, slip.PendingTransactionId), cancellationToken);
            if (settled.IsFailure)
            {
                return settled.Error!;
            }

            settlementId = settled.Value.Id;
        }
        else if (slip.PendingTransactionId is { } pendingId && await db.FinancialTransactions.FirstOrDefaultAsync(t => t.Id == pendingId, cancellationToken) is { } pending)
        {
            pending.Reviewed(null);
        }

        slip.CompleteReview(option.Decision, settlementId, currentUser.UserId, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PaymentReviewResult(slip.Id, receivable.Id, option.Decision, ReviewedMessage);
    }

    /// <summary>
    /// Opções pelo valor pago (P) e o saldo (S): P &lt; S, "Baixar com desconto" (saldo zera) ou "Deixar saldo em aberto"; P &gt; S, "Baixar com
    /// juros"; P = S, "Baixar o título". Título fechado (pago por outro meio ou cancelado): só "Registrar só a entrada".
    /// </summary>
    private async Task<List<PaymentReviewOption>> OptionsAsync(BillingSlip slip, Receivable receivable, bool canApprove, CancellationToken ct)
    {
        var paid = slip.PaidAmount ?? 0m;
        var balance = receivable.Balance;
        if (!receivable.IsOpen || paid <= 0)
        {
            return [new PaymentReviewOption(BankPaymentDecision.KeepOpen, "Registrar só a entrada", 0, 0, 0, balance, balance, false, true)];
        }

        if (paid == balance)
        {
            return [new PaymentReviewOption(BankPaymentDecision.Settle, "Baixar o título", balance, 0, 0, balance, 0, false, true)];
        }

        if (paid > balance)
        {
            return [new PaymentReviewOption(BankPaymentDecision.Interest, "Baixar com juros", balance, paid - balance, 0, balance, 0, false, true)];
        }

        var discount = balance - paid;
        var limits = await parameters.LimitsAsync(ct);
        var pct = balance > 0 ? discount / balance * 100 : 100;
        var aboveLimit = pct > limits.DiscountApprovalPct || discount > limits.DiscountApprovalAmount;
        return
        [
            new PaymentReviewOption(BankPaymentDecision.Discount, "Baixar com desconto", balance, 0, discount, balance, 0, aboveLimit, !aboveLimit || canApprove),
            new PaymentReviewOption(BankPaymentDecision.KeepOpen, "Deixar saldo em aberto", paid, 0, 0, balance, balance - paid, false, true),
        ];
    }

    /// <summary>A mais provável: desconto dentro do limite quando pagou a menor até a data do desconto; senão, a primeira permitida.</summary>
    private static string Suggested(BillingSlip slip, IReadOnlyList<PaymentReviewOption> options)
    {
        var discount = options.FirstOrDefault(o => o.Decision == BankPaymentDecision.Discount);
        if (discount is { Allowed: true, RequiresApproval: false } && slip.DiscountUntil is { } until && slip.CreditDate <= until)
        {
            return BankPaymentDecision.Discount;
        }

        return discount is not null ? BankPaymentDecision.KeepOpen : options.First(o => o.Allowed).Decision;
    }

    private async Task<List<PaymentReviewRow>> RowsAsync(IReadOnlyList<BillingSlip> slips, CancellationToken ct)
    {
        var ids = slips.Select(s => s.ReceivableId).Distinct().ToList();
        var titles = await db.Receivables.AsNoTracking().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var names = (await parties.FindManyAsync(titles.Values.Select(r => r.PartyId).Distinct().ToList(), ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        return slips.Select(s =>
        {
            var r = titles[s.ReceivableId];
            var paid = s.PaidAmount ?? 0m;
            return new PaymentReviewRow(s.Id, r.Id, r.Number, names.GetValueOrDefault(r.PartyId, string.Empty), s.Amount, paid, s.CreditDate, r.Balance, paid - r.Balance, s.ReviewReason, s.PaidAt, s.Version);
        }).ToList();
    }

    private async Task<bool> CanApproveAsync(CancellationToken ct) => await permissions.HasPermissionAsync("finance", "approve", ct);

    private static Error NotPending() => Error.NotFound(NotPendingCode, "Pagamento a conferir não encontrado.");
}
