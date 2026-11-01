using System.Globalization;
using System.Text;
using BemAqui.Assets.Domain;
using BemAqui.BuildingBlocks.Pdf;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BemAqui.Assets.Application;

/// <summary>Uma etiqueta (CA 1): a etiqueta em texto, o nome do bem e a URL curta do QR. Nenhum valor, responsável nem série: o modelo nem tem.</summary>
public sealed record AssetLabelItem(string Tag, string Name, string Url);

/// <summary>Etiquetas para imprimir: o nome fantasia do tenant, o tamanho do parâmetro e o leiaute (etiqueta por página ou folha A4).</summary>
public sealed record AssetLabelModel(IReadOnlyList<AssetLabelItem> Labels, string CompanyName, LabelSize Size, AssetLabelLayout Layout, DateTimeOffset GeneratedAt);

/// <summary>
/// Layout da etiqueta (PAT-01b CA 1 a 4, ADR 0022): QR à esquerda, em vetor (nítido em qualquer impressora térmica), gerado aqui com o
/// QRCoder (MIT, sem chamada externa); à direita a etiqueta em texto grande, o nome em até 2 linhas e o nome fantasia. Com
/// <see cref="AssetLabelLayout.Label"/>, uma etiqueta por página no tamanho do parâmetro; com <see cref="AssetLabelLayout.A4Sheet"/>, a grade
/// da <see cref="AssetLabelRules.Grid"/> com linha de corte. As datas do PDF são fixas (a do bem na reimpressão): a mesma etiqueta sai igual.
/// </summary>
public sealed class AssetLabelPdfDocument(AssetLabelModel model) : IDocument
{
    private const float PaddingMm = 1.5f;

    public AssetLabelModel Model => model;
    private const float PointsPerMm = 72f / 25.4f;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = model.Labels.Count == 1 ? "Etiqueta " + model.Labels[0].Tag : "Etiquetas do patrimônio", Author = model.CompanyName, Creator = "BemAqui Empresas",
        CreationDate = model.GeneratedAt, ModifiedDate = model.GeneratedAt,
    };

    public void Compose(IDocumentContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        if (model.Layout == AssetLabelLayout.A4Sheet)
        {
            var grid = AssetLabelRules.Grid(model.Size);
            foreach (var chunk in model.Labels.Chunk(grid.PerPage))
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(AssetLabelRules.SheetMarginMm, Unit.Millimetre);
                    Defaults(page);
                    page.Content().Column(column =>
                    {
                        column.Spacing(AssetLabelRules.SheetGapMm, Unit.Millimetre);
                        foreach (var line in chunk.Chunk(grid.Columns))
                        {
                            column.Item().Row(row =>
                            {
                                row.Spacing(AssetLabelRules.SheetGapMm, Unit.Millimetre);
                                foreach (var label in line)
                                {
                                    row.ConstantItem(model.Size.WidthMm, Unit.Millimetre).Height(model.Size.HeightMm, Unit.Millimetre)
                                        .Border(0.3f).BorderColor(Colors.Grey.Lighten2).Element(c => Label(c, label));
                                }
                            });
                        }
                    });
                });
            }

            return;
        }

        foreach (var label in model.Labels)
        {
            container.Page(page =>
            {
                page.Size(model.Size.WidthMm, model.Size.HeightMm, Unit.Millimetre);
                page.Margin(0);
                Defaults(page);
                page.Content().Element(c => Label(c, label));
            });
        }
    }

    /// <summary>
    /// SVG do QR (CA 2): só o texto recebido (a URL curta), correção M e a zona de silêncio de 4 módulos; um retângulo por trecho escuro de
    /// cada linha, sem anti-serrilhado.
    /// </summary>
    public static string QrSvg(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var matrix = data.ModuleMatrix;
        var size = matrix.Count;
        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {size} {size}\" width=\"{size}\" height=\"{size}\" shape-rendering=\"crispEdges\">");
        svg.Append(CultureInfo.InvariantCulture, $"<rect width=\"{size}\" height=\"{size}\" fill=\"#ffffff\"/><path fill=\"#000000\" d=\"");
        for (var y = 0; y < size; y++)
        {
            var x = 0;
            while (x < size)
            {
                if (!matrix[y][x])
                {
                    x++;
                    continue;
                }

                var start = x;
                while (x < size && matrix[y][x])
                {
                    x++;
                }

                svg.Append(CultureInfo.InvariantCulture, $"M{start} {y}h{x - start}v1h{start - x}z");
            }
        }

        svg.Append("\"/></svg>");
        return svg.ToString();
    }

    private static void Defaults(PageDescriptor page)
    {
        page.PageColor(Colors.White);
        page.DefaultTextStyle(t => t.FontFamily(QuestPdfRenderer.FontFamily).FontColor("#000000").EnableFontFeature(FontFeatures.TabularFigures).LineHeight(1.05f));
    }

    private void Label(IContainer container, AssetLabelItem label)
    {
        var size = model.Size;
        var qr = Math.Min(size.HeightMm - (2 * PaddingMm), size.WidthMm * 0.5f);
        var textWidth = size.WidthMm - qr - (3 * PaddingMm);

        // Etiqueta em texto grande: o maior corpo que cabe na largura (Archivo bold ~0,62 em por caractere), de 6 a 18 pt.
        var tagSize = Math.Clamp(textWidth * PointsPerMm / (Math.Max(label.Tag.Length, 1) * 0.62f), 6f, Math.Min(18f, size.HeightMm * 0.6f));
        var nameSize = Math.Clamp(tagSize * 0.6f, 5f, 9f);
        container.Padding(PaddingMm, Unit.Millimetre).Row(row =>
        {
            row.ConstantItem(qr, Unit.Millimetre).AlignMiddle().Height(qr, Unit.Millimetre).Svg(QrSvg(label.Url)).FitArea();
            row.ConstantItem(PaddingMm, Unit.Millimetre);
            row.RelativeItem().AlignMiddle().Column(column =>
            {
                column.Spacing(0.6f, Unit.Millimetre);
                column.Item().Text(label.Tag).FontSize(tagSize).Bold();
                column.Item().Text(label.Name).FontSize(nameSize).ClampLines(2);
                column.Item().Text(model.CompanyName).FontSize(Math.Max(nameSize - 1f, 4.5f)).ClampLines(1);
            });
        });
    }
}
