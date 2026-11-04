using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Privacy;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Identity.Application.Privacy;

/// <summary>Conteúdo do PDF do titular (CA 8): o pedido, o titular e as seções de cada módulo; a empresa (com o logo) vem do frame.</summary>
public sealed record PersonalDataPdfModel(string RequestNumber, string GeneratedAt, string SubjectName, IReadOnlyList<PersonalDataSection> Sections);

/// <summary>
/// "dados.pdf" (LGPD-01a CA 8) no modelo único (ADR 0045 §1, EXP-05 CA 19): o mesmo conteúdo do <c>dados.json</c> em forma legível, com o
/// cabeçalho, a faixa "Dados pessoais do titular — pedido {número}" e o rodapé do frame; um título por módulo e uma tabela campo e valor por
/// registro.
/// </summary>
public sealed class PersonalDataPdfDocument : PdfDocumentFrame
{
    private readonly PersonalDataPdfModel _model;

    public PersonalDataPdfDocument(PdfFrame frame, PersonalDataPdfModel model)
        : base(frame, Header(model)) => _model = model;

    private static PdfDocumentHeader Header(PersonalDataPdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new PdfDocumentHeader("Dados pessoais do titular", "— pedido " + model.RequestNumber);
    }

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var model = _model;
        col.Spacing(10);
        col.Item().Column(subject =>
        {
            subject.Item().Text("Titular: " + model.SubjectName);
            subject.Item().Text("Gerado em " + model.GeneratedAt);
        });
        col.Item().Text("Este arquivo reúne os dados pessoais que a empresa guarda sobre o titular, módulo por módulo, conforme a LGPD (art. 18 e 19). "
            + "Dados de outras pessoas aparecem mascarados.").Italic();
        foreach (var section in model.Sections)
        {
            col.Item().PaddingTop(6).Text(section.Title).FontSize(12).Bold();
            if (section.Note is { } note)
            {
                col.Item().Text(note).Italic().FontColor(PdfTheme.Muted);
            }

            foreach (var block in section.Blocks)
            {
                col.Item().Text($"{block.Title} ({block.Records.Count})").SemiBold();
                if (block.Records.Count == 0)
                {
                    col.Item().Text("Nenhum registro.").FontColor(PdfTheme.Muted);
                    continue;
                }

                foreach (var record in block.Records)
                {
                    col.Item().Background(PdfTheme.Surface).Padding(6).Table(t =>
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
}
