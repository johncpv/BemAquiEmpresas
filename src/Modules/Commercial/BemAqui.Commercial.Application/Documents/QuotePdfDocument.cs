using System.Globalization;
using BemAqui.BuildingBlocks.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Commercial.Application.Documents;

/// <summary>
/// Layout do orçamento no modelo único (ADR 0045 §1, EXP-05 CA 19): o mesmo conteúdo da COM-03 (cliente, itens, total, condições de pagamento,
/// validade, prazo e observações), com o cabeçalho, a faixa de título ("Orçamento {número} rev. {n}") e o rodapé do frame. As faixas e as marcas
/// da EXP-03 (vencido, recusado) vão na faixa do frame; a validade e "Sem efeito de contrato" fecham o corpo.
/// </summary>
public sealed class QuotePdfDocument : PdfDocumentFrame
{
    private static readonly CultureInfo PtBr = PdfTheme.PtBr;

    private static readonly string[] Columns = ["#", "Descrição", "Qtd.", "Un.", "Preço unit.", "Total"];

    private readonly QuotePdfModel _model;

    public QuotePdfDocument(PdfFrame frame, QuotePdfModel model)
        : base(frame, Header(model)) => _model = model;

    private static PdfDocumentHeader Header(QuotePdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new PdfDocumentHeader("Orçamento", $"{model.Number} rev. {model.Revision}", model.IssueDate, model.Mark, Notice: model.Notice);
    }

    private static string Money(decimal v) => v.ToString("C2", PtBr);

    private static string Qty(decimal v) => v.ToString("0.####", PtBr);

    /// <summary>A linha que fechava cada página do layout antigo: a validade e "Sem efeito de contrato".</summary>
    public static string ClosingText(DateOnly? validUntil) =>
        validUntil is { } v ? $"Orçamento válido até {PdfTheme.Date(v)}. Sem efeito de contrato." : "Sem efeito de contrato.";

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var model = _model;
        col.Spacing(10);
        col.Item().Background(PdfTheme.Surface).Padding(8).Column(cust =>
        {
            cust.Item().Text("Cliente: " + model.Customer.Name).SemiBold();
            if (model.Customer.Document is { } doc)
            {
                cust.Item().Text("Documento: " + doc);
            }

            if (model.Customer.SiteAddress is { } site)
            {
                cust.Item().Text("Obra: " + site);
            }

            if (model.Customer.Contact is { } contact)
            {
                cust.Item().Text("Contato: " + contact);
            }
        });
        col.Item().Element(Items);
        col.Item().AlignRight().Text("Total: " + Money(model.Total)).FontSize(12).Bold();
        if (model.Milestones.Count > 0)
        {
            col.Item().Element(Milestones);
        }

        col.Item().Text(t =>
        {
            t.Line(model.ValidUntil is { } v ? $"Validade: {PdfTheme.Date(v)}" : "Validade: a combinar");
            t.Line(model.DeliveryDays is { } d ? $"Prazo de entrega: {d} dias corridos após a aprovação" : "Prazo de entrega: a combinar");
        });
        if (!string.IsNullOrWhiteSpace(model.Notes))
        {
            col.Item().Text("Observações").SemiBold();
            col.Item().Text(model.Notes);
        }

        col.Item().PaddingTop(6).Text(ClosingText(model.ValidUntil)).FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted);
    });

    private void Items(IContainer c) => c.Table(table =>
    {
        table.ColumnsDefinition(cols =>
        {
            cols.ConstantColumn(24);
            cols.RelativeColumn(5);
            cols.ConstantColumn(50);
            cols.ConstantColumn(34);
            cols.ConstantColumn(80);
            cols.ConstantColumn(84);
        });
        table.Header(h =>
        {
            foreach (var title in Columns)
            {
                HeaderCell(h.Cell()).Text(title).SemiBold();
            }
        });
        foreach (var i in _model.Items)
        {
            BodyCell(table.Cell()).Text(i.Sequence.ToString(PtBr));
            BodyCell(table.Cell()).Text(i.Description);
            BodyCell(table.Cell()).AlignRight().Text(Qty(i.Quantity));
            BodyCell(table.Cell()).PaddingLeft(4).Text(i.Unit ?? "un");
            BodyCell(table.Cell()).AlignRight().Text(Money(i.UnitPrice));
            BodyCell(table.Cell()).AlignRight().Text(Money(i.Total));
        }
    });

    private void Milestones(IContainer c) => c.Column(col =>
    {
        col.Item().Text("Condições de pagamento").SemiBold();
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(4);
                cols.ConstantColumn(60);
                cols.ConstantColumn(90);
                cols.RelativeColumn(4);
            });
            foreach (var m in _model.Milestones)
            {
                table.Cell().PaddingVertical(2).Text(m.Description);
                table.Cell().PaddingVertical(2).AlignRight().Text(m.Percent.ToString("0.##", PtBr) + "%");
                table.Cell().PaddingVertical(2).AlignRight().Text(Money(m.Amount));
                table.Cell().PaddingVertical(2).PaddingLeft(8).Text(m.When);
            }
        });
    });
}
