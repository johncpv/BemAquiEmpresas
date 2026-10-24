using System.Globalization;
using BemAqui.BuildingBlocks.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BemAqui.Purchasing.Application.Documents;

/// <summary>
/// Layout do pedido de compra (ADR 0022, CMP-03b CA 1): A4, Archivo, números em pt-BR com dígitos tabulares e o cabeçalho da tabela
/// repetido na quebra. O único arquivo do Purchasing que conhece o QuestPDF.
/// </summary>
public sealed class PurchaseOrderPdfDocument(PurchaseOrderPdfModel model) : IDocument
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly string[] Columns = ["#", "Código", "Descrição", "Qtd.", "Un.", "Preço unit.", "Total"];

    private static string Money(decimal v) => v.ToString("C2", PtBr);

    /// <summary>Preço unitário com 2 a 4 casas (o preço guardado tem até 6; a unidade do fornecedor pode precisar de mais de 2).</summary>
    private static string Price(decimal v) => "R$ " + Math.Round(v, 4, MidpointRounding.AwayFromZero).ToString("#,##0.00##", PtBr);

    private static string Qty(decimal v) => v.ToString("#,##0.####", PtBr);

    public DocumentMetadata GetMetadata() => new() { Title = $"Pedido de compra {model.Number}", Author = model.Company.Name, Creator = "BemAqui Empresas" };

    public void Compose(IDocumentContainer container) => container.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(32);
        page.DefaultTextStyle(t => t.FontFamily(QuestPdfRenderer.FontFamily).FontSize(9).FontColor("#14202b").EnableFontFeature(FontFeatures.TabularFigures));
        page.Header().Element(Header);
        page.Content().PaddingVertical(12).Element(Content);
        page.Footer().AlignCenter().Text(t =>
        {
            t.Span($"Pedido de compra {model.Number}").FontSize(8);
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
            r.RelativeItem().Text($"Pedido de compra {model.Number}").FontSize(14).Bold();
            r.ConstantItem(120).AlignRight().Text($"Data: {model.IssueDate:dd/MM/yyyy}");
        });
        col.Item().Background("#eef2f6").Padding(8).Column(info =>
        {
            info.Item().Text("Fornecedor: " + model.Supplier.Name).SemiBold();
            if (!string.Equals(model.Supplier.LegalName, model.Supplier.Name, StringComparison.Ordinal))
            {
                info.Item().Text("Razão social: " + model.Supplier.LegalName);
            }

            if (model.Supplier.Document is { } doc)
            {
                info.Item().Text("CNPJ/CPF: " + doc);
            }

            info.Item().Text("Condição de pagamento: " + (model.PaymentTerm ?? "a combinar"));
            info.Item().Text($"Previsão de entrega: {model.ExpectedDate:dd/MM/yyyy}");
            info.Item().Text("Local de entrega: " + (model.DeliveryAddress ?? "a combinar"));
        });
        col.Item().Element(Lines);
        col.Item().AlignRight().Width(220).Element(Totals);
        if (!string.IsNullOrWhiteSpace(model.Notes))
        {
            col.Item().Text("Observações").SemiBold();
            col.Item().Text(model.Notes);
        }
    });

    private void Lines(IContainer c) => c.Table(table =>
    {
        table.ColumnsDefinition(cols =>
        {
            cols.ConstantColumn(22);
            cols.ConstantColumn(70);
            cols.RelativeColumn(5);
            cols.ConstantColumn(52);
            cols.ConstantColumn(30);
            cols.ConstantColumn(72);
            cols.ConstantColumn(76);
        });
        table.Header(h =>
        {
            foreach (var title in Columns)
            {
                h.Cell().BorderBottom(1).BorderColor("#9aa7b4").PaddingVertical(4).Text(title).SemiBold();
            }
        });
        foreach (var l in model.Lines)
        {
            table.Cell().PaddingVertical(3).Text(l.Sequence.ToString(PtBr));
            table.Cell().PaddingVertical(3).Column(code =>
            {
                code.Item().Text(l.ItemCode);
                if (l.SupplierCode is { } supplierCode)
                {
                    code.Item().Text("Forn.: " + supplierCode).FontSize(8);
                }
            });
            table.Cell().PaddingVertical(3).Text(l.Description);
            table.Cell().PaddingVertical(3).AlignRight().Text(Qty(l.Quantity));
            table.Cell().PaddingVertical(3).PaddingLeft(4).Text(l.Unit);
            table.Cell().PaddingVertical(3).AlignRight().Text(Price(l.UnitPrice));
            table.Cell().PaddingVertical(3).AlignRight().Text(Money(l.Total));
        }
    });

    private void Totals(IContainer c) => c.Table(table =>
    {
        table.ColumnsDefinition(cols =>
        {
            cols.RelativeColumn();
            cols.ConstantColumn(96);
        });
        void Row(string label, string value, bool strong = false)
        {
            var left = table.Cell().PaddingVertical(2).Text(label);
            var right = table.Cell().PaddingVertical(2).AlignRight().Text(value);
            if (strong)
            {
                left.FontSize(12).Bold();
                right.FontSize(12).Bold();
            }
        }

        Row("Subtotal", Money(model.Subtotal));
        Row("Frete", Money(model.Freight));
        Row($"IPI ({model.IpiPct.ToString("0.##", PtBr)}%)", Money(model.IpiAmount));
        Row("Total", Money(model.Total), strong: true);
    });
}
