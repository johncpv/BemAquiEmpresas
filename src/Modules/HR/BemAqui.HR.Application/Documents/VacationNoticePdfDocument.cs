using System.Globalization;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.HR.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BemAqui.HR.Application.Documents;

/// <summary>Aviso de férias (FLH-03 CA 5, CLT art. 135): a empresa, o funcionário, os períodos, o retorno, o abono e o pagamento, sem valor.</summary>
public sealed record VacationNoticeModel(
    PayslipPdfCompany Company,
    string EmployeeName,
    string? Registration,
    string Position,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly Start,
    DateOnly End,
    int Days,
    int SoldDays,
    DateOnly PaymentDue,
    DateOnly IssuedOn);

/// <summary>Layout do aviso de férias (ADR 0022): A4, Archivo, com o campo de ciência do funcionário.</summary>
public sealed class VacationNoticePdfDocument(VacationNoticeModel model) : IDocument
{
    private static string D(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public DocumentMetadata GetMetadata() => new() { Title = "Aviso de férias", Author = model.Company.LegalName, Creator = "BemAqui Empresas" };

    public void Compose(IDocumentContainer container) => container.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(40);
        page.DefaultTextStyle(t => t.FontFamily(QuestPdfRenderer.FontFamily).FontSize(10).FontColor("#14202b").EnableFontFeature(FontFeatures.TabularFigures));
        page.Header().Row(row =>
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

                col.Item().Text("Aviso de férias").FontSize(14).Bold();
            });
        });
        page.Content().PaddingVertical(20).Column(col =>
        {
            col.Spacing(10);
            col.Item().Text($"Funcionário: {model.EmployeeName}" + (model.Registration is { } reg ? $" (matrícula {reg})" : string.Empty)).SemiBold();
            col.Item().Text("Cargo: " + model.Position);
            col.Item().Text($"Comunicamos que, nos termos do art. 135 da CLT, suas férias referentes ao período aquisitivo de {D(model.PeriodStart)} a {D(model.PeriodEnd)} serão concedidas conforme abaixo.");
            col.Item().Background("#eef2f6").Padding(10).Column(box =>
            {
                box.Spacing(4);
                box.Item().Text($"Gozo: de {D(model.Start)} a {D(model.End)} ({model.Days} dias).");
                box.Item().Text($"Retorno ao trabalho: {D(model.End.AddDays(1))}.");
                box.Item().Text(model.SoldDays > 0 ? $"Abono pecuniário: {model.SoldDays} dias convertidos em dinheiro." : "Abono pecuniário: não.");
                box.Item().Text($"Pagamento das férias até {D(model.PaymentDue)}.");
            });
            col.Item().Text($"Emitido em {D(model.IssuedOn)}.");
            col.Item().PaddingTop(50).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().LineHorizontal(1).LineColor("#9aa7b4");
                    c.Item().AlignCenter().Text(model.Company.LegalName).FontSize(9);
                });
                row.ConstantItem(40);
                row.RelativeItem().Column(c =>
                {
                    c.Item().LineHorizontal(1).LineColor("#9aa7b4");
                    c.Item().AlignCenter().Text($"Ciente: {model.EmployeeName}").FontSize(9);
                });
            });
        });
        page.Footer().AlignCenter().Text("Documento gerado pelo BemAqui Empresas.").FontSize(8);
    });
}
