using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Engineering.Domain;

namespace BemAqui.Engineering.Application.Documents;

/// <summary>
/// O que o PDF da BOM mostra (EXP-05 CA 3): a OS, o produto, a revisão, a situação, quem liberou e quando, de onde foi copiada e as linhas
/// (código, descrição, quantidade por unidade, unidade, perda, necessidade da OS e observação). <b>Não existe</b> campo de custo aqui, para
/// nenhum usuário (o custo previsto da tela fica com <c>projects.view_cost</c> e não vai para o papel da fábrica).
/// </summary>
public sealed record BomPdfModel(
    string OrderNumber, string Product, decimal OrderQuantity, int Revision, BomStatus Status, string StatusLabel, string? ReleasedBy, DateTimeOffset? ReleasedAt,
    int? SupersededBy, string? CopiedFrom, IReadOnlyList<BomPdfLine> Lines, DateOnly IssueDate);

public sealed record BomPdfLine(int Sequence, string Code, string Description, decimal Quantity, string Unit, decimal ScrapPct, decimal Required, string StockUnit, string? Notes);

/// <summary>Montagem pura do PDF da BOM (EXP-05 CA 3), testada sem banco: marca por situação, revisão que substituiu e o limite de linhas.</summary>
public static class BomPdf
{
    public const int MaxLines = 300;

    public static string StatusLabel(BomStatus status) => status switch
    {
        BomStatus.Draft => "Rascunho",
        BomStatus.Released => "Liberada",
        _ => "Substituída",
    };

    /// <summary><c>Draft</c>: "Rascunho"; <c>Superseded</c>: a faixa "Revisão substituída pela {n}"; <c>Released</c>: nada.</summary>
    public static (PdfMark Mark, string? Notice) Mark(BomStatus status, int? supersededBy) => status switch
    {
        BomStatus.Draft => (PdfMark.Draft, null),
        BomStatus.Superseded => (PdfMark.None, supersededBy is { } n ? $"Revisão substituída pela {n}" : "Revisão substituída"),
        _ => (PdfMark.None, null),
    };

    /// <summary>A revisão que substituiu esta: a primeira liberada (vigente ou já substituída) com número maior.</summary>
    public static int? SupersededBy(int revision, IEnumerable<BomRevisionSummary> revisions)
    {
        ArgumentNullException.ThrowIfNull(revisions);
        return revisions.Where(r => r.Revision > revision && r.Status != BomStatus.Draft).OrderBy(r => r.Revision).Select(r => (int?)r.Revision).FirstOrDefault();
    }

    public static BomPdfModel Build(BomDetail bom, BomOrderHeader order, IReadOnlyList<BomRevisionSummary> revisions, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(bom);
        ArgumentNullException.ThrowIfNull(order);
        var product = order.ProductName is { Length: > 0 } name ? (order.ProductCode is { Length: > 0 } code ? $"{code} · {name}" : name) : "Sem item de estoque";
        var copied = bom.CopiedFromOrderNumber is { } from ? $"{from}, revisão {bom.CopiedFromRevision}" : null;
        var lines = bom.Lines.Select(l => new BomPdfLine(l.Sequence, l.ItemCode, l.ItemName, l.Quantity, l.UnitCode, l.ScrapPct, l.RequiredQuantity, l.StockUnitCode, l.Notes)).ToList();
        return new BomPdfModel(order.Number, product, order.Quantity, bom.Revision, bom.Status, StatusLabel(bom.Status), bom.ReleasedByName, bom.ReleasedAt,
            bom.Status == BomStatus.Superseded ? SupersededBy(bom.Revision, revisions) : null, copied, lines, today);
    }

    /// <summary>Nome do arquivo: <c>BOM-{OS}-rev{n}.pdf</c>.</summary>
    public static string FileName(string orderNumber, int revision) => $"BOM-{orderNumber}-rev{revision}.pdf";
}
