using System.Globalization;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.HR.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BemAqui.HR.Application;

/// <summary>Totais de uma folha no termo: proventos, descontos, líquido e o FGTS do mês.</summary>
public sealed record PayrollParallelTermTotals(decimal Gross, decimal Deductions, decimal Net, decimal Fgts);

/// <summary>O termo de conferência (CA 3): a empresa, a competência, o tipo, quantos funcionários e os totais das duas folhas.</summary>
public sealed record PayrollParallelTermModel(
    string CompanyName,
    string? CompanyDocument,
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
/// "Termo de conferência da competência {mês}" (FLH-06 CA 3, ADR 0022): A4, Archivo, números com dígitos tabulares. Os totais da folha da
/// contadora e da calculada lado a lado, a diferença zero e os campos da contadora responsável (nome, CRC, assinatura e data) e do
/// Proprietário. Nenhum dado de funcionário: só os totais.
/// </summary>
public sealed class PayrollParallelTermDocument(PayrollParallelTermModel model) : IDocument
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly string[] Titles = ["Total", "Folha da contadora", "Folha calculada", "Diferença"];

    private static string Money(decimal v) => v.ToString("N2", PtBr);

    private string Month => PayrollRules.PeriodLabel(model.Period);

    public DocumentMetadata GetMetadata() => new() { Title = $"Termo de conferência da competência {Month}", Author = model.CompanyName, Creator = "BemAqui Empresas" };

    public void Compose(IDocumentContainer container) => container.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(40);
        page.DefaultTextStyle(t => t.FontFamily(QuestPdfRenderer.FontFamily).FontSize(10).FontColor("#14202b").EnableFontFeature(FontFeatures.TabularFigures));
        page.Header().Column(col =>
        {
            col.Item().Text(model.CompanyName).SemiBold();
            if (model.CompanyDocument is { } doc)
            {
                col.Item().Text("CNPJ " + doc);
            }

            col.Item().PaddingTop(8).Text($"Termo de conferência da competência {Month}").FontSize(14).Bold();
            col.Item().Text($"Folha {model.TypeLabel} — conferência paralela da folha calculada pelo sistema com a folha da contadora");
        });
        page.Content().PaddingVertical(16).Column(col =>
        {
            col.Spacing(12);
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
                    t.Cell().BorderBottom(1).BorderColor("#9aa7b4").PaddingVertical(4).Text(title).SemiBold();
                }

                foreach (var (label, entered, calculated) in new[]
                {
                    ("Proventos", model.Entered.Gross, model.Calculated.Gross),
                    ("Descontos", model.Entered.Deductions, model.Calculated.Deductions),
                    ("Líquido", model.Entered.Net, model.Calculated.Net),
                    ("FGTS do mês", model.Entered.Fgts, model.Calculated.Fgts),
                })
                {
                    t.Cell().PaddingVertical(3).Text(label);
                    t.Cell().PaddingVertical(3).AlignRight().Text(Money(entered));
                    t.Cell().PaddingVertical(3).AlignRight().Text(Money(calculated));
                    t.Cell().PaddingVertical(3).AlignRight().Text(Money(calculated - entered));
                }
            });
            col.Item().Text($"Gerado em {model.GeneratedAt.ToString("dd/MM/yyyy 'às' HH:mm", PtBr)}. Assine, digitalize e anexe em RH → Conferência da folha antes de aceitar a competência.").FontSize(8).FontColor("#4a5866");
            col.Item().PaddingTop(24).Element(c => Signature(c, "Contadora responsável", model.AccountantName, model.AccountantCrc));
            col.Item().PaddingTop(24).Element(c => Signature(c, "Proprietário da empresa", null, null));
        });
        page.Footer().AlignCenter().Text(t =>
        {
            t.Span("Documento gerado pelo BemAqui Empresas. Página ").FontSize(8);
            t.CurrentPageNumber().FontSize(8);
            t.Span(" de ").FontSize(8);
            t.TotalPages().FontSize(8);
        });
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
