using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Inventory.Domain;

/// <summary>
/// Modo do ajuste (EST-01b CA 2): do documento inteiro, nunca da linha. <c>Counted</c> ("Quantidade contada", o padrão) guarda o que
/// existe na prateleira e a aprovação recalcula a diferença sobre o saldo de agora; <c>Delta</c> ("Diferença") guarda a diferença com
/// sinal e a aprovação soma ao saldo de agora (o inventário da EST-05 usa este, CA 13).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<StockAdjustmentMode>))]
public enum StockAdjustmentMode
{
    Counted,
    Delta,
}

/// <summary>Origem do ajuste: lançado na tela (<c>Manual</c>) ou gerado pelo fechamento do inventário (<c>InventoryCount</c>, EST-05).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StockAdjustmentOrigin>))]
public enum StockAdjustmentOrigin
{
    Manual,
    InventoryCount,
}

/// <summary>Motivo do ajuste (CA 2): Perda/avaria, Erro de lançamento, Sobra encontrada, Contagem, Outro (este exige descrição).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StockAdjustmentReason>))]
public enum StockAdjustmentReason
{
    Loss,
    EntryError,
    Surplus,
    Count,
    Other,
}

[JsonConverter(typeof(JsonStringEnumConverter<StockAdjustmentStatus>))]
public enum StockAdjustmentStatus
{
    Pending,
    Approved,
    Rejected,
    Cancelled,
}

/// <summary>
/// Ajuste de estoque (<c>inventory.stock_adjustments</c>, EST-01b, D15 <c>AJ-AAAA-0001</c>): sobra ou falta sem documento de origem,
/// num local e num modo. Nasce <c>Pending</c> sem mexer no saldo; só a aprovação por outra pessoa com <c>inventory.approve</c> grava os
/// movimentos <c>Adjustment</c> pelo motor, na mesma transação (ADR 0023 §1 e §5). O estorno pelo próprio documento fica registrado
/// aqui (<c>reversed_*</c>); a situação continua <c>Approved</c>.
/// </summary>
[DocumentName("O ajuste")]
public sealed class StockAdjustment : Entity, IAuditable, IConcurrencyVersioned
{
    public string Number { get; init; } = string.Empty;

    public Guid LocationId { get; init; }

    public StockAdjustmentMode Mode { get; init; }

    public StockAdjustmentOrigin Origin { get; init; }

    /// <summary>Inventário de origem (EST-05), só o id e o número.</summary>
    public Guid? OriginId { get; init; }

    public string? OriginNumber { get; init; }

    public StockAdjustmentReason Reason { get; init; }

    public string? ReasonDetail { get; init; }

    public StockAdjustmentStatus Status { get; set; }

    public Guid? RequestedBy { get; init; }

    public DateTimeOffset RequestedAt { get; init; }

    public Guid? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public string? DecisionComment { get; set; }

    public bool SelfApproved { get; set; }

    /// <summary>Motivo do cancelamento pelo autor (CA 10).</summary>
    public string? CancelReason { get; set; }

    /// <summary>Σ sobras × custo médio (2 casas por linha): estimado no envio, o aplicado depois da aprovação.</summary>
    public decimal SurplusValue { get; set; }

    /// <summary>Σ |faltas| × custo médio (2 casas por linha): estimado no envio, o aplicado depois da aprovação.</summary>
    public decimal ShortageValue { get; set; }

    public DateTimeOffset? ReversedAt { get; set; }

    public Guid? ReversedBy { get; set; }

    public string? ReversalReason { get; set; }

    public bool ReversalSelfApproved { get; set; }

    public uint Version { get; set; }
}

/// <summary>
/// Linha do ajuste (<c>inventory.stock_adjustment_lines</c>): item, lote, o saldo do sistema no envio, a quantidade contada (modo
/// <c>Counted</c>) ou a diferença com sinal (modo <c>Delta</c>), na unidade de estoque, e o custo médio do envio para a estimativa. A
/// aprovação grava o saldo de então, a diferença aplicada, o custo do movimento e o movimento (chave de idempotência no motor:
/// <c>(StockAdjustment, ajuste, sequence, Adjustment)</c>).
/// </summary>
public sealed class StockAdjustmentLine : Entity, IAuditable
{
    public Guid AdjustmentId { get; init; }

    public int Sequence { get; init; }

    public Guid ItemId { get; init; }

    public Guid? LotId { get; init; }

    public decimal SystemQtyAtRequest { get; init; }

    /// <summary>Modo <c>Counted</c>: o que existe na prateleira (≥ 0).</summary>
    public decimal? CountedQty { get; init; }

    /// <summary>Diferença com sinal no envio (no modo <c>Counted</c>, contado − sistema do envio).</summary>
    public decimal DeltaQty { get; init; }

    public decimal UnitCostEstimate { get; init; }

    /// <summary>O que foi digitado, na unidade escolhida (R-EST-3).</summary>
    public decimal EnteredQuantity { get; init; }

    public Guid? EnteredUnitId { get; init; }

    public decimal? SystemQtyAtApproval { get; set; }

    /// <summary>Diferença que o motor aplicou (recalculada no modo <c>Counted</c>, CA 7); 0 quando o saldo já batia com o contado.</summary>
    public decimal? AppliedDeltaQty { get; set; }

    /// <summary>Custo médio do momento da aprovação (o do movimento).</summary>
    public decimal? UnitCost { get; set; }

    public Guid? MovementId { get; set; }
}
