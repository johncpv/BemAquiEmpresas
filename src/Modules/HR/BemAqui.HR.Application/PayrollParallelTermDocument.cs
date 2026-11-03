using System.Globalization;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.HR.Domain;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.HR.Application;

/// <summary>Totais de uma folha no termo: proventos, descontos, líquido e o FGTS do mês.</summary>
public sealed record PayrollParallelTermTotals(decimal Gross, decimal Deductions, decimal Net, decimal Fgts);

/// <summary>O termo de conferência (CA 3): a competência, o tipo, quantos funcionários e os totais das duas folhas; a empresa vem do frame.</summary>
public sealed record PayrollParallelTermModel(
    DateOnly Period,
    string TypeLabel,
    int Employees,
    PayrollParallelTermTotals Entered,
    PayrollParallelTermTotals Calculated,
    string? CalculatorVersion,
    DateTime GeneratedAt,
    string? AccountantName,
    string? AccountantCrc);

/// <summary>
/// "Termo de conferência da competência {mês}" (FLH-06 CA 3) no modelo único (ADR 0045 §1, EXP-05 CA 19): os totais da folha da contadora e da
/// calculada lado a lado, a diferença zero e os campos da contadora responsável (nome, CRC, assinatura e data) e do Proprietário, com o
/// cabeçalho, a faixa de título e o rodapé do frame. Nenhum dado de funcionário: só os totais.
/// </summary>
public sealed class PayrollParallelTermDocument : PdfDocumentFrame
{
    private static readonly CultureInfo PtBr = PdfTheme.PtBr;

    private static readonly string[] Titles = ["Total", "Folha da contadora", "Folha calculada", "Diferença"];

    private readonly PayrollParallelTermModel _model;

    public PayrollParallelTermDocument(PdfFrame frame, PayrollParallelTermModel model)
        : base(frame, Header(model)) => _model = model;

    private static PdfDocumentHeader Header(PayrollParallelTermModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new PdfDocumentHeader("Termo de conferência da competência " + PayrollRules.PeriodLabel(model.Period));
    }

    private static string Money(decimal v) => v.ToString("N2", PtBr);

    private string Month => PayrollRules.PeriodLabel(_model.Period);

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var model = _model;
        col.Spacing(12);
        col.Item().Text($"Folha {model.TypeLabel} — conferência paralela da folha calculada pelo sistema com a folha da contadora");
        col.Item().Text($"Declaro que conferi, para a competência {Month}, a folha calculada pelo BemAqui Empresas (motor {model.CalculatorVersion ?? "—"}) com a folha que calculei para a empresa, para os {model.Employees} funcionários, por funcionário e por rubrica, e que não há diferença em nenhuma rubrica nem no líquido.");
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(3);
                cols.RelativeColumn(2);
                cols.RelativeColumn(2);
                cols.RelativeColumn(2);
            });
            foreach (var title in Titles)
            {
                HeaderCell(t.Cell()).Text(title).SemiBold();
            }

            foreach (var (label, entered, calculated) in new[]
            {
                ("Proventos", model.Entered.Gross, model.Calculated.Gross),
                ("Descontos", model.Entered.Deductions, model.Calculated.Deductions),
                ("Líquido", model.Entered.Net, model.Calculated.Net),
                ("FGTS do mês", model.Entered.Fgts, model.Calculated.Fgts),
            })
            {
                BodyCell(t.Cell()).Text(label);
                BodyCell(t.Cell()).AlignRight().Text(Money(entered));
                BodyCell(t.Cell()).AlignRight().Text(Money(calculated));
                BodyCell(t.Cell()).AlignRight().Text(Money(calculated - entered));
            }
        });
        col.Item().Text($"Gerado em {model.GeneratedAt.ToString("dd/MM/yyyy 'às' HH:mm", PtBr)}. Assine, digitalize e anexe em RH → Conferência da folha antes de aceitar a competência.")
            .FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted);
        col.Item().PaddingTop(24).Element(c => Signature(c, "Contadora responsável", model.AccountantName, model.AccountantCrc));
        col.Item().PaddingTop(24).Element(c => Signature(c, "Proprietário da empresa", null, null));
    });

    private static void Signature(IContainer c, string role, string? name, string? crc) => c.Column(col =>
    {
        col.Item().Text(role).SemiBold();
        col.Item().PaddingTop(6).Text("Nome: " + (name ?? "________________________________________________"));
        if (role.StartsWith("Contadora", StringComparison.Ordinal))
        {
            col.Item().PaddingTop(6).Text("CRC: " + (crc ?? "____________________"));
        }

        col.Item().PaddingTop(18).Text("Assinatura: ________________________________________   Data: ____/____/________");
    });
}
