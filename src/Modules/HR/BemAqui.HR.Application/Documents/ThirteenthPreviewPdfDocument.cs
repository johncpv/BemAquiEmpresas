using System.Globalization;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Domain;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.HR.Application.Documents;

/// <summary>
/// "Prévia do 13º" em PDF (EXP-05 CA 11) no modelo único, sempre com a marca <see cref="PdfMark.Simulation"/>: o ano, a parcela, a
/// competência e a data limite; por funcionário, matrícula, nome, avos, base, médias, adiantamento abatido e o valor da parcela; os totais.
/// O nome do arquivo não leva nome nem CPF (<see cref="FileName"/>).
/// </summary>
public sealed class ThirteenthPreviewPdfDocument(PdfFrame frame, ThirteenthSimulation model)
    : PdfDocumentFrame(frame, new PdfDocumentHeader("Prévia do 13º", $"{InstallmentLabel(model.Preview.Type)} de {model.Preview.Year}",
        DateOnly.FromDateTime(BusinessTime.ToBusiness(frame.IssuedAt).DateTime), PdfMark.Simulation, true))
{
    private static readonly string[] Columns = ["Matrícula", "Nome", "Avos", "Base", "Médias", "Adiantamento abatido", "Valor da parcela"];

    public static string InstallmentLabel(PayrollRunType type) => type == PayrollRunType.Thirteenth2 ? "2ª parcela" : "1ª parcela";

    /// <summary><c>previa-13-{ano}-{1|2}a-parcela.pdf</c>: sem nome nem CPF.</summary>
    public static string FileName(int year, PayrollRunType type) =>
        string.Create(CultureInfo.InvariantCulture, $"previa-13-{year}-{(type == PayrollRunType.Thirteenth2 ? 2 : 1)}a-parcela.pdf");

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var p = model.Preview;
        col.Spacing(8);
        col.Item().Element(c => Fields(c, 4,
            ("Parcela", InstallmentLabel(p.Type)),
            ("Ano", p.Year.ToString(CultureInfo.InvariantCulture)),
            ("Competência", p.Period.Length == 7 ? $"{p.Period[5..]}/{p.Period[..4]}" : p.Period),
            ("Data limite", p.DueDate is { } due ? PdfTheme.Date(due) : null)));
        col.Item().Text($"Simulação pelo cadastro, pelos espelhos fechados e pelas tabelas de hoje: não é a folha. O mês conta com {p.MonthMinDays} dias ou mais trabalhados.")
            .FontColor(PdfTheme.Muted);
        if (model.Blocked is { } blocked)
        {
            col.Item().Background(PdfTheme.WarnSurface).PaddingVertical(3).PaddingHorizontal(6).Text("Sem valores: " + blocked).SemiBold().FontColor(PdfTheme.WarnInk);
        }

        if (model.Rows.Count == 0)
        {
            col.Item().Text("Nenhum funcionário com avos no ano.");
            return;
        }

        col.Item().Element(Table);
        if (model.Rows.Any(r => r.Issue is not null))
        {
            col.Item().Element(c => Section(c, "Sem valor"));
            foreach (var r in model.Rows.Where(r => r.Issue is not null))
            {
                col.Item().Text($"{r.Name}: {r.Issue}");
            }
        }
    });

    private void Table(IContainer c) => c.Table(table =>
    {
        table.ColumnsDefinition(cols =>
        {
            cols.ConstantColumn(70);
            cols.RelativeColumn(3);
            cols.ConstantColumn(44);
            cols.RelativeColumn();
            cols.RelativeColumn();
            cols.RelativeColumn();
            cols.RelativeColumn();
        });
        table.Header(h =>
        {
            for (var i = 0; i < Columns.Length; i++)
            {
                var cell = h.Cell().Element(HeaderCell);
                (i >= 2 ? cell.AlignRight() : cell).Text(Columns[i]).SemiBold();
            }
        });
        foreach (var r in model.Rows)
        {
            table.Cell().Element(BodyCell).Text(r.Registration ?? "—");
            table.Cell().Element(BodyCell).Text(r.Name);
            table.Cell().Element(BodyCell).AlignRight().Text($"{r.Avos}/12");
            table.Cell().Element(BodyCell).AlignRight().Text(Value(r.Base));
            table.Cell().Element(BodyCell).AlignRight().Text(Value(r.Averages));
            table.Cell().Element(BodyCell).AlignRight().Text(Value(r.AdvanceDeducted));
            table.Cell().Element(BodyCell).AlignRight().Text(Value(r.Amount));
        }

        table.Cell().Element(BodyCell).Text(string.Empty);
        table.Cell().Element(BodyCell).Text($"Total ({model.Rows.Count} {(model.Rows.Count == 1 ? "funcionário" : "funcionários")})").SemiBold();
        table.Cell().Element(BodyCell).Text(string.Empty);
        table.Cell().Element(BodyCell).AlignRight().Text(Total(r => r.Base)).SemiBold();
        table.Cell().Element(BodyCell).AlignRight().Text(Total(r => r.Averages)).SemiBold();
        table.Cell().Element(BodyCell).AlignRight().Text(Total(r => r.AdvanceDeducted)).SemiBold();
        table.Cell().Element(BodyCell).AlignRight().Text(Total(r => r.Amount)).SemiBold();
    });

    private static string Value(decimal? value) => value is { } v ? PdfTheme.Money(v) : "—";

    private string Total(Func<ThirteenthSimulationRow, decimal?> value) =>
        model.Rows.Any(r => value(r) is not null) ? PdfTheme.Money(model.Rows.Sum(r => value(r) ?? 0m)) : "—";
}
