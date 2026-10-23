using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Inventory.Domain;

/// <summary>
/// Situação da requisição de material (EST-03): <c>Requested</c> (pedido do operador no <c>/fab</c>, sem baixa), <c>PendingApproval</c>
/// (consumo acima da BOM acima do limite, sem baixa), <c>Completed</c> (baixada, devolvida ou registrada como perda), <c>Rejected</c>
/// (pedido recusado ou requisição reprovada) e <c>Reversed</c> (estornada pelo movimento inverso).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<MaterialRequisitionStatus>))]
public enum MaterialRequisitionStatus
{
    Requested,
    PendingApproval,
    Completed,
    Rejected,
    Reversed,
}

/// <summary>
/// Tipo do documento (EST-03): <c>Issue</c> é a requisição (baixa para a OS); <c>Return</c> a devolução ao estoque pelo custo da baixa
/// original (CA 12); <c>Loss</c> o material da OS cancelada mantido no projeto como perda (CA 13), sem movimento.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<MaterialRequisitionKind>))]
public enum MaterialRequisitionKind
{
    Issue,
    Return,
    Loss,
}

/// <summary>
/// Requisição de material da OS (<c>inventory.material_requisitions</c>, EST-03, D15 <c>REQ-AAAA-0001</c>). A baixa, a devolução e o
/// estorno chamam o <c>IStockMovements</c> na transação dela (ADR 0023 §1); o custo do projeto vai por evento (ADR 0025 §2). A OS, a
/// etapa e o projeto são de outros módulos: só os ids.
/// </summary>
[DocumentName("A requisição")]
public sealed class MaterialRequisition : Entity, IAuditable, IConcurrencyVersioned
{
    public string Number { get; init; } = string.Empty;

    public MaterialRequisitionKind Kind { get; init; }

    public Guid ProductionOrderId { get; init; }

    public Guid ProjectId { get; init; }

    /// <summary>Etapa do pedido do operador (CA 5); nula na requisição do almoxarife.</summary>
    public Guid? StepId { get; init; }

    public string? StepName { get; init; }

    public MaterialRequisitionStatus Status { get; set; }

    public Guid? RequestedBy { get; init; }

    public DateTimeOffset RequestedAt { get; init; }

    /// <summary>Quem baixou (ou confirmou o pedido) e quando.</summary>
    public Guid? CompletedBy { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Quem aprovou ou reprovou o consumo acima da BOM (CA 4).</summary>
    public Guid? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public string? DecisionComment { get; set; }

    public bool SelfApproved { get; set; }

    /// <summary>Motivo da recusa do pedido do operador (CA 7) ou da perda (CA 13).</summary>
    public string? RejectReason { get; set; }

    /// <summary>Consumo acima da BOM (excedente sobre a reserva + material extra) pelo custo médio do envio, para a regra do CA 4.</summary>
    public decimal OverBomValue { get; set; }

    public Guid? ReversedBy { get; set; }

    public DateTimeOffset? ReversedAt { get; set; }

    public string? ReversalReason { get; set; }

    public bool ReversalSelfApproved { get; set; }

    public uint Version { get; set; }
}

/// <summary>
/// Linha da requisição (<c>inventory.material_requisition_lines</c>): item, lote, local e quantidade na unidade de estoque (sempre
/// positiva; o tipo dá o sentido). <c>OverQuantity</c> é a parte acima da reserva (CA 3), com o motivo; <c>IsExtra</c> marca o
/// "Material extra" (fora da BOM). Na devolução e na perda, <c>ReturnsLineId</c> aponta a linha da baixa original. O movimento que
/// nasceu dela fica em <c>MovementId</c> (a chave de idempotência no motor é a própria linha).
/// </summary>
public sealed class MaterialRequisitionLine : Entity, IAuditable
{
    public Guid RequisitionId { get; init; }

    public int Sequence { get; init; }

    public Guid? ReservationId { get; set; }

    public Guid ItemId { get; init; }

    public Guid? LotId { get; set; }

    public Guid LocationId { get; set; }

    public decimal Quantity { get; set; }

    public decimal OverQuantity { get; set; }

    public string? OverReason { get; set; }

    public bool IsExtra { get; set; }

    public Guid? ReturnsLineId { get; init; }

    public Guid? MovementId { get; set; }
}
