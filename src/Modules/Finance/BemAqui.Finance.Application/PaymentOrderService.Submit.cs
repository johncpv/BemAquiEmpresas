using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public sealed partial class PaymentOrderService
{
    private const int MaxItems = 100;

    /// <summary>
    /// Cria e envia a OP (CA 2 a 5): cada título disponível, com forma de pagamento e valor até o saldo líquido, vai para
    /// <c>AwaitingApproval</c> pela própria versão (dois envios do mesmo título: o segundo recebe 409), com o retrato do envio.
    /// </summary>
    public async Task<Result<PaymentOrderDetail>> SubmitAsync(PaymentOrderInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Items is not { Count: > 0 and <= MaxItems })
        {
            return ReceivableService.Invalid("items", $"Escolha de 1 a {MaxItems} títulos.");
        }

        if (input.Items.Select(i => i.PayableId).Distinct().Count() != input.Items.Count)
        {
            return ReceivableService.Invalid("items", "Cada título entra uma vez na ordem.");
        }

        var ids = input.Items.Select(i => i.PayableId).ToList();
        var payables = await db.Payables.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var partyMap = (await parties.FindManyAsync(payables.Values.Select(p => p.PartyId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id);
        var order = new PaymentOrder { RequestedBy = currentUser.UserId, CreatedAt = clock.GetUtcNow(), SubmittedAt = clock.GetUtcNow() };
        var items = new List<PaymentOrderItem>();
        for (var i = 0; i < input.Items.Count; i++)
        {
            var line = input.Items[i];
            payables.TryGetValue(line.PayableId, out var payable);
            if (ItemError(i, payable, line.Amount, line.ExpectedCharges) is { } error)
            {
                return error;
            }

            var item = new PaymentOrderItem { PaymentOrderId = order.Id, PayableId = payable!.Id, Amount = line.Amount, ExpectedCharges = line.ExpectedCharges, ApprovedTotal = line.Amount + line.ExpectedCharges };
            item.PayeeSnapshot = Serialize(SnapshotOf(payable, partyMap.GetValueOrDefault(payable.PartyId), item));
            items.Add(item);
            payable.Status = PayableStatus.AwaitingApproval;
        }

        order.Total = items.Sum(i => i.ApprovedTotal);
        order.ExpectedDate = input.ExpectedDate ?? payables.Values.Where(p => p.DueDate != null).Select(p => p.DueDate!.Value).DefaultIfEmpty(Today).Min();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        order.Number = await numbers.NextAsync(DocType, cancellationToken);
        db.PaymentOrders.Add(order);
        db.PaymentOrderItems.AddRange(items);
        events.Publish(new PaymentOrderSubmitted(order.Id, order.Total, order.RequestedBy));
        var levels = await ResolveLevelsAsync(order, payables.Values, cancellationToken);
        if (levels.IsFailure)
        {
            return levels.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(order, cancellationToken);
    }

    private static Error? ItemError(int i, Payable? payable, decimal amount, decimal charges) =>
        payable switch
        {
            null => ReceivableService.Invalid($"items[{i}].payableId", "O título não está disponível para pagamento."),
            { IsOpen: false } => ReceivableService.Invalid($"items[{i}].payableId", $"O título {payable.Number} não está disponível para pagamento."),
            { PaymentMethod: null } or { PaymentDetails: null } => ReceivableService.Invalid($"items[{i}].payableId", $"Informe a forma de pagamento do título {payable.Number}."),
            _ when amount <= 0 || amount > payable.Balance => ReceivableService.Invalid($"items[{i}].amount", $"O valor a pagar de {payable.Number} vai de R$ 0,01 até o saldo líquido ({ReceivableService.Money(payable.Balance)})."),
            _ when charges < 0 => ReceivableService.Invalid($"items[{i}].expectedCharges", "Os acréscimos previstos não podem ser negativos."),
            _ => null,
        };

    private static PayeeSnapshot SnapshotOf(Payable payable, PartySummary? party, PaymentOrderItem item) => new(
        payable.PartyId, party is null ? string.Empty : party.TradeName ?? party.LegalName, party?.Document, party?.CreatedAt, payable.Number, payable.Description, payable.NetAmount,
        payable.Retentions, item.Amount, item.ExpectedCharges, item.ApprovedTotal, payable.DueDate, PaymentDetails.Deserialize(payable.PaymentDetails)!, payable.PaymentMethodChangedAt);
}
