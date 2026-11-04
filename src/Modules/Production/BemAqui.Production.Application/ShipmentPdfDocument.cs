using System.Globalization;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Production.Contracts;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Production.Application;

/// <summary>
/// O que o PDF do romaneio mostra (PRD-06b CA 3): número, data, cliente, endereço de entrega, transporte, itens com quantidade e séries,
/// volumes e peso e as assinaturas; a empresa vem do frame. Não existe campo de valor nem de custo aqui: o romaneio não é documento fiscal.
/// A situação decide a marca (EXP-05 CA 20).
/// </summary>
public sealed record ShipmentPdfModel(
    string Number, DateOnly Date, string ProjectNumber, string CustomerName, string? CustomerDocument, string DeliveryAddress, string? CarrierName,
    string? VehiclePlate, string? DriverName, int? Volumes, decimal? GrossWeight, IReadOnlyList<ShipmentPdfItem> Items, ShipmentState Status = ShipmentState.Shipped);

public sealed record ShipmentPdfItem(int Sequence, string Code, string Description, decimal Quantity, string Unit, IReadOnlyList<string> SerialNumbers, string? LotNumber);

/// <summary>
/// Layout do romaneio no modelo único (ADR 0045 §1, EXP-05 CA 19 e 20): o mesmo conteúdo da PRD-06b, com o cabeçalho, a faixa de título e o
/// rodapé do frame; "não é documento fiscal" fecha o corpo. Rascunho e aguardando o estoque saem com "Rascunho"; cancelado e estornado, com
/// "Cancelado".
/// </summary>
public sealed class ShipmentPdfDocument : PdfDocumentFrame
{
    public const string NotFiscal = "Romaneio de expedição: não é documento fiscal.";

    private static readonly CultureInfo PtBr = PdfTheme.PtBr;

    private static readonly string[] Columns = ["#", "Código", "Descrição e séries", "Qtd.", "Un."];

    private readonly ShipmentPdfModel _model;

    public ShipmentPdfDocument(PdfFrame frame, ShipmentPdfModel model)
        : base(frame, Header(model)) => _model = model;

    private static PdfDocumentHeader Header(ShipmentPdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new PdfDocumentHeader("Romaneio", model.Number, model.Date, Mark(model.Status), Notice: model.Status == ShipmentState.Reversed ? "Romaneio estornado" : null);
    }

    /// <summary>Marca do romaneio pela situação (EXP-05 CA 20).</summary>
    public static PdfMark Mark(ShipmentState status) => status switch
    {
        ShipmentState.Draft or ShipmentState.AwaitingStockApproval => PdfMark.Draft,
        ShipmentState.Cancelled or ShipmentState.Reversed => PdfMark.Cancelled,
        _ => PdfMark.None,
    };

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var model = _model;
        col.Spacing(10);
        col.Item().Background(PdfTheme.Surface).Padding(8).Column(customer =>
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
        col.Item().PaddingTop(6).Text(NotFiscal).FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted);
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
                HeaderCell(h.Cell()).Text(title).SemiBold();
            }
        });
        foreach (var i in _model.Items)
        {
            BodyCell(table.Cell()).Text(i.Sequence.ToString(PtBr));
            BodyCell(table.Cell()).Text(i.Code);
            BodyCell(table.Cell()).Column(d =>
            {
                d.Item().Text(i.Description);
                if (i.SerialNumbers.Count > 0)
                {
                    d.Item().Text("Séries: " + string.Join(", ", i.SerialNumbers)).FontSize(PdfTheme.SmallFontSize);
                }

                if (i.LotNumber is { } lot)
                {
                    d.Item().Text("Lote: " + lot).FontSize(PdfTheme.SmallFontSize);
                }
            });
            BodyCell(table.Cell()).AlignRight().Text(ShipmentRules.Quantity(i.Quantity));
            BodyCell(table.Cell()).PaddingLeft(4).Text(i.Unit);
        }
    });
}
