using BemAqui.BuildingBlocks.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Inventory.Application.Documents;

/// <summary>
/// Resultado do inventário fechado no modelo único (EXP-04 CA 13): por linha, o saldo da foto, o contado, a diferença, o valor e quem contou;
/// os totais de sobras, faltas e líquido, a escolha para os não contados e o ajuste gerado com a situação. Em paisagem.
/// </summary>
public sealed class InventoryCountResultPdfDocument : PdfDocumentFrame
{
    private static readonly string[] Columns = ["Código", "Descrição", "Un.", "Lote", "Saldo da foto", "Contado", "Diferença", "Valor", "Contado por"];

    private readonly InventoryCountResultModel _model;

    public InventoryCountResultPdfDocument(PdfFrame frame, InventoryCountResultModel model)
        : base(frame, new PdfDocumentHeader("Resultado do inventário", (model ?? throw new ArgumentNullException(nameof(model))).Number, model.Date, model.Mark, true, model.Notice)) =>
        _model = model;

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var m = _model;
        col.Spacing(10);
        col.Item().Element(c => Fields(c, 3,
            ("Local", m.Location),
            ("Escopo", m.Scope),
            ("Foto do saldo", PdfTheme.DateTime(m.SnapshotAt)),
            ("Fechado por", m.ClosedByName is { } by ? by + (m.ClosedAt is { } at ? " em " + PdfTheme.DateTime(at) : string.Empty) : null),
            ("Não contados", m.Policy),
            ("Ajuste gerado", m.Adjustment)));
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
    });

    private void Lines(IContainer c) => c.Table(table =>
    {
        table.ColumnsDefinition(cols =>
        {
            cols.ConstantColumn(70);
            cols.RelativeColumn(4);
            cols.ConstantColumn(28);
            cols.ConstantColumn(60);
            cols.ConstantColumn(62);
            cols.ConstantColumn(62);
            cols.ConstantColumn(62);
            cols.ConstantColumn(72);
            cols.RelativeColumn(2);
        });
        table.Header(h =>
        {
            for (var i = 0; i < Columns.Length; i++)
            {
                var cell = HeaderCell(h.Cell());
                (i is >= 4 and <= 7 ? cell.AlignRight() : cell).Text(Columns[i]).SemiBold();
            }
        });
        foreach (var l in _model.Lines)
        {
            BodyCell(table.Cell()).Text(l.ItemCode);
            BodyCell(table.Cell()).Text(l.ItemName);
            BodyCell(table.Cell()).Text(l.Unit);
            BodyCell(table.Cell()).Text(l.Lot ?? "—");
            BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Quantity(l.SystemQty));
            BodyCell(table.Cell()).AlignRight().Text(l.CountedQty is { } counted ? PdfTheme.Quantity(counted) : "Não contado");
            BodyCell(table.Cell()).AlignRight().Text((l.Difference > 0 ? "+" : string.Empty) + PdfTheme.Quantity(l.Difference));
            BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Money(l.Value));
            BodyCell(table.Cell()).Text(l.CountedBy ?? "—");
        }
    });
}
