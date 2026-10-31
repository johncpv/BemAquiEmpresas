using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Privacy;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BemAqui.Identity.Application.Privacy;

/// <summary>Conteúdo do PDF do titular (CA 8): a empresa (com o logo), o pedido, o titular e as seções de cada módulo.</summary>
public sealed record PersonalDataPdfModel(string CompanyName, byte[]? Logo, string RequestNumber, string GeneratedAt, string SubjectName, IReadOnlyList<PersonalDataSection> Sections);

/// <summary>
/// "dados.pdf" (LGPD-01a CA 8, ADR 0022): o mesmo conteúdo do <c>dados.json</c> em forma legível. A4, Archivo, logo do tenant, número do
/// pedido e data no cabeçalho; um título por módulo e uma tabela campo e valor por registro.
/// </summary>
public sealed class PersonalDataPdfDocument(PersonalDataPdfModel model) : IDocument
{
    public DocumentMetadata GetMetadata() => new() { Title = "Dados do titular — " + model.RequestNumber, Author = model.CompanyName, Creator = "BemAqui Empresas" };

    public void Compose(IDocumentContainer container) => container.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(32);
        page.DefaultTextStyle(t => t.FontFamily(QuestPdfRenderer.FontFamily).FontSize(9).FontColor("#14202b").EnableFontFeature(FontFeatures.TabularFigures));
        page.Header().Row(row =>
        {
            if (model.Logo is { Length: > 0 } logo)
            {
                row.ConstantItem(110).Height(44).Image(logo).FitArea();
            }

            row.RelativeItem().AlignRight().Column(col =>
            {
                col.Item().Text(model.CompanyName).SemiBold();
                col.Item().Text("Dados pessoais do titular — pedido " + model.RequestNumber).FontSize(11).Bold();
                col.Item().Text("Titular: " + model.SubjectName);
                col.Item().Text("Gerado em " + model.GeneratedAt);
            });
        });
        page.Content().PaddingVertical(12).Column(col =>
        {
            col.Spacing(10);
            col.Item().Text("Este arquivo reúne os dados pessoais que a empresa guarda sobre o titular, módulo por módulo, conforme a LGPD (art. 18 e 19). "
                + "Dados de outras pessoas aparecem mascarados.").Italic();
            foreach (var section in model.Sections)
            {
                col.Item().PaddingTop(6).Text(section.Title).FontSize(12).Bold();
                if (section.Note is { } note)
                {
                    col.Item().Text(note).Italic().FontColor("#4a5866");
                }

                foreach (var block in section.Blocks)
                {
                    col.Item().Text($"{block.Title} ({block.Records.Count})").SemiBold();
                    if (block.Records.Count == 0)
                    {
                        col.Item().Text("Nenhum registro.").FontColor("#4a5866");
                        continue;
                    }

                    foreach (var record in block.Records)
                    {
                        col.Item().Background("#eef2f6").Padding(6).Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(2);
                                c.RelativeColumn(5);
                            });
                            foreach (var field in record.Fields.Where(f => !string.IsNullOrEmpty(f.Value)))
                            {
                                t.Cell().PaddingVertical(1).Text(field.Name).SemiBold();
                                t.Cell().PaddingVertical(1).Text(field.Value!);
                            }
                        });
                    }
                }

                if (section.Files is { Count: > 0 } files)
                {
                    col.Item().Text("Arquivos na pasta anexos/: " + string.Join(", ", files.Select(f => f.FileName)));
                }
            }
        });
        page.Footer().AlignCenter().Text(t =>
        {
            t.Span("Documento gerado pelo BemAqui Empresas. Página ").FontSize(8);
            t.CurrentPageNumber().FontSize(8);
            t.Span(" de ").FontSize(8);
            t.TotalPages().FontSize(8);
        });
    });
}
