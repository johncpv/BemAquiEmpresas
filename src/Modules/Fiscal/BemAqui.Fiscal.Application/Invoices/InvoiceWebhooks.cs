using BemAqui.BuildingBlocks.Integration;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Fiscal.Contracts;

namespace BemAqui.Fiscal.Application.Invoices;

/// <summary>
/// Tradutor dos eventos da nota para os avisos <c>invoice.*</c> (INT-02 CA 7, catálogo v1): id, tipo, data, total e o id do destinatário; nunca
/// <c>Taxes</c>, <c>CostTaxAmount</c> nem dado do destinatário. Roda na transação do consumidor (outbox + inbox).
/// </summary>
public sealed class InvoiceWebhooks(IOutboundWebhookPublisher publisher, TimeProvider clock)
    : IIntegrationEventConsumer<InvoiceAuthorized>, IIntegrationEventConsumer<InvoiceCancelled>, IIntegrationEventConsumer<InvoiceRejected>
{
    public Task HandleAsync(InvoiceAuthorized integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return Publish("invoice.authorized", integrationEvent.InvoiceId, new Dictionary<string, object?>
        {
            ["id"] = integrationEvent.InvoiceId, ["type"] = Type(integrationEvent.Type), ["issued_on"] = integrationEvent.IssuedOn,
            ["total"] = integrationEvent.TotalAmount, ["party_id"] = integrationEvent.PartyId,
        }, cancellationToken);
    }

    public Task HandleAsync(InvoiceCancelled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return Publish("invoice.cancelled", integrationEvent.InvoiceId, new Dictionary<string, object?>
        {
            ["id"] = integrationEvent.InvoiceId, ["type"] = Type(integrationEvent.Type), ["cancelled_on"] = integrationEvent.CancelledOn, ["total"] = integrationEvent.TotalAmount,
        }, cancellationToken);
    }

    public Task HandleAsync(InvoiceRejected integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return Publish("invoice.rejected", integrationEvent.InvoiceId, new Dictionary<string, object?>
        {
            ["id"] = integrationEvent.InvoiceId, ["type"] = Type(integrationEvent.Type),
        }, cancellationToken);
    }

    private static string Type(InvoiceType type) => type == InvoiceType.NFe ? "nfe" : "nfse";

    private Task Publish(string type, Guid id, Dictionary<string, object?> data, CancellationToken cancellationToken) =>
        publisher.PublishAsync(new OutboundWebhookMessage(type, id, clock.GetUtcNow(), data), cancellationToken);
}
