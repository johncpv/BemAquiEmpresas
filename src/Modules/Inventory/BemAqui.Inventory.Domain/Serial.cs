using BemAqui.Inventory.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Inventory.Domain;

/// <summary>
/// Número de série de uma unidade do produto acabado (<c>inventory.serials</c>, PRD-06a2 CA 4 e 5, R-EST-8): nasce <c>InStock</c> na
/// conclusão da OS, no local de entrada; vai a <c>Shipped</c> no romaneio (PRD-06b) e a <c>Cancelled</c> no estorno da conclusão (CA 10).
/// Único por <c>(item_id, serial_number)</c>. A OS e o projeto são de outros módulos: só o id, sem FK (ADR 0006).
/// </summary>
[DocumentName("A série")]
public sealed class Serial : Entity, IAuditable, IConcurrencyVersioned
{
    public Guid ItemId { get; init; }

    public string SerialNumber { get; init; } = string.Empty;

    public Guid? ProductionOrderId { get; init; }

    public Guid? ProjectId { get; init; }

    public Guid? LocationId { get; set; }

    public SerialState Status { get; set; } = SerialState.InStock;

    /// <summary>Movimento <c>Production</c> que deu entrada da unidade (o kardex do item).</summary>
    public Guid? EntryMovementId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CancelledAt { get; set; }

    public uint Version { get; set; }
}

/// <summary>
/// Lote de matéria-prima consumido pela OS (<c>inventory.serial_components</c>, PRD-06a2 CA 8, ponto 87): o líquido dos movimentos
/// <c>Issue</c> − <c>Return</c> da OS por item e lote, gravado na conclusão. Cada série herda os lotes da OS inteira, por isso a linha
/// nasce sem série (<c>SerialId</c> nulo = vale para todas as séries da OS); o estorno da conclusão marca <c>ReversedAt</c>. Só
/// referências, sem custo; só inclusão, fora da auditoria (o movimento é a trilha).
/// </summary>
public sealed class SerialComponent : Entity
{
    public Guid ProductionOrderId { get; init; }

    public Guid? SerialId { get; init; }

    public Guid ItemId { get; init; }

    /// <summary>Lote consumido; nulo para material sem controle de lote (entra pelo item e pela quantidade).</summary>
    public Guid? LotId { get; init; }

    public decimal Quantity { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ReversedAt { get; set; }
}

/// <summary>
/// Último número de série emitido por item e ano (<c>inventory.serial_sequences</c>, D15): a sequência da série é por item e ano, e o
/// <c>shared.number_sequences</c> é por tipo de documento (sem lugar para o item). Incremento atômico com <c>INSERT … ON CONFLICT DO
/// UPDATE … RETURNING</c>, na transação da conclusão: no rollback, o número volta.
/// </summary>
public sealed class SerialSequence
{
    public Guid ItemId { get; init; }

    public int Year { get; init; }

    public long LastValue { get; set; }
}
