using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public sealed partial class PaymentOrderService
{
    /// <summary>Estorno (CA 21): baixas estornadas, transações inversas, títulos fora da OP e OP Reversed. Nada é apagado.</summary>
    public async Task<Result<PaymentOrderDetail>> ReverseAsync(Guid id, ReasonInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 200)
        {
            return ReceivableService.Invalid("reason", "Informe o motivo, com até 200 caracteres.");
        }

        var order = await db.PaymentOrders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status != PaymentOrderStatus.Paid || order.Version != input.Version)
        {
            return Error.Conflict("payment_order_not_paid", "Só é possível estornar uma ordem paga.");
        }

        db.Entry(order).Property(o => o.Version).OriginalValue = input.Version;
        var settlements = await db.PayableSettlements.Where(s => s.PaymentOrderId == order.Id && s.ReversedAt == null).ToListAsync(cancellationToken);
        var payables = await PayablesOfAsync(order, cancellationToken);
        var reason = input.Reason.Trim();
        foreach (var s in settlements)
        {
            var reversal = new FinancialTransaction
            {
                BankAccountId = s.BankAccountId, Date = Today, Amount = s.Total, Type = TransactionType.Payment, Description = $"Estorno do pagamento ({order.Number})", PayableId = s.PayableId,
                SettlementId = s.Id, ReversesTransactionId = s.TransactionId, CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
            };
            db.FinancialTransactions.Add(reversal);
            s.ReversedAt = clock.GetUtcNow();
            s.ReversedBy = currentUser.UserId;
            s.ReversalReason = reason;
            s.ReversalTransactionId = reversal.Id;
            var payable = payables.Single(p => p.Id == s.PayableId);
            payable.PaidAmount -= s.Principal;
            if (!payable.InPaymentOrder)
            {
                // O saldo pode estar noutra OP pendente: aí o título continua nela.
                Release(payable);
            }
        }

        order.Status = PaymentOrderStatus.Reversed;
        order.ReversedAt = clock.GetUtcNow();
        order.ReversedBy = currentUser.UserId;
        order.ReversalReason = reason;
        await NotifyOwnersAsync($"Pagamento da {order.Number} estornado por {await WhoAsync(cancellationToken)}: {Cut(reason, 100)}", order, cancellationToken);
        return await SaveDecisionAsync(order, cancellationToken);
    }

    /// <summary>
    /// Reenvio (CA 14): a OP aprovada volta a Pending com os novos acréscimos e o novo retrato; a decisão "Reenviada: valor
    /// acima do aprovado" entra na linha do tempo e os aprovadores são avisados.
    /// </summary>
    public async Task<Result<PaymentOrderDetail>> ResubmitAsync(Guid id, ResubmitInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var order = await db.PaymentOrders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status != PaymentOrderStatus.Approved || order.Version != input.Version)
        {
            return Decided();
        }

        db.Entry(order).Property(o => o.Version).OriginalValue = input.Version;
        var items = await db.PaymentOrderItems.Where(i => i.PaymentOrderId == order.Id).ToListAsync(cancellationToken);
        var payables = await PayablesOfAsync(order, cancellationToken);
        var partyMap = (await parties.FindManyAsync(payables.Select(p => p.PartyId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id);
        foreach (var item in items)
        {
            if (input.Items.FirstOrDefault(l => l.ItemId == item.Id) is { } line)
            {
                if (line.Interest < 0 || line.Fine < 0 || line.Discount < 0)
                {
                    return ReceivableService.Invalid("items", "Juros, multa e desconto não podem ser negativos.");
                }

                item.ExpectedCharges = Math.Max(0, line.Interest + line.Fine - line.Discount);
                item.ApprovedTotal = item.Amount + item.ExpectedCharges;
            }

            var payable = payables.Single(p => p.Id == item.PayableId);
            item.PayeeSnapshot = Serialize(SnapshotOf(payable, partyMap.GetValueOrDefault(payable.PartyId), item));
            payable.Status = PayableStatus.AwaitingApproval;
        }

        order.Status = PaymentOrderStatus.Pending;
        order.SelfApproved = false;
        order.Total = items.Sum(i => i.ApprovedTotal);
        order.SubmittedAt = clock.GetUtcNow();
        db.PaymentOrderApprovals.Add(new PaymentOrderApproval
        {
            PaymentOrderId = order.Id, Kind = PaymentOrderDecisionKind.Resubmission, Decision = PaymentOrderDecision.Resubmitted, Comment = "Reenviada: valor acima do aprovado",
            DecidedBy = currentUser.UserId, DecidedAt = clock.GetUtcNow(),
        });
        events.Publish(new PaymentOrderSubmitted(order.Id, order.Total, currentUser.UserId));
        var levels = await ResolveLevelsAsync(order, payables, cancellationToken);
        if (levels.IsFailure)
        {
            return levels.Error!;
        }

        return await SaveDecisionAsync(order, cancellationToken);
    }
}
