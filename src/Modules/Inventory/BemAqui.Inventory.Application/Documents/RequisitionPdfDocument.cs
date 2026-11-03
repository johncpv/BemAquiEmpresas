using BemAqui.BuildingBlocks.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Inventory.Application.Documents;

/// <summary>
/// Folha de separação da requisição de material no modelo único (EXP-04 CA 10): OS, etapa, projeto e quem pediu; os itens com local, lote e a
/// reserva, a coluna "Separado" em branco e as assinaturas "Separado por" e "Recebido por". Sem custo.
/// </summary>
public sealed class RequisitionPdfDocument : PdfDocumentFrame
{
    private static readonly string[] Columns = ["Código", "Descrição", "Qtd.", "Un.", "Local", "Lote", "Reserva", "Separado"];

    private readonly RequisitionPdfModel _model;

    public RequisitionPdfDocument(PdfFrame frame, RequisitionPdfModel model)
        : base(frame, new PdfDocumentHeader((model ?? throw new ArgumentNullException(nameof(model))).KindLabel == "Requisição" ? "Folha de separação" : model.KindLabel, model.Number,
            model.Date, model.Mark, Notice: model.Notice)) => _model = model;

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var m = _model;
        col.Spacing(10);
        col.Item().Element(c => Fields(c, 3,
            ("Situação", m.StatusLabel),
            ("OS", m.OrderNumber),
            ("Etapa", m.StepName),
            ("Projeto", m.ProjectNumber),
            ("Pedida por", m.RequestedByName),
            ("Pedida em", PdfTheme.DateTime(m.RequestedAt))));
        col.Item().Element(Lines);
        col.Item().PaddingTop(24).Row(row =>
        {
            row.RelativeItem().Element(e => Signature(e, "Separado por (nome, data e assinatura)"));
            row.ConstantItem(24);
            row.RelativeItem().Element(e => Signature(e, "Recebido por (nome, data e assinatura)"));
        });
    });

    private void Lines(IContainer c) => c.Table(table =>
    {
        table.ColumnsDefinition(cols =>
        {
            cols.ConstantColumn(70);
            cols.RelativeColumn(4);
            cols.ConstantColumn(50);
            cols.ConstantColumn(28);
            cols.ConstantColumn(56);
            cols.ConstantColumn(56);
            cols.ConstantColumn(50);
            cols.ConstantColumn(56);
        });
        table.Header(h =>
        {
            for (var i = 0; i < Columns.Length; i++)
            {
                var cell = HeaderCell(h.Cell());
                (i == 2 ? cell.AlignRight() : cell).Text(Columns[i]).SemiBold();
            }
        });
        foreach (var l in _model.Lines)
        {
            BodyCell(table.Cell()).Text(l.ItemCode);
            BodyCell(table.Cell()).Text(l.ItemName);
            BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Quantity(l.Quantity));
            BodyCell(table.Cell()).PaddingLeft(4).Text(l.Unit);
            BodyCell(table.Cell()).Text(l.Location);
            BodyCell(table.Cell()).Text(l.Lot ?? "—");
            BodyCell(table.Cell()).Text(l.Reserved);
            // Campo em branco para quem separa marcar à mão.
            table.Cell().PaddingVertical(3).PaddingHorizontal(3).AlignBottom().Height(16).BorderBottom(0.75f).BorderColor(PdfTheme.Line);
        }
    });
}
