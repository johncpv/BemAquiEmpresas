using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Inventory.Contracts;

/// <summary>Situação da reserva (<c>inventory.reservations.status</c>, EST-02): ativa, consumida pela baixa (EST-03) ou liberada.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReservationState>))]
public enum ReservationState
{
    Active,
    Consumed,
    Released,
}

/// <summary>
/// Reserva de material de uma OS num local (EST-02 CA 16), sem custo: a necessidade daquele local (<c>Requested</c>), o reservado
/// (<c>Reserved</c>, que mora em <c>stock_positions.quantity_reserved</c>), o já baixado (<c>Issued</c>, EST-03) e a falta
/// (<c>Requested − Reserved − Issued</c>). Quantidades na unidade de estoque.
/// </summary>
public sealed record StockReservationInfo(
    Guid Id, Guid ProductionOrderId, Guid ItemId, Guid LocationId, Guid? BomLineId, int BomRevision, decimal Requested, decimal Reserved, decimal Issued, decimal Shortage,
    ReservationState Status, bool IsBackfill);

/// <summary>
/// Falta aberta de material de uma OS (EST-02 CA 16): por OS e item, somada nos locais, com a linha da BOM e quando a BOM foi
/// liberada (a ordem de atendimento). A CMP-01 recompõe as solicitações de compra a partir desta lista, não do evento.
/// </summary>
public sealed record StockShortageInfo(Guid ProductionOrderId, Guid ProjectId, Guid ItemId, Guid? BomLineId, decimal ShortQty, DateTimeOffset? BomReleasedAt);

/// <summary>
/// Reservas e faltas para a Produção, a EST-03 e a CMP-01 (EST-02 CA 16, ADR 0023 §6), em lote e sem custo. O reservado é
/// reconciliado a partir da fonte (a BOM liberada vigente e a situação da OS), nunca pelo delta dos eventos.
/// </summary>
public interface IStockReservations
{
    /// <summary>Reservas da OS (ativas, consumidas e liberadas), por item e local.</summary>
    Task<IReadOnlyList<StockReservationInfo>> ForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default);

    /// <summary>Faltas abertas das OS (falta &gt; 0), por OS e item, da BOM liberada há mais tempo para a mais nova.</summary>
    Task<IReadOnlyList<StockShortageInfo>> OpenShortagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// OS cujas faltas foram completadas pelas entradas dadas (CMP-04 CA 7: "Ver OS que receberam material"), pelo número dos movimentos.
    /// Lido na transação do chamador, logo depois do <see cref="IStockMovements.PostAsync"/>.
    /// </summary>
    Task<IReadOnlyList<Guid>> OrdersCompletedByEntriesAsync(IReadOnlyCollection<string> movementNumbers, CancellationToken cancellationToken = default);

    /// <summary>
    /// Libera, de forma síncrona, todas as reservas ativas da OS (a conclusão da PRD-06a2, ADR 0023 §1): o reservado da posição desce
    /// e as faltas zeram, na transação do chamador. Exige transação aberta (<see cref="InvalidOperationException"/> sem ela), como o
    /// <see cref="IStockMovements.PostAsync"/>. Idempotente: a segunda chamada não muda nada.
    /// </summary>
    Task ReleaseForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default);
}

/// <summary>
/// A falta de um item numa OS mudou (EST-02 CA 4, 9 e 11): <c>ShortQty</c> é a falta atual (0 quando zerou). <b>Só aviso</b> para a
/// CMP-01, que recompõe a solicitação a partir de <see cref="IStockReservations.OpenShortagesAsync"/>, não deste valor. Sem custo.
/// </summary>
public sealed record StockShortageChanged(Guid ItemId, Guid ProductionOrderId, Guid ProjectId, Guid? BomLineId, decimal ShortQty) : IIntegrationEvent;
