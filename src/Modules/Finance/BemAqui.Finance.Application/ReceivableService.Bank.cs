using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Baixa vinda do banco (BNK-02 CA 7 e 9): os valores já divididos pelo <see cref="BillingSlipSettlement"/> (ou decididos na conferência),
/// a conta da integração e a data do crédito. Sem a entrada pendente, cria a transação <c>Receipt</c>; com ela, liga a baixa à entrada que
/// já estava na conta. O desconto dentro das condições do boleto foi aprovado na emissão; na conferência (<paramref name="CheckDiscountLimit"/>)
/// o limite da FIN-01 vale para quem confere.
/// </summary>
public sealed record BankSettlementInput(
    Guid BankAccountId, DateOnly Date, decimal Principal, decimal Interest, decimal Fine, decimal Discount, Guid BillingSlipId, Guid? WebhookEventId, bool CheckDiscountLimit,
    Guid? PendingTransactionId = null);

public sealed partial class ReceivableService
{
    /// <summary>
    /// Baixa pelo banco, pelo mesmo caminho da FIN-01 (validações, invariante <c>paid_amount = Σ baixas</c>, <c>ReceivableSettled</c>), sem
    /// gravar: quem chama grava junto com o boleto, na mesma transação. Data retroativa não pede motivo (é a do banco); data futura e conta
    /// inativa recusam (o pagamento vai para a conferência). O título vem travado por quem chama.
    /// </summary>
    public async Task<Result<ReceivableSettlement>> SettleFromBankAsync(Receivable receivable, BankSettlementInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receivable);
        ArgumentNullException.ThrowIfNull(input);
        if (!receivable.IsOpen)
        {
            return NotOpen(receivable);
        }

        if (new[] { input.Principal, input.Interest, input.Fine, input.Discount }.Any(v => v < 0) || input.Principal <= 0)
        {
            return Invalid("principal", "Valores da baixa inválidos.");
        }

        if (input.Principal > receivable.Balance)
        {
            return Invalid("principal", $"O principal passa do saldo de {Money(receivable.Balance)}.");
        }

        if (input.Discount > input.Principal)
        {
            return Invalid("discount", "O desconto não pode passar do principal da baixa.");
        }

        var total = ReceivableSettlement.TotalOf(input.Principal, input.Interest, input.Fine, input.Discount);
        if (total <= 0)
        {
            return Invalid("discount", "O total recebido precisa ser maior que zero.");
        }

        var account = await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == input.BankAccountId, cancellationToken);
        if (account is null || !account.IsActive)
        {
            return Invalid("bankAccountId", "A conta do Inter está inativa.");
        }

        if (input.Date > Today)
        {
            return Invalid("date", "O banco informou uma data de crédito futura.");
        }

        if (account.OpeningDate is { } opening && input.Date < opening)
        {
            return Invalid("date", $"A data do crédito é anterior ao saldo inicial da conta ({opening:dd/MM/yyyy}).");
        }

        if (input.CheckDiscountLimit && await DiscountErrorAsync(input.Discount, receivable.Balance, cancellationToken) is { } overLimit)
        {
            return overLimit;
        }

        FinancialTransaction? pending = null;
        if (input.PendingTransactionId is { } pendingId)
        {
            pending = await db.FinancialTransactions.FirstOrDefaultAsync(t => t.Id == pendingId && t.PendingReview, cancellationToken);
            if (pending is null || pending.Amount != total)
            {
                return Invalid("decision", $"A baixa precisa somar o valor recebido do banco ({Money(pending?.Amount ?? 0)}).");
            }
        }

        var now = clock.GetUtcNow();
        var settlement = new ReceivableSettlement
        {
            ReceivableId = receivable.Id, BankAccountId = account.Id, Date = input.Date, Principal = input.Principal, Interest = input.Interest, Fine = input.Fine,
            Discount = input.Discount, Total = total, BalanceBefore = receivable.Balance, Kind = SettlementKind.Settlement, Source = SettlementSource.Bank,
            BillingSlipId = input.BillingSlipId, WebhookEventId = input.WebhookEventId, CreatedAt = now, CreatedBy = currentUser.UserId,
        };
        if (pending is not null)
        {
            pending.Reviewed(settlement.Id);
            settlement.TransactionId = pending.Id;
        }
        else
        {
            var transaction = new FinancialTransaction
            {
                BankAccountId = account.Id, Date = input.Date, Amount = total, Type = TransactionType.Receipt, Description = "Recebimento " + receivable.Number + " (Inter)",
                ReceivableId = receivable.Id, SettlementId = settlement.Id, BillingSlipId = input.BillingSlipId, CreatedAt = now, CreatedBy = currentUser.UserId,
            };
            db.FinancialTransactions.Add(transaction);
            settlement.TransactionId = transaction.Id;
        }

        db.ReceivableSettlements.Add(settlement);
        await RecalculateAsync(receivable, settlement, cancellationToken);
        Publish(receivable, settlement, false);
        return settlement;
    }
}
