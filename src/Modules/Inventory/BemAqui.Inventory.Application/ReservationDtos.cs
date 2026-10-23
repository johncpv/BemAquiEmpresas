using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Aba Material da OS (EST-02 CA 14): por item da BOM, a necessidade, o reservado, o baixado (EST-03), a falta e os locais; a soma
/// das faltas vai para o cabeçalho da OS. <c>Reconciling</c>: a BOM foi liberada e as reservas ainda não chegaram à revisão dela
/// ("Reservando material…", padrão 7).
/// </summary>
public sealed record OrderMaterialView(
    Guid ProductionOrderId, bool BomReleased, int? BomRevision, DateTimeOffset? BomReleasedAt, bool Reconciling, bool OrderClosed, decimal TotalShortage,
    decimal TotalOverIssued, IReadOnlyList<OrderMaterialLine> Lines);

/// <summary>
/// Linha da aba Material: necessidade (<c>Required</c>), reservado, baixado e falta somados nos locais; "Baixado acima da BOM"
/// (<c>OverIssued</c>, CA 8); a marca "Calculado na implantação" (<c>IsBackfill</c>); o link da solicitação de compra (CMP-01).
/// </summary>
public sealed record OrderMaterialLine(
    Guid ItemId, string ItemCode, string ItemName, string UnitCode, Guid? BomLineId, decimal Required, decimal Reserved, decimal Issued, decimal Shortage, decimal OverIssued,
    bool IsBackfill, IReadOnlyList<OrderMaterialLocation> Locations, string? PurchaseRequestLink);

public sealed record OrderMaterialLocation(
    Guid ReservationId, Guid LocationId, string LocationCode, string LocationName, decimal Requested, decimal Reserved, decimal Issued, decimal Shortage, ReservationState Status);

/// <summary>Uma reserva da OS (CA 17, IDOR pela OS): reserva de outra OS responde como inexistente.</summary>
public sealed record ReservationDetail(
    Guid Id, Guid ProductionOrderId, Guid ItemId, string ItemCode, string ItemName, string UnitCode, Guid LocationId, string LocationCode, string LocationName, decimal Requested,
    decimal Reserved, decimal Issued, decimal Shortage, ReservationState Status, int BomRevision, string Origin, bool IsBackfill, DateTimeOffset UpdatedAt);

/// <summary>Histórico da reserva (CA 18): quanto mudou, para quanto, a falta depois e por qual origem.</summary>
public sealed record ReservationChangeRow(
    Guid Id, Guid ProductionOrderId, string? OrderNumber, Guid ItemId, string ItemCode, string ItemName, string UnitCode, Guid LocationId, string LocationCode,
    ReservationChangeKind Kind, decimal QuantityDelta, decimal QuantityAfter, decimal RequestedAfter, decimal ShortageAfter, string Origin, bool IsBackfill, DateTimeOffset At);

public sealed record ShortageListQuery(string? Term, Guid? ProjectId, Guid? ItemId);

/// <summary>
/// Lista Faltas (CA 15): por item, a falta somada das OS abertas, o disponível agora, as OS e os projetos afetados, a data prometida
/// mais próxima e o link da solicitação de compra (CMP-01; nulo até lá).
/// </summary>
public sealed record ShortageRow(
    Guid ItemId, string ItemCode, string ItemName, string UnitCode, decimal ShortQty, decimal Available, DateOnly? NearestPromisedDate, IReadOnlyList<ShortageOrder> Orders,
    string? PurchaseRequestLink);

public sealed record ShortageOrder(Guid ProductionOrderId, string? OrderNumber, Guid ProjectId, string? ProjectNumber, decimal ShortQty, DateOnly? PromisedDate, bool IsBackfill);
