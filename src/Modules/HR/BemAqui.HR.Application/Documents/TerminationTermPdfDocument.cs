using System.Globalization;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.HR.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BemAqui.HR.Application.Documents;

/// <summary>Linha do termo: código, descrição, referência, tipo e valor.</summary>
public sealed record TerminationTermLine(string Code, string Name, string? Reference, PayrollItemKind Type, decimal Amount);

/// <summary>
/// Termo de rescisão para conferência (FLH-05 CA 6 e 7): a empresa, o funcionário, o tipo, o aviso, as datas, as verbas e os descontos, o
/// líquido, o FGTS e a multa a recolher e o prazo do pagamento, com a marca de conferência interna (e a de simulação antes da virada).
/// </summary>
public sealed record TerminationTermModel(
    PayslipPdfCompany Company,
    string EmployeeName,
    string? Registration,
    string Position,
    DateOnly Admission,
    DateOnly TerminationDate,
    string KindLabel,
    string NoticeLabel,
    int NoticeDays,
    IReadOnlyList<TerminationTermLine> Lines,
    decimal Gross,
    decimal Deductions,
    decimal Net,
    decimal Fgts,
    decimal Fine,
    DateOnly PaymentDeadline,
    bool Simulation,
    DateOnly IssuedOn);

/// <summary>Layout do termo de conferência (ADR 0022): A4, Archivo, a marca de conferência no topo e no rodapé; nunca é o termo oficial.</summary>
public sealed class TerminationTermPdfDocument(TerminationTermModel model) : IDocument
{
    public const string ConferenceMark = "Documento de conferência interna: o termo oficial é emitido pela contadora.";
    public const string SimulationMark = "Simulação";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly string[] Columns = ["Código", "Descrição", "Referência", "Proventos", "Descontos"];

    private static string D(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string M(decimal v) => v.ToString("N2", PtBr);

    public DocumentMetadata GetMetadata() => new() { Title = "Termo de rescisão para conferência", Author = model.Company.LegalName, Creator = "BemAqui Empresas" };

    public void Compose(IDocumentContainer container) => container.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(36);
        page.DefaultTextStyle(t => t.FontFamily(QuestPdfRenderer.FontFamily).FontSize(9.5f).FontColor("#14202b").EnableFontFeature(FontFeatures.TabularFigures));
        page.Header().Column(head =>
        {
            head.Item().Row(row =>
            {
                if (model.Company.Logo is { Length: > 0 } logo)
                {
                    row.ConstantItem(110).Height(44).Image(logo).FitArea();
                }

                row.RelativeItem().AlignRight().Column(col =>
                {
                    col.Item().Text(model.Company.LegalName).SemiBold();
                    if (model.Company.Document is { } doc)
                    {
                        col.Item().Text("CNPJ " + doc);
                    }

                    col.Item().Text("Termo de rescisão para conferência").FontSize(14).Bold();
                });
            });
            head.Item().PaddingTop(6).Background("#fff4d6").Padding(6).Text(model.Simulation ? $"{SimulationMark} · {ConferenceMark}" : ConferenceMark).SemiBold().FontColor("#7a4b00");
        });
        page.Content().PaddingVertical(12).Column(col =>
        {
            col.Spacing(8);
            col.Item().Text($"Funcionário: {model.EmployeeName}" + (model.Registration is { } reg ? $" (matrícula {reg})" : string.Empty)).SemiBold();
            col.Item().Text($"Cargo: {model.Position} · Admissão: {D(model.Admission)} · Desligamento: {D(model.TerminationDate)}");
            col.Item().Text($"Tipo: {model.KindLabel} · Aviso prévio: {model.NoticeLabel} ({model.NoticeDays} dias)");
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(60);
                    c.RelativeColumn(4);
                    c.RelativeColumn(1.4f);
                    c.RelativeColumn(1.4f);
                    c.RelativeColumn(1.4f);
                });
                table.Header(h =>
                {
                    foreach (var title in Columns)
                    {
                        h.Cell().BorderBottom(1).BorderColor("#9aa7b4").PaddingVertical(3).Text(title).SemiBold();
                    }
                });
                foreach (var line in model.Lines)
                {
                    table.Cell().PaddingVertical(2).Text(line.Code);
                    table.Cell().PaddingVertical(2).Text(line.Name + (line.Type == PayrollItemKind.Information ? " (informativo)" : string.Empty));
                    table.Cell().PaddingVertical(2).Text(line.Reference ?? string.Empty);
                    table.Cell().PaddingVertical(2).AlignRight().Text(line.Type == PayrollItemKind.Earning ? M(line.Amount) : line.Type == PayrollItemKind.Information ? $"({M(line.Amount)})" : string.Empty);
                    table.Cell().PaddingVertical(2).AlignRight().Text(line.Type == PayrollItemKind.Deduction ? M(line.Amount) : string.Empty);
                }
            });
            col.Item().Background("#eef2f6").Padding(8).Column(box =>
            {
                box.Spacing(3);
                box.Item().Text($"Total de proventos: R$ {M(model.Gross)} · Total de descontos: R$ {M(model.Deductions)}");
                box.Item().Text($"Líquido a pagar: R$ {M(model.Net)}").SemiBold();
                box.Item().Text($"FGTS a recolher (do mês e rescisório): R$ {M(model.Fgts)} · Multa do FGTS: R$ {M(model.Fine)}");
                box.Item().Text($"Prazo do pagamento: {D(model.PaymentDeadline)} (CLT art. 477 §6º).");
            });
            col.Item().Text($"Emitido em {D(model.IssuedOn)}.");
        });
        page.Footer().AlignCenter().Text($"{ConferenceMark} Gerado pelo BemAqui Empresas.").FontSize(8);
    });
}
