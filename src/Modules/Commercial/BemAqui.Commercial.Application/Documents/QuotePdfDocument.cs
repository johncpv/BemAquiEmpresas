using System.Globalization;
using BemAqui.BuildingBlocks.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BemAqui.Commercial.Application.Documents;

/// <summary>Layout do orçamento (ADR 0022): A4, Archivo, números em pt-BR com dígitos tabulares e cabeçalho da tabela repetido na quebra.</summary>
public sealed class QuotePdfDocument(QuotePdfModel model) : IDocument
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly string[] Columns = ["#", "Descrição", "Qtd.", "Un.", "Preço unit.", "Total"];

    private static string Money(decimal v) => v.ToString("C2", PtBr);

    private static string Qty(decimal v) => v.ToString("0.####", PtBr);

    public DocumentMetadata GetMetadata() => new() { Title = $"Orçamento {model.Number} rev. {model.Revision}", Author = model.Company.Name, Creator = "BemAqui Empresas" };

    public void Compose(IDocumentContainer container) => container.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(32);
        page.DefaultTextStyle(t => t.FontFamily(QuestPdfRenderer.FontFamily).FontSize(9).FontColor("#14202b").EnableFontFeature(FontFeatures.TabularFigures));
        page.Header().Element(Header);
        page.Content().PaddingVertical(12).Element(Content);
        page.Footer().AlignCenter().Text(t =>
        {
            t.Span(model.ValidUntil is { } v ? $"Orçamento válido até {v:dd/MM/yyyy}. Sem efeito de contrato." : "Sem efeito de contrato.").FontSize(8);
            t.Span("   Página ").FontSize(8);
            t.CurrentPageNumber().FontSize(8);
            t.Span(" de ").FontSize(8);
            t.TotalPages().FontSize(8);
        });
    });

    private void Header(IContainer c) => c.Row(row =>
    {
        if (model.Company.Logo is { Length: > 0 } logo)
        {
            row.ConstantItem(110).Height(48).Image(logo).FitArea();
        }
        else
        {
            row.ConstantItem(160).Text(model.Company.Name).FontSize(14).SemiBold();
        }

        row.RelativeItem().AlignRight().Column(col =>
        {
            col.Item().Text(model.Company.LegalName).SemiBold();
            if (model.Company.Document is { } doc)
            {
                col.Item().Text("CNPJ " + doc);
            }

            if (model.Company.Address is { } address)
            {
                col.Item().Text(address);
            }

            if (model.Company.Contact is { } contact)
            {
                col.Item().Text(contact);
            }
        });
    });

    private void Content(IContainer c) => c.Column(col =>
    {
        col.Spacing(10);
        col.Item().Row(r =>
        {
            r.RelativeItem().Text($"Orçamento {model.Number} rev. {model.Revision}").FontSize(14).Bold();
            r.ConstantItem(120).AlignRight().Text($"Data: {model.IssueDate:dd/MM/yyyy}");
        });
        col.Item().Background("#eef2f6").Padding(8).Column(cust =>
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
            t.Line(model.ValidUntil is { } v ? $"Validade: {v:dd/MM/yyyy}" : "Validade: a combinar");
            t.Line(model.DeliveryDays is { } d ? $"Prazo de entrega: {d} dias corridos após a aprovação" : "Prazo de entrega: a combinar");
        });
        if (!string.IsNullOrWhiteSpace(model.Notes))
        {
            col.Item().Text("Observações").SemiBold();
            col.Item().Text(model.Notes);
        }
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
                h.Cell().BorderBottom(1).BorderColor("#9aa7b4").PaddingVertical(4).Text(title).SemiBold();
            }
        });
        foreach (var i in model.Items)
        {
            table.Cell().PaddingVertical(3).Text(i.Sequence.ToString(PtBr));
            table.Cell().PaddingVertical(3).Text(i.Description);
            table.Cell().PaddingVertical(3).AlignRight().Text(Qty(i.Quantity));
            table.Cell().PaddingVertical(3).PaddingLeft(4).Text(i.Unit ?? "un");
            table.Cell().PaddingVertical(3).AlignRight().Text(Money(i.UnitPrice));
            table.Cell().PaddingVertical(3).AlignRight().Text(Money(i.Total));
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
            foreach (var m in model.Milestones)
            {
                table.Cell().PaddingVertical(2).Text(m.Description);
                table.Cell().PaddingVertical(2).AlignRight().Text(m.Percent.ToString("0.##", PtBr) + "%");
                table.Cell().PaddingVertical(2).AlignRight().Text(Money(m.Amount));
                table.Cell().PaddingVertical(2).PaddingLeft(8).Text(m.When);
            }
        });
    });
}
