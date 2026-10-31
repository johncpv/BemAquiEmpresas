using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Pagamento da OP (FIN-03a CA 13 a 18): com o estorno, o único caso de uso que cria transação
/// <see cref="TransactionType.Payment"/> (teste de arquitetura). Só OP aprovada paga; nada passa do aprovado mais a tolerância.
/// </summary>
public sealed partial class PaymentOrderService
{
    public async Task<Result<PaymentResult>> PayAsync(Guid id, PayInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var order = await db.PaymentOrders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status != PaymentOrderStatus.Approved)
        {
            return Error.Conflict("payment_order_not_approved", "Só é possível pagar uma ordem aprovada.");
        }

        if (order.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "A ordem mudou enquanto você pagava. Recarregue.");
        }

        var limits = await parameters.LimitsAsync(cancellationToken);
        if (await PayInputErrorAsync(input, limits, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var backdated = input.Date < Today.AddDays(-limits.BackdateDays);
        var items = await db.PaymentOrderItems.Where(i => i.PaymentOrderId == order.Id).ToListAsync(cancellationToken);
        var lines = input.Items.ToDictionary(l => l.ItemId);
        if (lines.Keys.Any(k => items.TrueForAll(i => i.Id != k)))
        {
            return ReceivableService.Invalid("items", "Um dos itens não é desta ordem.");
        }

        var planned = new List<(PaymentOrderItem Item, PayItemInput Line, decimal Total)>();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var line = lines.GetValueOrDefault(item.Id) ?? new PayItemInput(item.Id, item.ExpectedCharges, 0, 0);
            var total = PaymentOrderRules.TotalOf(item.Amount, line.Interest, line.Fine, line.Discount);
            if (line.Interest < 0 || line.Fine < 0 || line.Discount < 0 || total <= 0)
            {
                return ReceivableService.Invalid($"items[{i}].discount", "Juros, multa e desconto não podem ser negativos nem zerar o pagamento.");
            }

            if (PaymentOrderRules.Excess(total, item.ApprovedTotal, limits.PaymentTolerance) is var excess and > 0)
            {
                return Error.Conflict("exceeds_approved", $"O pagamento de {Snapshot(item).PayableNumber} passa do aprovado em {ReceivableService.Money(excess)}. Envie para nova aprovação.");
            }

            planned.Add((item, line, total));
        }

        // Salário (RH-02c CA 10 e 11): a conta ainda é a do retrato, e quem fechou a folha não paga.
        if (await EmployeeAccountsChangedAsync(items, cancellationToken) is { } changed)
        {
            return changed;
        }

        db.Entry(order).Property(o => o.Version).OriginalValue = input.Version;
        var payables = await PayablesOfAsync(order, cancellationToken);
        var (closer, selfPaid) = await PayrollCloserRuleAsync(payables, cancellationToken);
        if (closer is not null)
        {
            return closer;
        }

        order.SelfPaid = selfPaid;
        var suggestions = new List<RetentionSuggestion>();
        foreach (var (item, line, total) in planned)
        {
            var payable = payables.Single(p => p.Id == item.PayableId);
            var settlement = new PayableSettlement
            {
                PayableId = payable.Id, PaymentOrderId = order.Id, BankAccountId = input.BankAccountId, Date = input.Date, Principal = item.Amount, Interest = line.Interest,
                Fine = line.Fine, Discount = line.Discount, Total = total, BackdateReason = backdated ? input.BackdateReason!.Trim() : null, CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
            };
            var transaction = new FinancialTransaction
            {
                BankAccountId = input.BankAccountId, Date = input.Date, Amount = -total, Type = TransactionType.Payment, Description = $"Pagamento {payable.Number} ({order.Number})",
                PayableId = payable.Id, SettlementId = settlement.Id, CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
            };
            settlement.TransactionId = transaction.Id;
            db.PayableSettlements.Add(settlement);
            db.FinancialTransactions.Add(transaction);
            payable.PaidAmount += item.Amount;
            payable.Status = payable.Balance <= 0 ? PayableStatus.Paid : PayableStatus.PartiallyPaid;
            if (payable.Status == PayableStatus.Paid && payable.Retentions > 0)
            {
                var next = Today.AddMonths(1);
                suggestions.Add(new RetentionSuggestion(payable.Id, payable.Number, payable.Retentions, new DateOnly(next.Year, next.Month, 20)));
            }
        }

        order.Status = PaymentOrderStatus.Paid;
        order.PaidOn = input.Date;
        order.PaidBy = currentUser.UserId;
        order.BankAccountId = input.BankAccountId;
        events.Publish(new PaymentOrderPaid(order.Id, input.Date));
        if (selfPaid)
        {
            await NotifyOwnersAsync($"Autopagamento: {order.Number} com salários da folha que você fechou, pago por você (Proprietário único).", order, cancellationToken);
        }

        if (backdated)
        {
            await NotifyOwnersAsync($"Pagamento da {order.Number} com data de {input.Date:dd/MM/yyyy}, lançado por {await WhoAsync(cancellationToken)}.", order, cancellationToken);
        }

        var saved = await SaveDecisionAsync(order, cancellationToken);
        return saved.IsFailure ? saved.Error! : new PaymentResult(saved.Value, planned.Sum(p => p.Total), suggestions);
    }

    private async Task<Error?> PayInputErrorAsync(PayInput input, FinanceLimits limits, CancellationToken ct)
    {
        if (!await db.BankAccounts.AnyAsync(a => a.Id == input.BankAccountId && a.IsActive, ct))
        {
            return ReceivableService.Invalid("bankAccountId", "Escolha uma conta ativa.");
        }

        if (input.Date > Today)
        {
            return ReceivableService.Invalid("date", "A data do pagamento não pode ser futura.");
        }

        if (input.Date < Today.AddDays(-limits.BackdateDays) && string.IsNullOrWhiteSpace(input.BackdateReason))
        {
            return ReceivableService.Invalid("backdateReason", $"Pagamento com mais de {limits.BackdateDays} dias para trás: informe o motivo.");
        }

        // BNK-03b CA 11: pagamento com data no período de conciliação fechado da conta.
        return await period.CheckAsync(input.BankAccountId, input.Date, input.BackdateReason, "backdateReason", "pagar", ct);
    }

    private async Task<string> WhoAsync(CancellationToken ct) =>
        currentUser.UserId is { } me ? await users.DisplayNameAsync(me, ct) ?? "um usuário" : "o sistema";

    private async Task NotifyOwnersAsync(string title, PaymentOrder order, CancellationToken ct)
    {
        foreach (var owner in await users.OwnerIdsAsync(ct))
        {
            notifications.Notify(owner, NotificationType, title, Link(order));
        }
    }
}
