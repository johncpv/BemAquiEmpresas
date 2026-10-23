using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Engineering.Domain;

/// <summary>
/// Situação da revisão da BOM (ENG-02, ponto 25), gravada como texto: <c>Draft</c> (rascunho, editável, sem efeito em estoque),
/// <c>Released</c> (vigente; uma por OS, índice único parcial) e <c>Superseded</c> (substituída por uma revisão liberada depois).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BomStatus>))]
public enum BomStatus
{
    Draft,
    Released,
    Superseded,
}

/// <summary>
/// Lista de materiais da OS de fabricação (<c>engineering.boms</c>, ENG-02, R-ENG-3 a R-ENG-5): uma linha por revisão, com o número
/// crescente por OS. O rascunho é editável à vontade; a liberada nunca é editada nem apagada, só revisada (CA 21). A OS é só o id,
/// validada pelo <c>IProductionOrders</c> (ADR 0006).
/// </summary>
[DocumentName("A BOM")]
public sealed class Bom : Entity, IAuditable, IConcurrencyVersioned
{
    public Guid ProductionOrderId { get; init; }

    /// <summary>Revisão: 0 na primeira, + 1 a cada "Revisar BOM" (único por OS).</summary>
    public int Revision { get; init; }

    public BomStatus Status { get; private set; } = BomStatus.Draft;

    public DateTimeOffset? ReleasedAt { get; private set; }

    public Guid? ReleasedBy { get; private set; }

    /// <summary>BOM de origem da cópia (outra OS, CA 5) ou a liberada que a revisão copiou (CA 12).</summary>
    public Guid? CopiedFromBomId { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public uint Version { get; set; }

    public bool IsDraft => Status == BomStatus.Draft;

    /// <summary>Libera o rascunho (CA 8 e 13); só o rascunho é liberado.</summary>
    public void Release(DateTimeOffset at, Guid? by)
    {
        if (Status != BomStatus.Draft)
        {
            throw new InvalidOperationException("Só o rascunho da BOM é liberado.");
        }

        Status = BomStatus.Released;
        ReleasedAt = at;
        ReleasedBy = by;
    }

    /// <summary>A revisão liberada depois desta passa a valer (CA 13).</summary>
    public void Supersede()
    {
        if (Status != BomStatus.Released)
        {
            throw new InvalidOperationException("Só a BOM liberada é substituída.");
        }

        Status = BomStatus.Superseded;
    }
}

/// <summary>
/// Linha da BOM (<c>engineering.bom_lines</c>): item, quantidade <b>por unidade do produto</b> (<c>numeric(18,4)</c>, &gt; 0) na unidade
/// escolhida, perda % (<c>numeric(7,4)</c>, 0 a 100) e observação. <see cref="RequiredQuantity"/> é a necessidade da OS já na unidade
/// de estoque (qtd × qtd da OS × (1 + perda %), convertida e arredondada em 4 casas), recalculada a cada gravação e na liberação: é o
/// que o evento e o <c>IBoms</c> levam, para o Inventory não recalcular conversão nem perda.
/// </summary>
public sealed class BomLine : Entity, IAuditable
{
    public Guid BomId { get; init; }

    /// <summary>Posição na lista (1..n), regravada a cada salvar.</summary>
    public int Sequence { get; set; }

    public Guid ItemId { get; set; }

    public decimal Quantity { get; set; }

    public Guid UnitId { get; set; }

    public decimal ScrapPct { get; set; }

    public string? Notes { get; set; }

    public decimal RequiredQuantity { get; set; }
}
