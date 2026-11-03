using BemAqui.BuildingBlocks.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Inventory.Application.Documents;

/// <summary>
/// Folha de contagem do inventário no modelo único (EXP-04 CA 12): contagem <b>cega</b>. Local, escopo e, por item e lote, "Quantidade contada"
/// e "Contado por" em branco; as recontagens primeiro, com "Recontagem". O modelo não tem o saldo do sistema.
/// </summary>
public sealed class InventoryCountSheetPdfDocument : PdfDocumentFrame
{
    private static readonly string[] Columns = ["#", "Código", "Descrição", "Un.", "Lote", "Quantidade contada", "Contado por"];

    private readonly InventoryCountSheetModel _model;

    public InventoryCountSheetPdfDocument(PdfFrame frame, InventoryCountSheetModel model)
        : base(frame, new PdfDocumentHeader("Folha de contagem", (model ?? throw new ArgumentNullException(nameof(model))).Number, model.Date)) => _model = model;

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var m = _model;
        col.Spacing(10);
        col.Item().Element(c => Fields(c, 3, ("Local", m.Location), ("Escopo", m.Scope), ("Foto do saldo", PdfTheme.DateTime(m.SnapshotAt))));
        col.Item().Text("Conte o que está na prateleira e anote na unidade indicada. A folha não traz o saldo do sistema.").FontColor(PdfTheme.Muted);
        col.Item().Element(Lines);
        col.Item().PaddingTop(24).Row(row =>
        {
            row.RelativeItem().Element(e => Signature(e, "Contado por (nome e assinatura)"));
            row.ConstantItem(24);
            row.RelativeItem().Element(e => Signature(e, "Data da contagem"));
        });
    });

    private void Lines(IContainer c) => c.Table(table =>
    {
        table.ColumnsDefinition(cols =>
        {
            cols.ConstantColumn(22);
            cols.ConstantColumn(70);
            cols.RelativeColumn(4);
            cols.ConstantColumn(28);
            cols.ConstantColumn(60);
            cols.ConstantColumn(80);
            cols.ConstantColumn(80);
        });
        table.Header(h =>
        {
            foreach (var column in Columns)
            {
                HeaderCell(h.Cell()).Text(column).SemiBold();
            }
        });
        var sequence = 0;
        foreach (var l in _model.Lines)
        {
            BodyCell(table.Cell()).Text((++sequence).ToString(PdfTheme.PtBr));
            BodyCell(table.Cell()).Text(l.ItemCode);
            BodyCell(table.Cell()).Column(d =>
            {
                d.Item().Text(l.ItemName);
                if (l.Recount)
                {
                    d.Item().Text(InventoryPdf.RecountLabel).FontSize(PdfTheme.SmallFontSize).SemiBold().FontColor(PdfTheme.WarnInk);
                }
            });
            BodyCell(table.Cell()).Text(l.Unit);
            BodyCell(table.Cell()).Text(l.Lot ?? "—");
            for (var blank = 0; blank < 2; blank++)
            {
                // Em branco: quem conta anota à mão.
                table.Cell().PaddingVertical(3).PaddingHorizontal(3).AlignBottom().Height(16).BorderBottom(0.75f).BorderColor(PdfTheme.Line);
            }
        }
    });
}
