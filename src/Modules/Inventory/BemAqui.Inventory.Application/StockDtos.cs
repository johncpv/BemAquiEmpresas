using System.Text.Json;
using System.Text.Json.Serialization;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;

namespace BemAqui.Inventory.Application;

/// <summary>Situação da posição em Saldos (CA 28): "Normal", alerta "Abaixo do mínimo", crítico "Sem saldo".</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StockSituation>))]
public enum StockSituation
{
    Normal,
    Low,
    Out,
}

public sealed record BalanceListQuery(string? Term, Guid? LocationId, Guid? CategoryId, ItemType? Type, StockSituation? Situation);

/// <summary>Linha de Saldos (CA 28): físico, reservado (0 até a EST-02), disponível, mínimo do item e custo médio.</summary>
public sealed record BalanceRow(
    Guid ItemId, string ItemCode, string ItemName, string UnitCode, ItemType ItemType, Guid LocationId, string LocationCode, string LocationName,
    decimal OnHand, decimal Reserved, decimal Available, decimal MinStock, decimal AverageCost, StockSituation Situation);

public sealed record MovementListQuery(
    Guid? ItemId, Guid? LocationId, StockMovementType? Type, DateOnly? From, DateOnly? To, Guid? ProjectId, Guid? ProductionOrderId, string? SourceType, Guid? SourceId,
    string? Origin, string? Number);

/// <summary>Linha de Movimentos e do kardex (CA 24): quantidade com sinal, custo, total, saldo e custo médio depois.</summary>
public sealed record MovementRow(
    Guid Id, string Number, StockMovementType Type, DateTimeOffset At, DateTimeOffset PostedAt, long PostedSeq, Guid ItemId, string ItemCode, string ItemName, string UnitCode,
    Guid LocationId, string LocationCode, string LocationName, Guid? LotId, string? LotNumber, decimal Quantity, decimal UnitCost, decimal TotalCost, decimal BalanceAfter,
    decimal AverageCostAfter, string Origin, string SourceType, Guid SourceId, string? SourceNumber, string? SourceLink, Guid? ProjectId, Guid? ProductionOrderId,
    Guid? ReversesMovementId, Guid? ReversedByMovementId, DateTimeOffset? ReversedAt, string? UserName);

/// <summary>Quem pode estornar (CA 20) do ponto de vista de quem abriu: o autor dentro dos limites, outra pessoa com aprovar, ou ninguém.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReverseRule>))]
public enum ReverseRule
{
    Author,
    Approver,
    SoleOwner,
    Denied,
    NotManual,
    AlreadyReversed,
    IsReversal,
}

public sealed record MovementDetail(
    MovementRow Movement, string? Reason, string? ProjectNumber, string? ReversedByName, string? ReversalReason, bool ReversalSelfApproved, string? ReversesNumber,
    Guid? ManualPostingId, string? AuthorName, ReverseRule ReverseRule, string? ReverseMessage, decimal OnHandNow, decimal AverageCostNow);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReverseMovementInput(string? Reason);

/// <summary>Contexto do item para os diálogos de lançamento (padrões propositivos e impacto antes → depois).</summary>
public sealed record StockItemContext(
    Guid ItemId, string Code, string Name, Guid UnitId, string UnitCode, bool TracksLot, bool IsActive, ItemType Type, decimal AverageCost, decimal TotalOnHand,
    IReadOnlyList<StockLocationPosition> Positions, IReadOnlyList<StockLotBalance> Lots, string? SuggestedLotNumber, Guid? DefaultLocationId,
    decimal ManualApprovalAmount, decimal ManualCostDeviationPct, bool OpeningOpen);

public sealed record StockLocationPosition(Guid LocationId, string LocationCode, string LocationName, decimal OnHand, decimal Reserved, decimal Available, bool HasMovement);

public sealed record StockLotBalance(Guid LotId, string LotNumber, Guid LocationId, decimal OnHand);

public sealed record LotDetail(Guid Id, Guid ItemId, string LotNumber, Guid? SupplierPartyId, DateTimeOffset? ReceivedAt, DateOnly? Expiry, decimal OnHand);

/// <summary>Saldo inicial avulso (CA 1): data de corte ≤ hoje, quantidade na unidade de estoque ou convertida, custo &gt; 0.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OpeningBalanceInput(DateOnly CutoffDate, Guid ItemId, Guid LocationId, decimal Quantity, Guid? UnitId, decimal UnitCost, string? LotNumber);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OpeningBalanceLineInput(Guid ItemId, Guid LocationId, decimal Quantity, Guid? UnitId, decimal UnitCost, string? LotNumber);

/// <summary>Saldo inicial em lote (CA 4): todas as linhas numa transação, um número <c>MOV</c>.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OpeningBatchInput(DateOnly CutoffDate, IReadOnlyList<OpeningBalanceLineInput> Lines);

/// <summary>Entrada manual, saída avulsa ou transferência (CA 6 a 8). Lote por id (existente) ou número (a entrada cria).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManualPostingInput(
    ManualPostingKind Kind, Guid ItemId, Guid LocationId, Guid? ToLocationId, decimal Quantity, Guid? UnitId, decimal? UnitCost, Guid? LotId, string? LotNumber,
    string? Reason, Guid? ProjectId);

/// <summary>
/// Decisão do aprovador (CA 10): só o comentário e a versão; qualquer outro campo é recusado com 422 (quem aprova decide sobre o
/// que foi enviado, sem editar).
/// </summary>
public sealed record ManualDecisionInput(string? Comment, uint Version)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManualCancelInput(uint Version);

public sealed record ManualPostingListQuery(ManualPostingStatus? Status, bool AwaitingMe, ManualPostingKind? Kind);

public sealed record ManualPostingSummary(
    Guid Id, string Number, ManualPostingKind Kind, ManualPostingStatus Status, string? ApprovalReason, decimal EstimatedValue, string? ItemSummary, string? LocationName,
    int LineCount, string? RequestedByName, DateTimeOffset RequestedAt, bool AwaitingMe);

public sealed record ManualPostingLineView(
    Guid Id, int Sequence, Guid ItemId, string ItemCode, string ItemName, string UnitCode, Guid LocationId, string LocationName, Guid? ToLocationId, string? ToLocationName,
    string? LotNumber, decimal Quantity, decimal EnteredQuantity, string? EnteredUnitCode, decimal? UnitCost, Guid? MovementId, Guid? InMovementId, DateTimeOffset? ReversedAt,
    string? ReversedByName);

/// <summary>Impacto da aprovação (CA 10): saldo do local e custo médio antes → depois, calculados agora.</summary>
public sealed record ManualPostingImpact(decimal OnHandBefore, decimal OnHandAfter, decimal AverageBefore, decimal AverageAfter, decimal Value, bool Insufficient);

public sealed record ManualPostingDetail(
    Guid Id, string Number, ManualPostingKind Kind, ManualPostingStatus Status, string? ApprovalReason, decimal EstimatedValue, string? Reason, Guid? ProjectId, string? ProjectNumber,
    DateOnly? CutoffDate, Guid? RequestedBy, string? RequestedByName, DateTimeOffset RequestedAt, string? DecidedByName, DateTimeOffset? DecidedAt, string? DecisionComment,
    bool SelfApproved, DateTimeOffset? PostedAt, IReadOnlyList<ManualPostingLineView> Lines, ManualPostingImpact? Impact, bool CanDecide, bool CanCancel, string? DecideBlockedReason,
    uint Version);

/// <summary>Situação da implantação do estoque (CA 5): resumo para o diálogo e a faixa "Implantação concluída em {data} por {usuário}".</summary>
public sealed record StockOpeningStatus(
    bool Completed, DateTimeOffset? CompletedAt, string? CompletedByName, int ItemCount, int LocationCount, int LineCount, decimal TotalValue, decimal ManualApprovalAmount,
    decimal ManualCostDeviationPct, uint Version);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompleteStockOpeningInput(uint Version);
