using System.Text.Json.Serialization;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;

namespace BemAqui.Inventory.Application;

/// <summary>Escopo da abertura (CA 1): o padrão é "itens com saldo no local"; com movimento inclui os zerados; filtro por categoria e tipo.</summary>
public sealed record InventoryCountScopeInput(bool IncludeZeroWithMovement, Guid? CategoryId, ItemType? ItemType);

/// <summary>"Abrir inventário" (CA 1): o local e o escopo.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OpenInventoryCountInput(Guid LocationId, InventoryCountScopeInput? Scope);

/// <summary>"Salvar contagem" de uma linha (CA 3 e 16): a quantidade contada, a unidade (nula = de estoque) e a versão da linha.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CountLineInput(decimal Quantity, Guid? UnitId, uint Version);

/// <summary>"Encontrei outro item" (CA 3): item e lote (já cadastrado) que não estavam na lista, com a quantidade contada.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FoundLineInput(Guid ItemId, Guid? LotId, decimal Quantity, Guid? UnitId);

/// <summary>"Pedir recontagem" (CA 7): as linhas contadas que outra pessoa vai recontar, com a versão do inventário.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecountInput(IReadOnlyList<Guid>? LineIds, uint Version);

/// <summary>"Fechar inventário" (CA 9 e 10): a escolha para os não contados (obrigatória quando houver) e a versão.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CloseInventoryCountInput(UncountedPolicy? UncountedPolicy, uint Version);

/// <summary>"Cancelar inventário" (CA 13): motivo obrigatório e a versão.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CancelInventoryCountInput(string? Reason, uint Version);

/// <summary>"Reabrir contagem" (CA 12): só a versão.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReopenInventoryCountInput(uint Version);

public sealed record InventoryCountListQuery(InventoryCountStatus? Status, Guid? LocationId);

/// <summary>Linha da lista "Inventários": número, local, situação, foto, quem abriu, o progresso e o ajuste gerado.</summary>
public sealed record InventoryCountSummary(
    Guid Id, string Number, Guid LocationId, string LocationName, InventoryCountStatus Status, DateTimeOffset SnapshotAt, string? OpenedByName, int LineCount, int CountedCount,
    Guid? AdjustmentId, string? AdjustmentNumber);

/// <summary>
/// Detalhe do inventário, sem saldo do sistema nem custo (quem só conta também abre): cabeçalho, escopo, progresso
/// ("{x} de {y} itens contados"), recontagens pendentes, a situação do ajuste gerado (CA 12) e o que o usuário pode fazer.
/// </summary>
public sealed record InventoryCountDetail(
    Guid Id, string Number, Guid LocationId, string LocationCode, string LocationName, InventoryCountStatus Status, string StatusLabel, string ScopeLabel, DateTimeOffset SnapshotAt,
    string? OpenedByName, string? ClosedByName, DateTimeOffset? ClosedAt, UncountedPolicy? UncountedPolicy, string? CancelReason, string? CancelledByName, DateTimeOffset? CancelledAt,
    int ReopenCount, int LineCount, int CountedCount, int RecountPending, string Progress, Guid? AdjustmentId, string? AdjustmentNumber, StockAdjustmentStatus? AdjustmentStatus,
    string? AdjustmentSituation, bool CanCount, bool CanManage, bool CanCancel, bool CanReopen, uint Version);

/// <summary>Unidade em que a linha pode ser contada: a de estoque e as das conversões do item (R-EST-3).</summary>
public sealed record CountUnitOption(Guid Id, string Code);

/// <summary>
/// Linha da contagem cega (CA 3, 15): item, lote, unidade e o que foi contado, por quem e quando. Sem saldo do sistema, sem
/// movimento, sem diferença e sem custo (teste de contrato).
/// </summary>
public sealed record InventoryCountingLine(
    Guid Id, int Sequence, Guid ItemId, string ItemCode, string ItemName, string UnitCode, Guid StockUnitId, IReadOnlyList<CountUnitOption> Units, bool TracksLot, Guid? LotId,
    string? LotNumber, decimal? CountedQty, decimal? EnteredQuantity, string? EnteredUnitCode, string? CountedByName, DateTimeOffset? CountedAt, bool CountedByMe, bool IsRecount,
    bool AddedDuringCount, uint Version);

/// <summary>A contagem (backoffice e celular): só as linhas que o usuário pode contar (a recontagem esconde as que ele contou, CA 8).</summary>
public sealed record InventoryCountingView(
    Guid CountId, string Number, Guid LocationId, string LocationName, InventoryCountStatus Status, int LineCount, int CountedCount, string Progress, bool CanCount,
    IReadOnlyList<InventoryCountingLine> Lines, uint Version);

/// <summary>
/// Divergência (CA 5, 7 e 10), só com <c>inventory.adjust</c>: a foto, o contado, os movimentos durante a contagem, a diferença, o
/// valor pelo custo médio e o antes → depois sobre o saldo de agora (ou o gravado no fechamento).
/// </summary>
public sealed record InventoryDivergenceLine(
    Guid Id, int Sequence, Guid ItemId, string ItemCode, string ItemName, string UnitCode, Guid? LotId, string? LotNumber, decimal SystemQty, decimal? CountedQty, bool Counted,
    string? CountedByName, DateTimeOffset? CountedAt, decimal MovementDelta, bool HadMovement, decimal Difference, decimal UnitCost, decimal Value, decimal SystemQtyNow, decimal After,
    bool IsRecount, string? PreviousCountedByName, decimal? PreviousCountedQty, bool RecountPending, bool CanRequestRecount);

/// <summary>Divergências do inventário (CA 7, 9 e 10): as linhas com diferença por valor, os não contados e o impacto do fechamento.</summary>
public sealed record InventoryDivergences(
    Guid CountId, string Number, InventoryCountStatus Status, int Uncounted, string? UncountedQuestion, UncountedPolicy? Policy, decimal Surplus, decimal Shortage, decimal Net,
    IReadOnlyList<InventoryDivergenceLine> Lines, uint Version);
