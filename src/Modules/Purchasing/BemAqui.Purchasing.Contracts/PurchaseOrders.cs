using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Purchasing.Contracts;

/// <summary>
/// Situação do pedido de compra (04-fluxos §1): <c>Draft → PendingApproval → Approved → Sent → PartiallyReceived → Received</c>;
/// <c>Cancelled</c> antes de receber. Reprovar volta a <c>Draft</c>; editar aprovado ou enviado volta a <c>PendingApproval</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PurchaseOrderStatus>))]
public enum PurchaseOrderStatus
{
    Draft,
    PendingApproval,
    Approved,
    Sent,
    PartiallyReceived,
    Received,
    Cancelled,
}

/// <summary>
/// Destino do saldo a receber (CMP-03a CA 24): todos os pedidos, só os do "Estoque geral" (sem projeto) ou só os de um projeto.
/// </summary>
public sealed record PurchaseOrderDestination(bool AllDestinations, Guid? ProjectId)
{
    public static PurchaseOrderDestination All { get; } = new(true, null);

    public static PurchaseOrderDestination GeneralStock { get; } = new(false, null);

    public static PurchaseOrderDestination Project(Guid projectId) => new(false, projectId);
}

/// <summary>Pedido de compra visto pelos outros módulos (CMP-03b, CMP-04, PRD-04): número, situação, fornecedor, destino e total.</summary>
public sealed record PurchaseOrderInfo(Guid Id, string Number, PurchaseOrderStatus Status, Guid PartyId, Guid? ProjectId, decimal Total, DateOnly ExpectedDate);

/// <summary>Comprometido de compras num projeto por natureza (ADR 0025 §1, para a PRD-04): o que está pedido e aprovado, ainda não recebido.</summary>
public sealed record PurchaseCommitment(CostType CostType, decimal Amount);

/// <summary>
/// Pedidos de compra para os outros módulos (CMP-03a): só leitura, sem permissão própria (quem chama confere a sua).
/// </summary>
public interface IPurchaseOrders
{
    Task<PurchaseOrderInfo?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saldo a receber por item (CA 24), na unidade de estoque: quantidade pedida − recebida dos pedidos <c>Approved</c>, <c>Sent</c> e
    /// <c>PartiallyReceived</c>; rascunho, aguardando aprovação, cancelado e recebido ficam fora. Item sem saldo fica fora do dicionário.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, decimal>> OpenQuantityByItemAsync(IReadOnlyCollection<Guid> itemIds, PurchaseOrderDestination destination, CancellationToken cancellationToken = default);

    /// <summary>Comprometido de compras do projeto (pedidos aprovados ainda não recebidos, pelo saldo a receber × preço), por natureza.</summary>
    Task<IReadOnlyList<PurchaseCommitment>> CommittedByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Última compra do item (CMP-04 CA 15): o último pedido aprovado (ou além, sem cancelado) com o item; null sem compra.</summary>
    Task<LastPurchaseInfo?> LastPurchaseAsync(Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pedidos não cancelados que atendem as linhas de SC pedidas (DSH-03 CA 5, "Pedido {PC} previsto para {data}"), do previsto mais cedo para o
    /// mais tarde. Só leitura, sem valor.
    /// </summary>
    Task<IReadOnlyList<PurchaseOrderForRequestLine>> ForRequestLinesAsync(IReadOnlyCollection<Guid> requestLineIds, CancellationToken cancellationToken = default);
}

/// <summary>Linha do pedido para a conferência da nota de entrada (FIS-04a CA 10): quantidades na unidade de estoque e o preço unitário.</summary>
public sealed record PurchaseOrderLineInfo(Guid LineId, int Sequence, Guid ItemId, decimal Quantity, decimal QuantityReceived, decimal UnitPrice)
{
    /// <summary>Saldo a receber (nunca negativo).</summary>
    public decimal OpenQuantity => Math.Max(0m, Quantity - QuantityReceived);
}

/// <summary>Pedido com as linhas, o frete e o IPI (FIS-04a CA 9 e 10).</summary>
public sealed record PurchaseOrderWithLines(
    Guid Id, string Number, PurchaseOrderStatus Status, Guid PartyId, decimal Freight, decimal IpiAmount, decimal Total, DateOnly ExpectedDate, IReadOnlyList<PurchaseOrderLineInfo> Lines);

/// <summary>
/// Pedidos com as linhas para a nota de entrada (FIS-04a CA 9 e 10): só leitura, sem permissão própria (o Fiscal mostra os valores só a quem
/// tem <c>purchasing.view</c>).
/// </summary>
public interface IPurchaseOrderLines
{
    /// <summary>Pedidos <c>Approved</c>, <c>Sent</c> e <c>PartiallyReceived</c> do fornecedor, do mais recente para o mais antigo.</summary>
    Task<IReadOnlyList<PurchaseOrderWithLines>> OpenForPartyAsync(Guid partyId, CancellationToken cancellationToken = default);

    /// <summary>Um pedido com as linhas, em qualquer situação; null se não existe.</summary>
    Task<PurchaseOrderWithLines?> FindAsync(Guid orderId, CancellationToken cancellationToken = default);
}

/// <summary>Pedido que atende uma linha de SC: número, situação e data prevista de entrega (sem valor).</summary>
public sealed record PurchaseOrderForRequestLine(Guid RequestLineId, Guid PurchaseOrderId, string Number, PurchaseOrderStatus Status, DateOnly ExpectedDate);

/// <summary>Pedido enviado para aprovação (CA 8): só aviso; o sino e o e-mail aos aprovadores do nível 1 saem na mesma transação.</summary>
public sealed record PurchaseOrderSubmitted(Guid PurchaseOrderId, int Attempt, decimal Total, Guid? RequestedBy) : IIntegrationEvent;

/// <summary>Nível do pedido liberado para decisão (CA 9).</summary>
public sealed record PurchaseOrderLevelReleased(Guid PurchaseOrderId, int Attempt, int Level) : IIntegrationEvent;

/// <summary>
/// Pedido aprovado (CA 12): só aviso e projeção (notificação, custo comprometido). Os títulos previstos nascem na transação da aprovação
/// pelo <c>IPayables</c> (ADR 0024 §1); nenhum consumidor do Finance cria título por este evento.
/// </summary>
public sealed record PurchaseOrderApproved(Guid PurchaseOrderId, Guid PartyId, Guid? ProjectId, decimal Total, Guid PaymentTermId, DateOnly ExpectedDate) : IIntegrationEvent;

/// <summary>Pedido reprovado (CA 14): volta a rascunho, o solicitante é avisado.</summary>
public sealed record PurchaseOrderRejected(Guid PurchaseOrderId, int Attempt) : IIntegrationEvent;

/// <summary>Pedido em aprovação, aprovado ou enviado editado (CA 15): volta à aprovação.</summary>
public sealed record PurchaseOrderChanged(Guid PurchaseOrderId, int Attempt, bool WasSent) : IIntegrationEvent;

/// <summary>Pedido cancelado (CA 16): só aviso; os títulos previstos já foram cancelados na mesma transação.</summary>
public sealed record PurchaseOrderCancelled(Guid PurchaseOrderId) : IIntegrationEvent;
