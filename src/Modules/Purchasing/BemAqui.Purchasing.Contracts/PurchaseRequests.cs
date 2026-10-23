using System.Text.Json.Serialization;

namespace BemAqui.Purchasing.Contracts;

/// <summary>De onde veio a solicitação de compra (CMP-01, R-CMP-1): à mão, pela falta da BOM liberada ou pelo estoque mínimo.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PurchaseRequestOrigin>))]
public enum PurchaseRequestOrigin
{
    Manual,
    BomShortage,
    MinStock,
}

/// <summary>
/// Situação da solicitação e da linha (CMP-01): <c>Open</c> espera o comprador; <c>Quoting</c> está em cotação (CMP-02); <c>Ordered</c>
/// virou pedido (CMP-03a); <c>Cancelled</c>. A da solicitação é derivada das linhas.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PurchaseRequestStatus>))]
public enum PurchaseRequestStatus
{
    Open,
    Quoting,
    Ordered,
    Cancelled,
}

/// <summary>Linha de solicitação de compra vista pelos outros módulos (CMP-01 CA 15): o número e a situação para o link "SC gerada".</summary>
public sealed record PurchaseRequestLineInfo(
    Guid RequestId, string RequestNumber, Guid LineId, Guid ItemId, decimal Quantity, DateOnly NeededBy, PurchaseRequestStatus Status, PurchaseRequestOrigin Origin,
    Guid? ProductionOrderId, Guid? ProjectId);

/// <summary>
/// Solicitações de compra para a Produção (aba Material da OS) e o Estoque (lista Faltas), CMP-01 CA 15. Só leitura; as linhas
/// canceladas ficam fora.
/// </summary>
public interface IPurchaseRequests
{
    /// <summary>Linhas de SC da OS (de qualquer origem que tenha a OS), não canceladas.</summary>
    Task<IReadOnlyList<PurchaseRequestLineInfo>> ForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default);

    /// <summary>O mesmo de <see cref="ForOrderAsync"/> para várias OS numa consulta (lista Faltas).</summary>
    Task<IReadOnlyList<PurchaseRequestLineInfo>> ForOrdersAsync(IReadOnlyCollection<Guid> productionOrderIds, CancellationToken cancellationToken = default);
}
