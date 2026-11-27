using BemAqui.BuildingBlocks.Integration;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Finance.Contracts;

namespace BemAqui.Finance.Application;

/// <summary>
/// Tradutor dos eventos do boleto para os avisos <c>billing_slip.*</c> (INT-02 CA 7, catálogo v1): ids, valor, datas e o motivo em código; nunca
/// nome, documento ou contato do pagador. Roda na transação do consumidor (outbox + inbox).
/// </summary>
public sealed class BillingSlipWebhooks(IOutboundWebhookPublisher publisher, TimeProvider clock)
    : IIntegrationEventConsumer<BillingSlipIssued>, IIntegrationEventConsumer<BillingSlipCancelled>, IIntegrationEventConsumer<BillingSlipPaid>
{
    public Task HandleAsync(BillingSlipIssued integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return Publish("billing_slip.issued", integrationEvent.BillingSlipId, new Dictionary<string, object?>
        {
            ["id"] = integrationEvent.BillingSlipId, ["receivable_id"] = integrationEvent.ReceivableId, ["amount"] = integrationEvent.Amount,
            ["due_date"] = integrationEvent.DueDate, ["party_id"] = integrationEvent.PartyId,
        }, cancellationToken);
    }

    public Task HandleAsync(BillingSlipCancelled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return Publish("billing_slip.cancelled", integrationEvent.BillingSlipId, new Dictionary<string, object?>
        {
            ["id"] = integrationEvent.BillingSlipId, ["receivable_id"] = integrationEvent.ReceivableId, ["reason"] = integrationEvent.Reason, ["expired"] = integrationEvent.Expired,
        }, cancellationToken);
    }

    public Task HandleAsync(BillingSlipPaid integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return Publish("billing_slip.paid", integrationEvent.BillingSlipId, new Dictionary<string, object?>
        {
            ["id"] = integrationEvent.BillingSlipId, ["receivable_id"] = integrationEvent.ReceivableId, ["paid_amount"] = integrationEvent.PaidAmount,
            ["credit_date"] = integrationEvent.CreditDate,
        }, cancellationToken);
    }

    private Task Publish(string type, Guid id, Dictionary<string, object?> data, CancellationToken cancellationToken) =>
        publisher.PublishAsync(new OutboundWebhookMessage(type, id, clock.GetUtcNow(), data), cancellationToken);
}
