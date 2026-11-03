using BemAqui.BuildingBlocks.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Inventory.Application.Documents;

/// <summary>
/// Ajuste de estoque no modelo único (EXP-04 CA 11): modo, local, motivo e quem lançou; por item, o saldo no envio, o contado (ou a
/// diferença), a diferença e o valor pelo custo médio, como a tela; totais de sobras, faltas e valor líquido.
/// </summary>
public sealed class StockAdjustmentPdfDocument : PdfDocumentFrame
{
    private readonly StockAdjustmentPdfModel _model;

    public StockAdjustmentPdfDocument(PdfFrame frame, StockAdjustmentPdfModel model)
        : base(frame, new PdfDocumentHeader("Ajuste de estoque", (model ?? throw new ArgumentNullException(nameof(model))).Number, model.Date, model.Mark, Notice: model.Notice)) =>
        _model = model;

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var m = _model;
        col.Spacing(10);
        col.Item().Element(c => Fields(c, 3,
            ("Situação", m.StatusLabel),
            ("Modo", m.ModeLabel),
            ("Local", m.Location),
            ("Motivo", m.Reason),
            ("Lançado por", m.RequestedByName is { } by ? $"{by} em {PdfTheme.DateTime(m.RequestedAt)}" : PdfTheme.DateTime(m.RequestedAt)),
            ("Decidido por", m.DecidedByName is { } d ? d + (m.DecidedAt is { } at ? " em " + PdfTheme.DateTime(at) : string.Empty) : null),
            ("Origem", m.OriginNumber ?? "Lançamento manual")));
        col.Item().Element(Lines);
        col.Item().AlignRight().Text(t =>
        {
            t.Span("Sobras: ").FontColor(PdfTheme.Muted);
            t.Span(PdfTheme.Money(m.Surplus)).SemiBold();
            t.Span("   Faltas: ").FontColor(PdfTheme.Muted);
            t.Span(PdfTheme.Money(m.Shortage)).SemiBold();
            t.Span("   Valor líquido: ").FontColor(PdfTheme.Muted);
            t.Span(PdfTheme.Money(m.Net)).Bold();
        });
        col.Item().PaddingTop(24).Row(row =>
        {
            row.RelativeItem().Element(e => Signature(e, "Lançado por"));
            row.ConstantItem(24);
            row.RelativeItem().Element(e => Signature(e, "Aprovado por"));
        });
    });

    private void Lines(IContainer c) => c.Table(table =>
    {
        var columns = new[] { "Código", "Descrição", "Un.", "Lote", "Saldo no envio", _model.Counted ? "Contado" : "Informado", "Diferença", "Valor" };
        table.ColumnsDefinition(cols =>
        {
            cols.ConstantColumn(66);
            cols.RelativeColumn(4);
            cols.ConstantColumn(28);
            cols.ConstantColumn(52);
            cols.ConstantColumn(56);
            cols.ConstantColumn(52);
            cols.ConstantColumn(52);
            cols.ConstantColumn(64);
        });
        table.Header(h =>
        {
            for (var i = 0; i < columns.Length; i++)
            {
                var cell = HeaderCell(h.Cell());
                (i >= 4 ? cell.AlignRight() : cell).Text(columns[i]).SemiBold();
            }
        });
        foreach (var l in _model.Lines)
        {
            BodyCell(table.Cell()).Text(l.ItemCode);
            BodyCell(table.Cell()).Text(l.ItemName);
            BodyCell(table.Cell()).Text(l.Unit);
            BodyCell(table.Cell()).Text(l.Lot ?? "—");
            BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Quantity(l.SystemAtRequest));
            BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Quantity(l.Entered));
            BodyCell(table.Cell()).AlignRight().Text((l.Delta > 0 ? "+" : string.Empty) + PdfTheme.Quantity(l.Delta));
            BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Money(l.Value));
        }
    });
}
