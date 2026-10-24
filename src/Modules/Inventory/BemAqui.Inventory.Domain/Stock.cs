using System.Text.Json.Serialization;
using BemAqui.Inventory.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Inventory.Domain;

/// <summary>
/// Posição agregada do item no local (<c>inventory.stock_positions</c>, ADR 0023 §2): físico e reservado, uma linha por item e
/// local, com os <c>CHECK</c>s no banco. A reserva (EST-02) trava aqui, nunca no lote. Só o motor escreve (teste de arquitetura).
/// </summary>
public sealed class StockPosition : Entity
{
    private StockPosition()
    {
    }

    public Guid ItemId { get; private set; }

    public Guid LocationId { get; private set; }

    public decimal QuantityOnHand { get; private set; }

    public decimal QuantityReserved { get; private set; }

    /// <summary>Disponível = físico − reservado.</summary>
    public decimal Available => QuantityOnHand - QuantityReserved;

    public void ApplyOnHand(decimal delta) => QuantityOnHand += delta;

    /// <summary>Reservado da posição (EST-02): só o <c>ReservationAllocator</c>, sob a trava do item, na mesma transação da reserva.</summary>
    public void ApplyReserved(decimal delta) => QuantityReserved += delta;
}

/// <summary>
/// Físico por item, local e lote (<c>inventory.stock_balances</c>, ADR 0023 §2), com <c>lot_id</c> nulo para o item sem lote.
/// Muda na mesma transação da posição: Σ lotes = posição.
/// </summary>
public sealed class StockBalance : Entity
{
    private StockBalance()
    {
    }

    public Guid ItemId { get; private set; }

    public Guid LocationId { get; private set; }

    public Guid? LotId { get; private set; }

    public decimal QuantityOnHand { get; private set; }

    public void ApplyOnHand(decimal delta) => QuantityOnHand += delta;
}

/// <summary>Lote do item (<c>inventory.lots</c>), único por <c>(item_id, lot_number)</c>; nasce na primeira entrada com o número.</summary>
public sealed class Lot : Entity, IAuditable
{
    public Guid ItemId { get; init; }

    public string LotNumber { get; init; } = string.Empty;

    public Guid? SupplierPartyId { get; init; }

    public DateTimeOffset? ReceivedAt { get; init; }

    public DateOnly? Expiry { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Movimento de estoque (<c>inventory.stock_movements</c>, ADR 0023 §4): imutável (trigger recusa UPDATE e DELETE; nenhum setter
/// público). Grava o físico da posição e o custo médio depois dele; o kardex é lido por <see cref="PostedSeq"/>.
/// </summary>
public sealed class StockMovement : Entity
{
    private StockMovement()
    {
    }

    public string Number { get; private set; } = string.Empty;

    public StockMovementType Type { get; private set; }

    public Guid ItemId { get; private set; }

    public Guid LocationId { get; private set; }

    public Guid? LotId { get; private set; }

    public Guid? SerialId { get; private set; }

    /// <summary>Com sinal, na unidade de estoque: entrada positiva, saída negativa.</summary>
    public decimal Quantity { get; private set; }

    public decimal UnitCost { get; private set; }

    /// <summary>Quantidade × custo, com o sinal da quantidade, em 2 casas <c>AwayFromZero</c>.</summary>
    public decimal TotalCost { get; private set; }

    /// <summary>Físico da posição (item, local) depois do movimento.</summary>
    public decimal BalanceAfter { get; private set; }

    /// <summary>Custo médio do item depois do movimento.</summary>
    public decimal AverageCostAfter { get; private set; }

    /// <summary>Ordem de lançamento (sequência do banco): o kardex segue esta ordem, nunca a data de negócio.</summary>
    public long PostedSeq { get; private set; }

    public string SourceType { get; private set; } = string.Empty;

    public Guid SourceId { get; private set; }

    /// <summary>Número legível do documento de origem (o lançamento manual usa o próprio <c>MOV</c>).</summary>
    public string? SourceNumber { get; private set; }

    public int Leg { get; private set; }

    public Guid? ReversesMovementId { get; private set; }

    public string? Reason { get; private set; }

    /// <summary><c>Manual</c> (lançamento do estoque) ou <c>Document</c> (outro documento, estornado só por ele).</summary>
    public string Origin { get; private set; } = string.Empty;

    public Guid? ProjectId { get; private set; }

    public Guid? ProductionOrderId { get; private set; }

    /// <summary>Data de negócio (UTC); o saldo inicial fica em D 23:59:59 de São Paulo (ADR 0016).</summary>
    public DateTimeOffset At { get; private set; }

    public DateTimeOffset PostedAt { get; private set; }

    public Guid? UserId { get; private set; }

    public bool IsManual => Origin == StockOrigins.Manual;

    public static StockMovement Create(
        string number, StockMovementType type, Guid itemId, Guid locationId, Guid? lotId, Guid? serialId, decimal quantity, decimal unitCost,
        decimal balanceAfter, decimal averageCostAfter, string sourceType, Guid sourceId, int leg, Guid? reversesMovementId, string? reason,
        Guid? projectId, Guid? productionOrderId, DateTimeOffset at, DateTimeOffset postedAt, Guid? userId, string? sourceNumber = null) => new()
        {
            Number = number,
            Type = type,
            ItemId = itemId,
            LocationId = locationId,
            LotId = lotId,
            SerialId = serialId,
            Quantity = quantity,
            UnitCost = unitCost,
            TotalCost = AverageCostCalculator.LineTotal(quantity, unitCost),
            BalanceAfter = balanceAfter,
            AverageCostAfter = averageCostAfter,
            SourceType = sourceType,
            SourceId = sourceId,
            SourceNumber = sourceNumber ?? (sourceType == StockSources.ManualPosting ? number : null),
            Leg = leg,
            ReversesMovementId = reversesMovementId,
            Reason = reason,
            Origin = sourceType == StockSources.ManualPosting ? StockOrigins.Manual : StockOrigins.Document,
            ProjectId = projectId,
            ProductionOrderId = productionOrderId,
            At = at,
            PostedAt = postedAt,
            UserId = userId,
        };
}

public static class StockOrigins
{
    public const string Manual = "Manual";
    public const string Document = "Document";
}

/// <summary>Tipo do lançamento manual (D7): saldo inicial, entrada manual, saída avulsa e transferência.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ManualPostingKind>))]
public enum ManualPostingKind
{
    OpeningBalance,
    Receipt,
    Issue,
    Transfer,
}

[JsonConverter(typeof(JsonStringEnumConverter<ManualPostingStatus>))]
public enum ManualPostingStatus
{
    Pending,
    Posted,
    Rejected,
    Cancelled,
}

/// <summary>
/// Lançamento manual (<c>inventory.manual_postings</c>, D7, ADR 0023 §5): nasce com o número <c>MOV</c>; abaixo dos limites grava
/// <c>Posted</c> na mesma transação; acima, fica <c>Pending</c> sem mexer no saldo até outra pessoa com <c>inventory.approve</c>
/// decidir sobre o que foi enviado.
/// </summary>
[DocumentName("O lançamento")]
public sealed class ManualPosting : Entity, IAuditable, IConcurrencyVersioned
{
    public string Number { get; init; } = string.Empty;

    public ManualPostingKind Kind { get; init; }

    public ManualPostingStatus Status { get; set; }

    /// <summary>Por que precisa de aprovação ("Acima de R$ 1.000,00", "Custo 25% acima do médio"); null = não precisa.</summary>
    public string? ApprovalReason { get; init; }

    /// <summary>Valor estimado no envio (quantidade × custo), para a regra e a lista.</summary>
    public decimal EstimatedValue { get; init; }

    public string? Reason { get; init; }

    public Guid? ProjectId { get; init; }

    /// <summary>Data de corte do saldo inicial (ADR 0016).</summary>
    public DateOnly? CutoffDate { get; init; }

    public Guid? RequestedBy { get; init; }

    public DateTimeOffset RequestedAt { get; init; }

    public Guid? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public string? DecisionComment { get; set; }

    public bool SelfApproved { get; set; }

    public DateTimeOffset? PostedAt { get; set; }

    public uint Version { get; set; }
}

/// <summary>
/// Linha do lançamento manual (<c>inventory.manual_posting_lines</c>): o que foi enviado, já na unidade de estoque, e os
/// movimentos que nasceram dela. O estorno fica registrado aqui (auditado); o movimento original nunca muda.
/// </summary>
public sealed class ManualPostingLine : Entity, IAuditable
{
    public Guid PostingId { get; init; }

    public int Sequence { get; init; }

    public Guid ItemId { get; init; }

    public Guid LocationId { get; init; }

    /// <summary>Destino da transferência.</summary>
    public Guid? ToLocationId { get; init; }

    public Guid? LotId { get; set; }

    /// <summary>Número do lote informado; o lote nasce quando o lançamento grava.</summary>
    public string? LotNumber { get; init; }

    /// <summary>Na unidade de estoque, sempre positiva (o tipo dá o sentido).</summary>
    public decimal Quantity { get; init; }

    public decimal EnteredQuantity { get; init; }

    public Guid? EnteredUnitId { get; init; }

    /// <summary>Custo unitário informado (entrada e saldo inicial); a saída usa o custo médio do momento.</summary>
    public decimal? UnitCost { get; init; }

    /// <summary>Movimento da linha (na transferência, a perna de saída).</summary>
    public Guid? MovementId { get; set; }

    /// <summary>Perna de entrada da transferência.</summary>
    public Guid? InMovementId { get; set; }

    public DateTimeOffset? ReversedAt { get; set; }

    public Guid? ReversedBy { get; set; }

    public string? ReversalReason { get; set; }

    public bool ReversalSelfApproved { get; set; }
}

/// <summary>
/// Parâmetros do estoque (<c>inventory.parameters</c>, linha única do tenant, EST-01a): limite de valor e de desvio de custo dos
/// lançamentos manuais (D7) e o fim da janela do saldo inicial. Sem tela nesta story: valores do seed.
/// </summary>
[DocumentName("A implantação do estoque")]
public sealed class InventoryParameters : Entity, IAuditable, IConcurrencyVersioned
{
    public const decimal DefaultApprovalAmount = 1000m;
    public const decimal DefaultCostDeviationPct = 20m;

    public decimal ManualApprovalAmount { get; set; } = DefaultApprovalAmount;

    public decimal ManualCostDeviationPct { get; set; } = DefaultCostDeviationPct;

    public DateTimeOffset? OpeningCompletedAt { get; set; }

    public Guid? OpeningCompletedBy { get; set; }

    /// <summary>Quando a recomposição das reservas da implantação terminou (EST-02 CA 12, D3); depois disso o job não faz nada.</summary>
    public DateTimeOffset? ReservationsBackfilledAt { get; set; }

    /// <summary>Valor inicial da sequência da série por item e ano (PRD-06a2 CA 4, D15): padrão 1; sem tela nesta fase.</summary>
    public int SerialStartValue { get; set; } = 1;

    public uint Version { get; set; }
}
