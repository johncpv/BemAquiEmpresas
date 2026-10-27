using System.Globalization;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.HR.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BemAqui.HR.Application.Documents;

/// <summary>Empresa no cabeçalho (CAD-06): razão social, CNPJ e o logo publicado (só <c>Clean</c>).</summary>
public sealed record PayslipPdfCompany(string LegalName, string? Document, byte[]? Logo);

/// <summary>
/// Funcionário no holerite (CA 1): nome, matrícula, CPF já mascarado 3+2, cargo, CBO e admissão. Nada de CPF completo, endereço, dados
/// bancários nem dependentes: o modelo nem tem esses campos.
/// </summary>
public sealed record PayslipPdfEmployee(string Name, string? Registration, string MaskedCpf, string Position, string? Cbo, DateOnly AdmissionDate);

public sealed record PayslipPdfLine(string Code, string Description, PayrollItemKind Type, string? Reference, decimal Amount);

/// <summary>Holerite em PDF (RH-02b CA 1 e 3): o rascunho leva a marca d'água.</summary>
public sealed record PayslipPdfModel(
    PayslipPdfCompany Company,
    PayslipPdfEmployee Employee,
    DateOnly Period,
    PayrollRunType Type,
    IReadOnlyList<PayslipPdfLine> Lines,
    decimal Gross,
    decimal Deductions,
    decimal Net,
    decimal InssBase,
    decimal FgtsBase,
    decimal IrrfBase,
    decimal FgtsAmount,
    bool Draft);

/// <summary>
/// Layout do holerite (ADR 0022): A4, Archivo, números em pt-BR com dígitos tabulares, um holerite por documento, cabeçalho da tabela
/// repetido na quebra. Rascunho: marca d'água "RASCUNHO — folha não fechada" em todas as páginas.
/// </summary>
public sealed class PayslipPdfDocument(PayslipPdfModel model) : IDocument
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly string[] Columns = ["Código", "Descrição", "Referência", "Proventos", "Descontos"];

    private static readonly string[] BaseTitles = ["Base do INSS", "Base do FGTS", "FGTS do mês", "Base do IRRF"];

    private static string Money(decimal v) => v.ToString("N2", PtBr);

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Holerite {PayrollRules.TypeLabel(model.Type)} {PayslipDocumentRules.MonthYear(model.Period)}", Author = model.Company.LegalName, Creator = "BemAqui Empresas",
    };

    public void Compose(IDocumentContainer container) => container.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(32);
        page.DefaultTextStyle(t => t.FontFamily(QuestPdfRenderer.FontFamily).FontSize(9).FontColor("#14202b").EnableFontFeature(FontFeatures.TabularFigures));
        if (model.Draft)
        {
            page.Foreground().AlignCenter().AlignMiddle().Rotate(-30).Text(PayslipDocumentRules.Watermark).FontSize(36).Bold().FontColor("#c9d1d9");
        }

        page.Header().Element(Header);
        page.Content().PaddingVertical(12).Element(Content);
        page.Footer().AlignCenter().Text(t =>
        {
            t.Span("Documento gerado pelo BemAqui Empresas. Página ").FontSize(8);
            t.CurrentPageNumber().FontSize(8);
            t.Span(" de ").FontSize(8);
            t.TotalPages().FontSize(8);
        });
    });

    private void Header(IContainer c) => c.Row(row =>
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

            col.Item().Text($"Holerite — {PayrollRules.TypeLabel(model.Type)} — competência {PayslipDocumentRules.MonthYear(model.Period)}").FontSize(11).Bold();
        });
    });

    private void Content(IContainer c) => c.Column(col =>
    {
        col.Spacing(10);
        col.Item().Background("#eef2f6").Padding(8).Table(t =>
        {
            t.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(3);
                cols.RelativeColumn(2);
                cols.RelativeColumn(2);
            });
            t.Cell().Text("Funcionário: " + model.Employee.Name).SemiBold();
            t.Cell().Text("Matrícula: " + (model.Employee.Registration ?? "—"));
            t.Cell().Text("CPF: " + model.Employee.MaskedCpf);
            t.Cell().Text("Cargo: " + model.Employee.Position);
            t.Cell().Text("CBO: " + (model.Employee.Cbo ?? "—"));
            t.Cell().Text($"Admissão: {model.Employee.AdmissionDate:dd/MM/yyyy}");
        });
        col.Item().Element(Lines);
        col.Item().AlignRight().Text(t =>
        {
            t.Span("Total de proventos: ").SemiBold();
            t.Span(Money(model.Gross));
            t.Span("    Total de descontos: ").SemiBold();
            t.Span(Money(model.Deductions));
        });
        col.Item().AlignRight().Background("#eef2f6").Padding(8).Text("Líquido a receber: " + model.Net.ToString("C2", PtBr)).FontSize(13).Bold();
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(cols =>
            {
                for (var i = 0; i < 4; i++)
                {
                    cols.RelativeColumn();
                }
            });
            foreach (var title in BaseTitles)
            {
                t.Cell().BorderBottom(1).BorderColor("#9aa7b4").PaddingVertical(3).Text(title).SemiBold();
            }

            foreach (var value in new[] { model.InssBase, model.FgtsBase, model.FgtsAmount, model.IrrfBase })
            {
                t.Cell().PaddingVertical(3).Text(Money(value));
            }
        });
    });

    private void Lines(IContainer c) => c.Table(table =>
    {
        table.ColumnsDefinition(cols =>
        {
            cols.ConstantColumn(56);
            cols.RelativeColumn(5);
            cols.ConstantColumn(70);
            cols.ConstantColumn(80);
            cols.ConstantColumn(80);
        });
        table.Header(h =>
        {
            foreach (var title in Columns)
            {
                h.Cell().BorderBottom(1).BorderColor("#9aa7b4").PaddingVertical(4).Text(title).SemiBold();
            }
        });
        foreach (var l in model.Lines)
        {
            table.Cell().PaddingVertical(3).Text(l.Code);
            table.Cell().PaddingVertical(3).Text(l.Type == PayrollItemKind.Information ? $"{l.Description} (informativa: {Money(l.Amount)})" : l.Description);
            table.Cell().PaddingVertical(3).Text(l.Reference ?? string.Empty);
            table.Cell().PaddingVertical(3).AlignRight().Text(l.Type == PayrollItemKind.Earning ? Money(l.Amount) : string.Empty);
            table.Cell().PaddingVertical(3).AlignRight().Text(l.Type == PayrollItemKind.Deduction ? Money(l.Amount) : string.Empty);
        }
    });
}
