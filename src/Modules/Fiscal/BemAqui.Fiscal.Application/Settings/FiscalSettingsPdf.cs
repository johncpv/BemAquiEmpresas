using System.Globalization;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Fiscal.Domain;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Fiscal.Application.Settings;

public sealed record FiscalSettingsPdfRow(string Section, string Label, string Value, string Situation);

/// <summary>Quando foi gerado, quantos campos já foram conferidos e as linhas; a empresa (razão social e CNPJ) vem do frame.</summary>
public sealed record FiscalSettingsPdfModel(DateTimeOffset GeneratedAt, int Confirmed, int Total, IReadOnlyList<FiscalSettingsPdfRow> Rows);

/// <summary>
/// "Baixar para a contadora" (FIS-02a CA 10) no modelo único (ADR 0045 §1, EXP-05 CA 19): todos os campos de Configurações → Fiscal, o valor,
/// a situação (conferido ou a confirmar) e espaço para a resposta, em A4 deitado, com o cabeçalho, a faixa de título e o rodapé do frame; o
/// aviso "Valores sugeridos pelo sistema" fecha o corpo.
/// </summary>
public sealed class FiscalSettingsPdfDocument : PdfDocumentFrame
{
    public const string Caution = "Valores sugeridos pelo sistema. Confira cada um antes de emitir em produção.";

    private static readonly string[] Columns = ["Seção", "Campo", "Valor", "Situação", "Resposta da contadora"];

    private readonly FiscalSettingsPdfModel _model;

    public FiscalSettingsPdfDocument(PdfFrame frame, FiscalSettingsPdfModel model)
        : base(frame, new PdfDocumentHeader("Configuração fiscal para conferência da contadora", Landscape: true)) =>
        _model = model ?? throw new ArgumentNullException(nameof(model));

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var model = _model;
        col.Spacing(8);
        col.Item().Text($"Gerado em {BusinessTime.ToBusiness(model.GeneratedAt).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)} · {model.Confirmed} de {model.Total} campos conferidos")
            .FontColor(PdfTheme.Muted);
        col.Item().Table(table =>
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
                    HeaderCell(h.Cell()).Text(title).SemiBold();
                }
            });
            foreach (var row in model.Rows)
            {
                BodyCell(table.Cell()).Text(row.Section);
                BodyCell(table.Cell()).Text(row.Label);
                BodyCell(table.Cell()).Text(row.Value);
                BodyCell(table.Cell()).Text(row.Situation);
                BodyCell(table.Cell()).MinHeight(22).Text(string.Empty);
            }
        });
        col.Item().Text(Caution).FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted);
    });
}

/// <summary>Monta o PDF da conferência a partir da configuração de agora (CA 10).</summary>
public sealed class FiscalSettingsExport(FiscalSettingsService settings, PdfFrameFactory frames, IPdfRenderer renderer, TimeProvider clock)
{
    public async Task<byte[]> RenderAsync(CancellationToken cancellationToken = default)
    {
        var current = await settings.CurrentAsync(cancellationToken);
        var view = await settings.GetAsync(cancellationToken);
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
        var frame = await frames.CreateAsync(cancellationToken);
        return await renderer.RenderAsync(new FiscalSettingsPdfDocument(frame, new FiscalSettingsPdfModel(clock.GetUtcNow(), view.ConfirmedCount, view.TotalCount, rows)), cancellationToken);
    }
}
