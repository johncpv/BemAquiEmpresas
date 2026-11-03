using BemAqui.BuildingBlocks.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Engineering.Application.Documents;

/// <summary>
/// BOM da OS no modelo único (EXP-05 CA 3): OS, produto, revisão e situação, quem liberou, as linhas com a necessidade da OS e o "Conferido por".
/// "Rascunho" na revisão em rascunho; a faixa "Revisão substituída pela {n}" na substituída. Sem custo.
/// </summary>
public sealed class BomPdfDocument : PdfDocumentFrame
{
    private static readonly string[] Columns = ["#", "Código", "Descrição", "Qtd. por un.", "Un.", "Perda", "Necessidade da OS", "Observação"];

    private readonly BomPdfModel _model;

    public BomPdfDocument(PdfFrame frame, BomPdfModel model)
        : base(frame, Header(model)) => _model = model;

    private static PdfDocumentHeader Header(BomPdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var (mark, notice) = BomPdf.Mark(model.Status, model.SupersededBy);
        return new PdfDocumentHeader("Lista de materiais (BOM)", $"{model.OrderNumber} · rev. {model.Revision}", model.IssueDate, mark, Notice: notice);
    }

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var m = _model;
        col.Spacing(8);
        col.Item().Element(c => Fields(c, 3,
            ("OS", m.OrderNumber),
            ("Produto", m.Product),
            ("Quantidade da OS", PdfTheme.Quantity(m.OrderQuantity)),
            ("Revisão", m.Revision.ToString(PdfTheme.PtBr)),
            ("Situação", m.StatusLabel),
            ("Liberada", m.ReleasedAt is { } at ? $"{PdfTheme.DateTime(at)}{(m.ReleasedBy is { } by ? " por " + by : string.Empty)}" : null),
            ("Copiada de", m.CopiedFrom)));
        col.Item().Element(c => Section(c, "Materiais"));
        col.Item().Element(Lines);
        col.Item().PaddingTop(24).Row(row =>
        {
            row.RelativeItem().Element(e => Signature(e, "Conferido por (nome e assinatura)"));
            row.ConstantItem(24);
            row.RelativeItem().Element(e => Signature(e, "Data"));
        });
    });

    private void Lines(IContainer c)
    {
        if (_model.Lines.Count == 0)
        {
            c.Text("BOM sem material: esta OS usa só mão de obra.").FontColor(PdfTheme.Muted);
            return;
        }

        c.Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(20);
                cols.ConstantColumn(64);
                cols.RelativeColumn(4);
                cols.ConstantColumn(54);
                cols.ConstantColumn(26);
                cols.ConstantColumn(34);
                cols.ConstantColumn(62);
                cols.RelativeColumn(2);
            });
            table.Header(h =>
            {
                for (var i = 0; i < Columns.Length; i++)
                {
                    var cell = HeaderCell(h.Cell());
                    (i is 3 or 5 or 6 ? cell.AlignRight() : cell).Text(Columns[i]).SemiBold();
                }
            });
            foreach (var l in _model.Lines)
            {
                BodyCell(table.Cell()).Text(l.Sequence.ToString(PdfTheme.PtBr));
                BodyCell(table.Cell()).Text(l.Code);
                BodyCell(table.Cell()).Text(l.Description);
                BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Quantity(l.Quantity));
                BodyCell(table.Cell()).PaddingLeft(3).Text(l.Unit);
                BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Quantity(l.ScrapPct) + "%");
                BodyCell(table.Cell()).AlignRight().Text($"{PdfTheme.Quantity(l.Required)} {l.StockUnit}");
                BodyCell(table.Cell()).Text(l.Notes ?? string.Empty);
            }
        });
    }
}
