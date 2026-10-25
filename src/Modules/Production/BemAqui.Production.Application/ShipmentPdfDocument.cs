using System.Globalization;
using BemAqui.BuildingBlocks.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BemAqui.Production.Application;

/// <summary>
/// O que o PDF do romaneio mostra (PRD-06b CA 3): empresa, número, data, cliente, endereço de entrega, transporte, itens com quantidade e
/// séries, volumes e peso e as assinaturas. Não existe campo de valor nem de custo aqui: o romaneio não é documento fiscal.
/// </summary>
public sealed record ShipmentPdfModel(
    ShipmentPdfCompany Company, string Number, DateOnly Date, string ProjectNumber, string CustomerName, string? CustomerDocument, string DeliveryAddress, string? CarrierName,
    string? VehiclePlate, string? DriverName, int? Volumes, decimal? GrossWeight, IReadOnlyList<ShipmentPdfItem> Items);

public sealed record ShipmentPdfCompany(string Name, string LegalName, string? Document, string? Address, string? Contact, byte[]? Logo);

public sealed record ShipmentPdfItem(int Sequence, string Code, string Description, decimal Quantity, string Unit, IReadOnlyList<string> SerialNumbers, string? LotNumber);

/// <summary>Layout do romaneio (ADR 0022): A4, Archivo embutida, dígitos tabulares, cabeçalho da tabela repetido na quebra.</summary>
public sealed class ShipmentPdfDocument(ShipmentPdfModel model) : IDocument
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly string[] Columns = ["#", "Código", "Descrição e séries", "Qtd.", "Un."];

    public DocumentMetadata GetMetadata() => new() { Title = $"Romaneio {model.Number}", Author = model.Company.Name, Creator = "BemAqui Empresas" };

    public void Compose(IDocumentContainer container) => container.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(32);
        page.DefaultTextStyle(t => t.FontFamily(QuestPdfRenderer.FontFamily).FontSize(9).FontColor("#14202b").EnableFontFeature(FontFeatures.TabularFigures));
        page.Header().Element(Header);
        page.Content().PaddingVertical(12).Element(Content);
        page.Footer().AlignCenter().Text(t =>
        {
            t.Span("Romaneio de expedição: não é documento fiscal.").FontSize(8);
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
            r.RelativeItem().Text($"Romaneio {model.Number}").FontSize(14).Bold();
            r.ConstantItem(140).AlignRight().Text($"Data: {model.Date:dd/MM/yyyy}");
        });
        col.Item().Background("#eef2f6").Padding(8).Column(customer =>
        {
            customer.Item().Text("Cliente: " + model.CustomerName).SemiBold();
            if (model.CustomerDocument is { } doc)
            {
                customer.Item().Text("Documento: " + doc);
            }

            customer.Item().Text("Projeto: " + model.ProjectNumber);
            customer.Item().Text("Entrega: " + model.DeliveryAddress);
        });
        col.Item().Column(transport =>
        {
            transport.Item().Text("Transporte").SemiBold();
            transport.Item().Text("Transportadora: " + (model.CarrierName ?? "—"));
            transport.Item().Text($"Placa: {model.VehiclePlate ?? "—"}    Motorista: {model.DriverName ?? "—"}");
            transport.Item().Text($"Volumes: {(model.Volumes is { } v ? v.ToString(PtBr) : "—")}    Peso bruto: {(model.GrossWeight is { } w ? w.ToString("0.####", PtBr) + " kg" : "—")}");
        });
        col.Item().Element(Items);
        col.Item().PaddingTop(28).Row(row =>
        {
            row.RelativeItem().Element(e => Signature(e, "Expedido por (nome e assinatura)"));
            row.ConstantItem(24);
            row.RelativeItem().Element(e => Signature(e, "Recebido por (nome, documento e assinatura)"));
        });
        col.Item().Text("Data do recebimento: ____/____/________");
    });

    private static void Signature(IContainer c, string label) => c.Column(col =>
    {
        col.Item().Height(28);
        col.Item().BorderTop(1).BorderColor("#9aa7b4").PaddingTop(4).Text(label);
    });

    private void Items(IContainer c) => c.Table(table =>
    {
        table.ColumnsDefinition(cols =>
        {
            cols.ConstantColumn(24);
            cols.ConstantColumn(90);
            cols.RelativeColumn(5);
            cols.ConstantColumn(50);
            cols.ConstantColumn(34);
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
            table.Cell().PaddingVertical(3).Text(i.Code);
            table.Cell().PaddingVertical(3).Column(d =>
            {
                d.Item().Text(i.Description);
                if (i.SerialNumbers.Count > 0)
                {
                    d.Item().Text("Séries: " + string.Join(", ", i.SerialNumbers)).FontSize(8);
                }

                if (i.LotNumber is { } lot)
                {
                    d.Item().Text("Lote: " + lot).FontSize(8);
                }
            });
            table.Cell().PaddingVertical(3).AlignRight().Text(ShipmentRules.Quantity(i.Quantity));
            table.Cell().PaddingVertical(3).PaddingLeft(4).Text(i.Unit);
        }
    });
}
