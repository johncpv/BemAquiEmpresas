using System.Globalization;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Fiscal.Domain;
using BemAqui.Identity.Contracts;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BemAqui.Fiscal.Application.Settings;

public sealed record FiscalSettingsPdfRow(string Section, string Label, string Value, string Situation);

public sealed record FiscalSettingsPdfModel(string LegalName, string? Document, DateTimeOffset GeneratedAt, int Confirmed, int Total, IReadOnlyList<FiscalSettingsPdfRow> Rows);

/// <summary>
/// "Baixar para a contadora" (FIS-02a CA 10): todos os campos de Configurações → Fiscal, o valor, a situação (conferido ou a confirmar) e
/// espaço para a resposta. A4 deitado, Archivo embutida (ADR 0022).
/// </summary>
public sealed class FiscalSettingsPdfDocument(FiscalSettingsPdfModel model) : IDocument
{
    private static readonly string[] Columns = ["Seção", "Campo", "Valor", "Situação", "Resposta da contadora"];

    public DocumentMetadata GetMetadata() => new() { Title = "Configuração fiscal para a contadora", Author = model.LegalName, Creator = "BemAqui Empresas" };

    public void Compose(IDocumentContainer container) => container.Page(page =>
    {
        page.Size(PageSizes.A4.Landscape());
        page.Margin(28);
        page.DefaultTextStyle(t => t.FontFamily(QuestPdfRenderer.FontFamily).FontSize(8.5f).FontColor("#14202b").EnableFontFeature(FontFeatures.TabularFigures));
        page.Header().Column(col =>
        {
            col.Item().Text("Configuração fiscal para conferência da contadora").FontSize(13).SemiBold();
            col.Item().Text(model.LegalName + (model.Document is { } d ? " · CNPJ " + Cnpj(d) : string.Empty));
            col.Item().Text($"Gerado em {BusinessTime.ToBusiness(model.GeneratedAt).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)} · {model.Confirmed} de {model.Total} campos conferidos")
                .FontColor("#4b5866");
        });
        page.Content().PaddingVertical(10).Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(1.3f);
                c.RelativeColumn(2.2f);
                c.RelativeColumn(2.6f);
                c.RelativeColumn(1.2f);
                c.RelativeColumn(2.4f);
            });
            table.Header(h =>
            {
                foreach (var title in Columns)
                {
                    h.Cell().Background("#e6ebf0").Padding(4).Text(title).SemiBold();
                }
            });
            foreach (var row in model.Rows)
            {
                table.Cell().BorderBottom(0.5f).BorderColor("#c9d1d9").Padding(4).Text(row.Section);
                table.Cell().BorderBottom(0.5f).BorderColor("#c9d1d9").Padding(4).Text(row.Label);
                table.Cell().BorderBottom(0.5f).BorderColor("#c9d1d9").Padding(4).Text(row.Value);
                table.Cell().BorderBottom(0.5f).BorderColor("#c9d1d9").Padding(4).Text(row.Situation);
                table.Cell().BorderBottom(0.5f).BorderColor("#c9d1d9").MinHeight(22).Padding(4).Text(string.Empty);
            }
        });
        page.Footer().AlignCenter().Text(t =>
        {
            t.Span("Valores sugeridos pelo sistema. Confira cada um antes de emitir em produção.   Página ").FontSize(7.5f);
            t.CurrentPageNumber().FontSize(7.5f);
            t.Span(" de ").FontSize(7.5f);
            t.TotalPages().FontSize(7.5f);
        });
    });

    private static string Cnpj(string d) => d.Length == 14 ? $"{d[..2]}.{d[2..5]}.{d[5..8]}/{d[8..12]}-{d[12..]}" : d;
}

/// <summary>Monta o PDF da conferência a partir da configuração de agora (CA 10).</summary>
public sealed class FiscalSettingsExport(FiscalSettingsService settings, ICompanyFiscalProfile company, IPdfRenderer renderer, TimeProvider clock)
{
    public async Task<byte[]> RenderAsync(CancellationToken cancellationToken = default)
    {
        var current = await settings.CurrentAsync(cancellationToken);
        var view = await settings.GetAsync(cancellationToken);
        var profile = await company.GetFiscalProfileAsync(cancellationToken);
        var sections = FiscalSettingKeys.Sections.ToDictionary(s => s.Code, s => s.Title, StringComparer.Ordinal);
        var reviews = view.Reviews.ToDictionary(r => r.Key, StringComparer.Ordinal);
        var rows = FiscalSettingKeys.All.Select(k =>
        {
            var review = reviews[k.Key];
            var situation = review.ReviewState == FiscalReviewState.Confirmed
                ? "Conferido" + (review.ReviewedAt is { } at ? " em " + BusinessTime.ToBusiness(at).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : string.Empty)
                : "A confirmar";
            return new FiscalSettingsPdfRow(sections[k.Section], k.Label, FiscalSettingsMapping.Display(k, current), situation);
        }).ToList();
        return await renderer.RenderAsync(new FiscalSettingsPdfDocument(new FiscalSettingsPdfModel(profile.LegalName, profile.Document, clock.GetUtcNow(), view.ConfirmedCount, view.TotalCount, rows)), cancellationToken);
    }
}
