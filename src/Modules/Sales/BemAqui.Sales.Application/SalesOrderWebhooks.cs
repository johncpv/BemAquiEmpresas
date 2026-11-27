using System.Text.Json;
using BemAqui.BuildingBlocks.Integration;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Sales.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Sales.Application;

/// <summary>
/// Tradutor dos eventos do pedido para os avisos públicos <c>sales_order.*</c> (INT-02 CA 7, catálogo v1): só id, número, situação, total e o id do
/// cliente; nunca nome, documento ou contato. Roda na transação do consumidor (outbox + inbox): reentrega da mesma mensagem não duplica. Os tipos
/// <c>picked</c>, <c>invoiced</c>, <c>shipped</c> e <c>delivered</c> entram com os eventos da VND-02b.
/// </summary>
public sealed class SalesOrderWebhooks(ISalesDb db, IOutboundWebhookPublisher publisher)
    : IIntegrationEventConsumer<OrderCreated>, IIntegrationEventConsumer<OrderConfirmed>, IIntegrationEventConsumer<OrderPaid>, IIntegrationEventConsumer<OrderCancelled>
{
    public Task HandleAsync(OrderCreated integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return PublishAsync("sales_order.created", integrationEvent.OrderId, integrationEvent.Number, integrationEvent.Status, integrationEvent.Total, integrationEvent.At, cancellationToken);
    }

    public Task HandleAsync(OrderConfirmed integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return PublishAsync("sales_order.confirmed", integrationEvent.OrderId, integrationEvent.Number, integrationEvent.Status, integrationEvent.Total, integrationEvent.At, cancellationToken);
    }

    public Task HandleAsync(OrderPaid integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return PublishAsync("sales_order.paid", integrationEvent.OrderId, integrationEvent.Number, integrationEvent.Status, integrationEvent.Total, integrationEvent.At, cancellationToken);
    }

    public Task HandleAsync(OrderCancelled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return PublishAsync("sales_order.cancelled", integrationEvent.OrderId, integrationEvent.Number, integrationEvent.Status, integrationEvent.Total, integrationEvent.At, cancellationToken);
    }

    private async Task PublishAsync(string type, Guid id, string number, SalesOrderStatus status, decimal total, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var partyId = await db.SalesOrders.AsNoTracking().Where(o => o.Id == id).Select(o => (Guid?)o.CustomerId).FirstOrDefaultAsync(cancellationToken);
        await publisher.PublishAsync(new OutboundWebhookMessage(type, id, at, new Dictionary<string, object?>
        {
            ["id"] = id,
            ["number"] = number,
            ["status"] = JsonNamingPolicy.SnakeCaseLower.ConvertName(status.ToString()),
            ["total"] = total,
            ["party_id"] = partyId,
        }), cancellationToken);
    }
}
