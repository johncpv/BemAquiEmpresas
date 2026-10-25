using System.Text.Json;
using System.Text.Json.Serialization;
using BemAqui.Inventory.Domain;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Linha do ajuste (CA 2): item, lote (quando o item controla) e a quantidade, que o modo do ajuste interpreta: a quantidade contada
/// (≥ 0) no modo Counted ou a diferença com sinal (≠ 0) no modo Delta, na unidade de estoque ou noutra com conversão. O modo é do
/// ajuste inteiro: qualquer outro campo na linha (ex.: <c>mode</c>) é recusado com 422.
/// </summary>
public sealed record StockAdjustmentLineInput(Guid ItemId, Guid? LotId, decimal Quantity, Guid? UnitId)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>"Enviar para aprovação" (CA 2): local, modo do ajuste inteiro, motivo ("Outro" exige a descrição) e as linhas.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StockAdjustmentInput(Guid LocationId, StockAdjustmentMode Mode, StockAdjustmentReason Reason, string? ReasonDetail, IReadOnlyList<StockAdjustmentLineInput>? Lines);

/// <summary>Linha do ajuste do inventário (CA 13): a diferença com sinal, na unidade de estoque.</summary>
public sealed record CountAdjustmentLine(Guid ItemId, Guid? LotId, decimal DeltaQuantity);

/// <summary>Ajuste gerado pelo fechamento do inventário (EST-05 CA 10): sempre no modo Delta, motivo "Contagem", com a referência.</summary>
public sealed record CountAdjustmentInput(Guid CountId, string CountNumber, Guid LocationId, IReadOnlyList<CountAdjustmentLine> Lines);

/// <summary>
/// Decisão de quem aprova (CA 6): só o comentário e a versão; qualquer outro campo (linha, quantidade, lote, modo) é recusado com 422.
/// </summary>
public sealed record StockAdjustmentDecisionInput(string? Comment, uint Version)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

/// <summary>"Cancelar ajuste" (CA 10) e "Estornar ajuste" (CA 12): motivo obrigatório e a versão.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StockAdjustmentReasonInput(string? Reason, uint Version);

public sealed record StockAdjustmentListQuery(StockAdjustmentStatus? Status, bool AwaitingMe, StockAdjustmentMode? Mode, Guid? LocationId);

/// <summary>Linha da lista "Ajustes" (CA 15).</summary>
public sealed record StockAdjustmentSummary(
    Guid Id, string Number, Guid LocationId, string LocationName, StockAdjustmentMode Mode, StockAdjustmentOrigin Origin, StockAdjustmentStatus Status, bool Reversed,
    string? RequestedByName, DateTimeOffset RequestedAt, int ItemCount, decimal SurplusValue, decimal ShortageValue, decimal DifferenceValue, bool SelfApproved, bool AwaitingMe);

/// <summary>
/// Linha do ajuste com o que foi enviado e, enquanto pendente, o saldo de agora (CA 7): físico, reservado e disponível da posição, o
/// saldo do sistema (lote ou item), a diferença que a aprovação aplicaria, o saldo depois e a recusa, se houver. Depois da aprovação,
/// a diferença aplicada, o custo e o movimento.
/// </summary>
public sealed record StockAdjustmentLineView(
    Guid Id, int Sequence, Guid ItemId, string ItemCode, string ItemName, string UnitCode, bool TracksLot, Guid? LotId, string? LotNumber,
    decimal SystemQtyAtRequest, decimal? CountedQty, decimal DeltaQty, decimal EnteredQuantity, string? EnteredUnitCode, decimal UnitCostEstimate, decimal EstimatedValue,
    decimal OnHand, decimal Reserved, decimal Available, decimal SystemQtyNow, decimal DeltaNow, decimal ValueNow, decimal OnHandAfter, bool Changed, string? Refusal,
    decimal? SystemQtyAtApproval, decimal? AppliedDeltaQty, decimal? UnitCost, Guid? MovementId, string? MovementNumber, bool BelowMinimum);

/// <summary>
/// Impacto da aprovação calculado agora (CA 5 e 7): sobras e faltas em R$, se o saldo mudou desde o envio e a recusa. No ajuste do
/// inventário (EST-05), <c>ReservationNote</c> avisa quando a aprovação vai reduzir reservas de OS e gerar falta.
/// </summary>
public sealed record StockAdjustmentImpact(bool ChangedSinceRequest, decimal Surplus, decimal Shortage, string? Refusal, Guid? RefusalItemId, string? ReservationNote = null);

public sealed record StockAdjustmentDetail(
    Guid Id, string Number, Guid LocationId, string LocationCode, string LocationName, StockAdjustmentMode Mode, StockAdjustmentOrigin Origin, Guid? OriginId, string? OriginNumber,
    string? OriginLink, StockAdjustmentReason Reason, string ReasonLabel, string? ReasonDetail, StockAdjustmentStatus Status, Guid? RequestedBy, string? RequestedByName,
    DateTimeOffset RequestedAt, string? DecidedByName, DateTimeOffset? DecidedAt, string? DecisionComment, bool SelfApproved, string? CancelReason, DateTimeOffset? ReversedAt,
    string? ReversedByName, string? ReversalReason, bool ReversalSelfApproved, int ItemCount, decimal SurplusValue, decimal ShortageValue, decimal DifferenceValue,
    IReadOnlyList<StockAdjustmentLineView> Lines, StockAdjustmentImpact? Impact, bool CanDecide, bool CanCancel, bool CanReverse, string? DecideBlockedReason,
    string? ReverseBlockedReason, uint Version);
