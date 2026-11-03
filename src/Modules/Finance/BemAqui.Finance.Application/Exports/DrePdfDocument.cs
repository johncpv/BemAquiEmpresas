using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Domain;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Finance.Application.Exports;

/// <summary>
/// "DRE gerencial" do período (EXP-02 CA 15), em paisagem: as linhas na ordem da tela com o recuo por nível (8 pt), subtotais e resultados em
/// negrito, um mês por coluna, total, % da receita líquida e Δ; resultado negativo no vermelho do <see cref="PdfTheme"/>. Com receita
/// provisória, a faixa "Provisório"; sempre a nota "Gerencial, por competência; não substitui a contabilidade." Sem drill-down.
/// </summary>
public sealed class DrePdfDocument(PdfFrame frame, DreReport report)
    : PdfDocumentFrame(frame, new PdfDocumentHeader("DRE gerencial", null, DateOnly.FromDateTime(BusinessTime.ToBusiness(frame.IssuedAt).DateTime), Landscape: true))
{
    public const float IndentPerLevel = 8;

    private static readonly string[] TailHeaders = ["Total", "% RL", "Δ mês ant.", "Δ %"];

    /// <summary>Até 6 meses, a fonte normal; com mais colunas, menor (24 meses cabem na A4 em paisagem). Os valores saem sem "R$".</summary>
    private float FontSize => report.Months.Count switch
    {
        <= 6 => 8f,
        <= 12 => 6.5f,
        _ => 5f,
    };

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        col.Spacing(8);
        if (report.IsProvisional)
        {
            col.Item().Background(PdfTheme.WarnSurface).PaddingVertical(3).PaddingHorizontal(6).Text(FinanceExports.DreProvisional).SemiBold().FontColor(PdfTheme.WarnInk);
        }

        col.Item().Text(t =>
        {
            t.DefaultTextStyle(s => s.FontColor(PdfTheme.Muted));
            t.Span("Período: ").SemiBold();
            t.Span($"{FinanceExports.MonthLabel(report.From)} a {FinanceExports.MonthLabel(report.To)} · valores em reais");
        });
        col.Item().DefaultTextStyle(s => s.FontSize(FontSize)).Table(t =>
        {
            t.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(report.Months.Count > 12 ? 5 : 4);
                for (var i = 0; i < report.Months.Count + 4; i++)
                {
                    cols.RelativeColumn();
                }
            });
            t.Header(h =>
            {
                HeaderCell(h.Cell()).Text("Linha").SemiBold();
                foreach (var m in report.Months)
                {
                    HeaderCell(h.Cell()).AlignRight().Text(FinanceExports.MonthLabel(m.Month) + (m.IsProvisional ? "*" : string.Empty)).SemiBold();
                }

                foreach (var label in TailHeaders)
                {
                    HeaderCell(h.Cell()).AlignRight().Text(label).SemiBold();
                }
            });
            foreach (var row in report.Rows)
            {
                var strong = row.Kind != DreRowKind.Account;
                var label = BodyCell(t.Cell()).PaddingLeft(row.Level * IndentPerLevel).Text(row.Code is { Length: > 0 } code ? $"{code} {row.Label}" : row.Label);
                if (strong)
                {
                    label.SemiBold();
                }

                foreach (var value in row.Values)
                {
                    Number(t.Cell(), row, value, strong);
                }

                Number(t.Cell(), row, row.Total, strong);
                Percent(t.Cell(), row.PercentOfNetRevenue);
                Number(t.Cell(), row, row.Delta, false);
                Percent(t.Cell(), row.DeltaPercent);
            }
        });
        if (report.Months.Any(m => m.IsProvisional))
        {
            col.Item().Text("* Mês provisório.").FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted);
        }

        col.Item().Text(FinanceExports.DreNote).FontColor(PdfTheme.Muted);
    });

    private static void Number(IContainer cell, DreRow row, decimal? value, bool strong)
    {
        var text = BodyCell(cell).AlignRight().Text(value is { } v ? v.ToString("N2", PdfTheme.PtBr) : "—");
        if (strong)
        {
            text.SemiBold();
        }

        if (row.CriticalWhenNegative && value < 0)
        {
            text.FontColor(PdfTheme.CritInk);
        }
    }

    private static void Percent(IContainer cell, decimal? value) =>
        BodyCell(cell).AlignRight().Text(value is { } v ? PdfTheme.Percent(v) : "—");
}
