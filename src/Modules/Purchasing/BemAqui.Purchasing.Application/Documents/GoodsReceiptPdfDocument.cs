using BemAqui.BuildingBlocks.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Purchasing.Application.Documents;

/// <summary>
/// Comprovante de recebimento no modelo único (EXP-04 CA 9): dados do recebimento, os itens recebidos com lote e local e as assinaturas
/// "Recebido por" e "Conferido por". Estornado sai com "Cancelado" e o motivo. Sem custo de entrada, preço nem valor.
/// </summary>
public sealed class GoodsReceiptPdfDocument : PdfDocumentFrame
{
    private static readonly string[] Columns = ["#", "Código", "Descrição", "Qtd. recebida", "Un.", "Lote", "Local"];

    private readonly GoodsReceiptPdfModel _model;

    public GoodsReceiptPdfDocument(PdfFrame frame, GoodsReceiptPdfModel model)
        : base(frame, Header(model)) => _model = model;

    private static PdfDocumentHeader Header(GoodsReceiptPdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var (mark, notice) = GoodsReceiptPdf.Mark(model.Reversed, model.ReversalReason);
        return new PdfDocumentHeader("Comprovante de recebimento", model.Number, model.ReceivedOn, mark, Notice: notice);
    }

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var m = _model;
        col.Spacing(10);
        col.Item().Element(c => Fields(c, 3,
            ("Pedido de compra", m.PurchaseOrderNumber),
            ("Fornecedor", m.SupplierName),
            ("Nota do fornecedor", m.SupplierInvoice),
            ("Data do recebimento", PdfTheme.Date(m.ReceivedOn)),
            ("Recebido por", m.ReceivedByName),
            ("Projeto", m.ProjectNumber ?? "Estoque geral")));
        if (m.SoleOwner)
        {
            col.Item().Text(GoodsReceiptPdf.SoleOwnerNotice).SemiBold().FontColor(PdfTheme.WarnInk);
        }

        if (m.Reversed && m.ReversedAt is { } at)
        {
            col.Item().Text($"Estornado em {PdfTheme.DateTime(at)}{(m.ReversedByName is { Length: > 0 } by ? " por " + by : string.Empty)}.").FontColor(PdfTheme.CritInk);
        }

        col.Item().Element(c => Section(c, "Itens recebidos"));
        col.Item().Element(Lines);
        if (m.Notes is { Length: > 0 } notes)
        {
            col.Item().Text("Observações").SemiBold();
            col.Item().Text(notes);
        }

        col.Item().PaddingTop(24).Row(row =>
        {
            row.RelativeItem().Element(e => Signature(e, "Recebido por"));
            row.ConstantItem(24);
            row.RelativeItem().Element(e => Signature(e, "Conferido por"));
        });
    });

    private void Lines(IContainer c) => c.Table(table =>
    {
        table.ColumnsDefinition(cols =>
        {
            cols.ConstantColumn(22);
            cols.ConstantColumn(70);
            cols.RelativeColumn(4);
            cols.ConstantColumn(70);
            cols.ConstantColumn(30);
            cols.ConstantColumn(70);
            cols.RelativeColumn(2);
        });
        table.Header(h =>
        {
            for (var i = 0; i < Columns.Length; i++)
            {
                var cell = HeaderCell(h.Cell());
                (i == 3 ? cell.AlignRight() : cell).Text(Columns[i]).SemiBold();
            }
        });
        foreach (var l in _model.Lines)
        {
            BodyCell(table.Cell()).Text(l.Sequence.ToString(PdfTheme.PtBr));
            BodyCell(table.Cell()).Text(l.ItemCode);
            BodyCell(table.Cell()).Text(l.ItemName);
            BodyCell(table.Cell()).AlignRight().Column(q =>
            {
                q.Item().AlignRight().Text(PdfTheme.Quantity(l.Quantity));
                if (l.SupplierQuantity is { } supplier)
                {
                    q.Item().AlignRight().Text(supplier).FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted);
                }
            });
            BodyCell(table.Cell()).PaddingLeft(4).Text(l.Unit);
            BodyCell(table.Cell()).Text(string.IsNullOrWhiteSpace(l.Lot) ? "—" : l.Lot);
            BodyCell(table.Cell()).Text(l.Location);
        }
    });
}
