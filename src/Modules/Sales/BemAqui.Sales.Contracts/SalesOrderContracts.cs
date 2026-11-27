using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Sales.Contracts;

/// <summary>
/// Situação do pedido de venda (VND-02a, 04-fluxos §1.12): rascunho, aberto (estoque reservado), aguardando pagamento (cobrança emitida), pago (virou
/// venda) e cancelado. Separação, nota, envio e entrega chegam com a VND-02b.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesOrderStatus>))]
public enum SalesOrderStatus
{
    Draft,
    Open,
    AwaitingPayment,
    Paid,
    Cancelled,
}

/// <summary>
/// De onde o pedido veio (VND-02a CA 2, ADR 0053): sempre do principal autenticado, nunca do corpo. Pessoa = <see cref="Backoffice"/>; aplicativo
/// conectado = a origem do aplicativo (Conecta, marketplace, API); <see cref="Storefront"/> é a loja virtual (Fase 14).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesOrderOrigin>))]
public enum SalesOrderOrigin
{
    Backoffice,
    Conecta,
    Marketplace,
    Api,
    Storefront,
}

/// <summary>Pedido para os outros módulos e para a tradução dos avisos (INT-02): só ids, número, origem, situação, total e datas. Sem dado pessoal.</summary>
public sealed record SalesOrderSummary(
    Guid Id, string Number, SalesOrderStatus Status, SalesOrderOrigin Origin, Guid? SourceAppId, string? ExternalId, Guid CustomerId, decimal Total, Guid? SaleId, Guid? ReceivableId,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>Leitura do pedido pelos consumidores dos eventos (INT-02, VND-02b), sem tocar em <c>sales.orders</c>.</summary>
public interface ISalesOrderLookup
{
    Task<SalesOrderSummary?> FindAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>O pedido de um aplicativo pelo id externo (CA 2: único por origem, aplicativo e id externo).</summary>
    Task<SalesOrderSummary?> FindByExternalAsync(SalesOrderOrigin origin, Guid? sourceAppId, string externalId, CancellationToken cancellationToken = default);
}

/// <summary>Pedido gravado (outbox, CA 27). Sem dado pessoal: o consumidor relê pelo <see cref="ISalesOrderLookup"/>.</summary>
public sealed record OrderCreated(Guid OrderId, string Number, SalesOrderOrigin Origin, Guid? SourceAppId, string? ExternalId, SalesOrderStatus Status, decimal Total, DateTimeOffset At)
    : IIntegrationEvent;

/// <summary>Pedido confirmado: estoque reservado (CA 9).</summary>
public sealed record OrderConfirmed(Guid OrderId, string Number, SalesOrderOrigin Origin, Guid? SourceAppId, string? ExternalId, SalesOrderStatus Status, decimal Total, DateTimeOffset At)
    : IIntegrationEvent;

/// <summary>Pedido pago (CA 18 e 20): virou a venda <c>SaleId</c>.</summary>
public sealed record OrderPaid(
    Guid OrderId, string Number, SalesOrderOrigin Origin, Guid? SourceAppId, string? ExternalId, SalesOrderStatus Status, decimal Total, DateTimeOffset At, Guid SaleId)
    : IIntegrationEvent;

/// <summary>Pedido cancelado (CA 12, 25 e 26): reserva liberada, cobrança cancelada e, se pago, a venda estornada.</summary>
public sealed record OrderCancelled(Guid OrderId, string Number, SalesOrderOrigin Origin, Guid? SourceAppId, string? ExternalId, SalesOrderStatus Status, decimal Total, DateTimeOffset At)
    : IIntegrationEvent;
