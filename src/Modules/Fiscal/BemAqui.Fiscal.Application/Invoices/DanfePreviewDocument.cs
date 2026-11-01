using System.Globalization;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Fiscal.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BemAqui.Fiscal.Application.Invoices;

/// <summary>Dados da prévia: emitente, a chave e o número que a nota teria agora (podem mudar), os avisos do Emissor e a nota.</summary>
public sealed record DanfePreviewModel(
    string IssuerName, string? IssuerDocument, string AccessKey, long Number, int Series, IReadOnlyList<string> Warnings, OutgoingInvoice Invoice, IReadOnlyList<OutgoingInvoiceItem> Items,
    string? Complementary);

/// <summary>
/// "Pré-visualizar" (FIS-03a CA 5): DANFE simplificado em PDF montado pelo BemAqui com o <c>IPdfRenderer</c> (reserva da ADR 0027 para a
/// DANFE do Emissor, que na prévia só existe em HTML e nunca é repassada), com a faixa "Prévia, sem valor fiscal" em cada página. Os
/// textos livres vão como texto do PDF (o QuestPDF não interpreta marcação): nada é montado como HTML (CA 4).
/// </summary>
public sealed class DanfePreviewDocument(DanfePreviewModel model) : IDocument
{
    public const string Band = "Prévia, sem valor fiscal";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly string[] Columns = ["Código", "Descrição", "NCM", "CFOP", "Qtde.", "Valor unit.", "Total"];

    public DocumentMetadata GetMetadata() => new() { Title = "Prévia da NF-e — " + Band, Author = model.IssuerName, Creator = "BemAqui Empresas" };

    public void Compose(IDocumentContainer container) => container.Page(page =>
    {
        var invoice = model.Invoice;
        page.Size(PageSizes.A4);
        page.Margin(24);
        page.DefaultTextStyle(t => t.FontFamily(QuestPdfRenderer.FontFamily).FontSize(8f).FontColor("#14202b").EnableFontFeature(FontFeatures.TabularFigures));
        page.Header().Column(col =>
        {
            col.Item().Background("#fde7c3").Padding(6).AlignCenter().Text(Band.ToUpperInvariant()).FontSize(12).Bold().FontColor("#7a4b00");
            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem(3).Column(c =>
                {
                    c.Item().Text(model.IssuerName).FontSize(11).SemiBold();
                    if (model.IssuerDocument is { } d)
                    {
                        c.Item().Text("CNPJ " + d);
                    }
                });
                row.RelativeItem(2).Column(c =>
                {
                    c.Item().Text("DANFE — Documento Auxiliar da NF-e").SemiBold();
                    c.Item().Text($"Série {model.Series.ToString(CultureInfo.InvariantCulture)} · Nº {model.Number.ToString(CultureInfo.InvariantCulture)} (na prévia)");
                    c.Item().Text("Chave: " + model.AccessKey).FontSize(7);
                });
            });
        });
        page.Content().PaddingVertical(8).Column(col =>
        {
            col.Spacing(6);
            col.Item().Text("Natureza da operação: " + (invoice.NatureOfOperation ?? "—"));
            col.Item().Background("#eef1f4").Padding(5).Column(c =>
            {
                c.Item().Text("Destinatário").SemiBold();
                c.Item().Text(invoice.RecipientName ?? "—");
                c.Item().Text($"{invoice.RecipientDocument ?? "—"} · IE {invoice.RecipientStateRegistration ?? "—"}");
                c.Item().Text($"{invoice.RecipientStreet}, {invoice.RecipientNumber} {invoice.RecipientComplement} — {invoice.RecipientDistrict} — {invoice.RecipientCityName}/{invoice.RecipientState} — CEP {invoice.RecipientPostalCode}");
            });
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(1.2f);
                    c.RelativeColumn(4f);
                    c.RelativeColumn(1.1f);
                    c.RelativeColumn(0.8f);
                    c.RelativeColumn(1f);
                    c.RelativeColumn(1.4f);
                    c.RelativeColumn(1.4f);
                });
                foreach (var title in Columns)
                {
                    table.Cell().Background("#e6ebf0").Padding(3).Text(title).SemiBold();
                }

                foreach (var item in model.Items.OrderBy(i => i.Sequence))
                {
                    table.Cell().BorderBottom(0.5f).BorderColor("#c9d1d9").Padding(3).Text(item.Code);
                    table.Cell().BorderBottom(0.5f).BorderColor("#c9d1d9").Padding(3).Column(c =>
                    {
                        c.Item().Text(item.Description);
                        if (item.AdditionalInfo is { } info)
                        {
                            c.Item().Text(info).FontSize(6.5f).FontColor("#4b5866");
                        }
                    });
                    table.Cell().BorderBottom(0.5f).BorderColor("#c9d1d9").Padding(3).Text(item.Ncm ?? "—");
                    table.Cell().BorderBottom(0.5f).BorderColor("#c9d1d9").Padding(3).Text(item.Cfop ?? "—");
                    table.Cell().BorderBottom(0.5f).BorderColor("#c9d1d9").Padding(3).AlignRight().Text(item.Quantity.ToString("0.####", PtBr) + " " + item.Unit);
                    table.Cell().BorderBottom(0.5f).BorderColor("#c9d1d9").Padding(3).AlignRight().Text(item.UnitPrice.ToString("N2", PtBr));
                    table.Cell().BorderBottom(0.5f).BorderColor("#c9d1d9").Padding(3).AlignRight().Text(item.GrossAmount.ToString("N2", PtBr));
                }
            });
            col.Item().AlignRight().Column(c =>
            {
                foreach (var tax in invoice.Totals.Taxes.Where(t => t.Code != TaxCodes.Difal))
                {
                    c.Item().Text($"{tax.Code}: base {tax.Base.ToString("N2", PtBr)} · valor {tax.Amount.ToString("N2", PtBr)}");
                }

                c.Item().Text("Valor total da nota: " + invoice.TotalAmount.ToString("C", PtBr)).FontSize(10).SemiBold();
            });
            if (model.Complementary is { } info)
            {
                col.Item().Text("Informações complementares").SemiBold();
                col.Item().Text(info);
            }

            foreach (var warning in model.Warnings)
            {
                col.Item().Text("Aviso do Emissor Fiscal: " + warning).FontColor("#7a4b00");
            }
        });
        page.Footer().AlignCenter().Text(t =>
        {
            t.Span(Band + ". O número e a chave podem mudar na emissão.   Página ").FontSize(7f);
            t.CurrentPageNumber().FontSize(7f);
            t.Span(" de ").FontSize(7f);
            t.TotalPages().FontSize(7f);
        });
    });
}
