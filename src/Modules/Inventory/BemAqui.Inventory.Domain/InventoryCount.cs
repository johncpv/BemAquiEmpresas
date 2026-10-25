using System.Text.Json.Serialization;
using BemAqui.Inventory.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Inventory.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<InventoryCountStatus>))]
public enum InventoryCountStatus
{
    Counting,
    Closed,
    Cancelled,
}

/// <summary>O que fazer com as linhas não contadas no fechamento (CA 9): considerar zero ou manter o saldo do sistema (sem diferença).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<UncountedPolicy>))]
public enum UncountedPolicy
{
    Zero,
    KeepSystem,
}

/// <summary>
/// Inventário de um local (<c>inventory.inventory_counts</c>, EST-05, D15 <c>INV-AAAA-0001</c>): a foto do saldo na abertura
/// (<c>snapshot_at</c>), a contagem cega por item e lote, a recontagem das divergências por outra pessoa e o fechamento, que gera um
/// ajuste da EST-01b em modo Delta (<c>adjustment_id</c>). Enquanto <c>Counting</c>, o local fica bloqueado para ajuste e estorno
/// manual (<see cref="InventoryCountRules"/>, CA 6); um só <c>Counting</c> por local (índice único parcial). O inventário nunca mexe no
/// saldo: todo efeito passa pelo ajuste.
/// </summary>
[DocumentName("O inventário")]
public sealed class InventoryCount : Entity, IAuditable, IConcurrencyVersioned
{
    public string Number { get; init; } = string.Empty;

    public Guid LocationId { get; init; }

    /// <summary>Escopo da abertura em jsonb (<see cref="InventoryCountScope"/>): itens com saldo ou também os só com movimento, categoria e tipo.</summary>
    public string Scope { get; init; } = "{}";

    public InventoryCountStatus Status { get; set; }

    /// <summary>Horário da foto do saldo (CA 1).</summary>
    public DateTimeOffset SnapshotAt { get; init; }

    public Guid? OpenedBy { get; init; }

    public Guid? ClosedBy { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public UncountedPolicy? UncountedPolicy { get; set; }

    /// <summary>Ajuste gerado no último fechamento (CA 10); a reabertura (CA 12) limpa e o histórico guarda o anterior.</summary>
    public Guid? AdjustmentId { get; set; }

    public string? AdjustmentNumber { get; set; }

    public string? CancelReason { get; set; }

    public Guid? CancelledBy { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>Quantas vezes a contagem foi reaberta depois de um ajuste reprovado (CA 12).</summary>
    public int ReopenCount { get; set; }

    public uint Version { get; set; }
}

/// <summary>
/// Linha do inventário (<c>inventory.inventory_count_lines</c>): item e lote, o saldo do sistema na foto (<c>system_qty</c>), a
/// quantidade contada, quem contou e quando, e o saldo do sistema no momento da contagem (<c>system_qty_at_count</c>, nunca mostrado a
/// quem conta), de onde sai o movimento durante a contagem (<c>movement_delta</c>, CA 5). A recontagem (CA 7 e 8) é uma linha nova com
/// <c>recount_of_line_id</c>; a anterior fica <c>superseded</c> e continua no histórico. No fechamento grava a diferença e o custo médio.
/// </summary>
public sealed class InventoryCountLine : Entity, IAuditable, IConcurrencyVersioned
{
    public Guid CountId { get; init; }

    public int Sequence { get; init; }

    public Guid ItemId { get; init; }

    public Guid? LotId { get; init; }

    public decimal SystemQty { get; init; }

    public decimal? CountedQty { get; set; }

    /// <summary>O que foi digitado, na unidade escolhida (R-EST-3).</summary>
    public decimal? EnteredQuantity { get; set; }

    public Guid? EnteredUnitId { get; set; }

    public Guid? CountedBy { get; set; }

    public DateTimeOffset? CountedAt { get; set; }

    /// <summary>Saldo do sistema (lote ou item no local) quando a linha foi contada: a foto + os movimentos até ali.</summary>
    public decimal? SystemQtyAtCount { get; set; }

    /// <summary>Movimentos do item e lote no local entre a foto e a contagem da linha (CA 5); gravado no fechamento.</summary>
    public decimal? MovementDelta { get; set; }

    /// <summary>Contado − (foto + movimentos), gravada no fechamento (CA 5 e 10).</summary>
    public decimal? Difference { get; set; }

    /// <summary>Custo médio no fechamento, para o valor da divergência.</summary>
    public decimal? UnitCost { get; set; }

    /// <summary>Recontagem: a linha que esta substitui (CA 7).</summary>
    public Guid? RecountOfLineId { get; init; }

    /// <summary>Substituída por uma recontagem: fica no histórico, fora da diferença.</summary>
    public bool Superseded { get; set; }

    /// <summary>Incluída durante a contagem ("Encontrei outro item", CA 3).</summary>
    public bool AddedDuringCount { get; init; }

    public uint Version { get; set; }
}

/// <summary>Escopo da abertura (CA 1): o padrão é "itens com saldo no local"; com movimento inclui os zerados que já tiveram movimento.</summary>
public sealed record InventoryCountScope(bool IncludeZeroWithMovement, Guid? CategoryId, ItemType? ItemType);
