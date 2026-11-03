using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Production.Domain;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Production.Application;

/// <summary>
/// Termo de aceite técnico (EXP-04 CA 8): identificação do cliente, do projeto e da obra, o equipamento com as séries, o checklist da entrega
/// técnica com "Conforme / Não conforme / Observação", o texto fixo da declaração e as assinaturas de quem aceita pelo cliente e do
/// instalador. Em branco até o aceite; com a OS concluída, as marcas do checklist, a data e quem aceitou, e a faixa "Aceite registrado em".
/// </summary>
public sealed class AcceptanceTermPdfDocument : PdfDocumentFrame
{
    private const string Blank = "____________________";

    private readonly AcceptanceTermPdfModel _model;

    public AcceptanceTermPdfDocument(PdfFrame frame, AcceptanceTermPdfModel model)
        : base(frame, Header(model)) => _model = model;

    private static PdfDocumentHeader Header(AcceptanceTermPdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new PdfDocumentHeader("Termo de aceite técnico", model.Number, model.IssueDate, Notice: InstallationPdf.TermNotice(model.Status, model.AcceptedOn));
    }

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var m = _model;
        col.Spacing(8);
        col.Item().Element(c => Fields(c, 2,
            ("Empresa", Frame.Company.LegalName),
            ("Cliente", m.CustomerDocument is { Length: > 0 } doc ? $"{m.CustomerName} · {doc}" : m.CustomerName),
            ("Projeto", $"{m.ProjectNumber} · {m.ProjectName}"),
            ("Endereço da obra", m.SiteAddress),
            ("Instalador", m.InstallerName),
            ("OS de instalação", m.Number)));
        col.Item().Element(c => Section(c, "Equipamento"));
        col.Item().Column(list =>
        {
            if (m.Equipment.Count == 0)
            {
                list.Item().Text("Equipamento: " + Blank).FontColor(PdfTheme.Muted);
            }

            foreach (var e in m.Equipment)
            {
                list.Item().BorderBottom(0.5f).BorderColor(PdfTheme.Surface).PaddingVertical(3).Column(item =>
                {
                    item.Item().Text(t =>
                    {
                        t.Span(e.Description).SemiBold();
                        t.Span($" · {PdfTheme.Quantity(e.Quantity)}{(e.Unit is { Length: > 0 } u ? " " + u : string.Empty)}");
                    });
                    item.Item().Text(e.Serials.Count == 0 ? "Série: " + Blank : "Série: " + string.Join(", ", e.Serials)).FontSize(PdfTheme.SmallFontSize);
                });
            }
        });
        col.Item().Element(c => Section(c, "Checklist da entrega técnica"));
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(4);
                cols.ConstantColumn(58);
                cols.ConstantColumn(70);
                cols.RelativeColumn(2);
            });
            table.Header(h =>
            {
                HeaderCell(h.Cell()).Text("Item").SemiBold();
                HeaderCell(h.Cell()).AlignCenter().Text("Conforme").SemiBold();
                HeaderCell(h.Cell()).AlignCenter().Text("Não conforme").SemiBold();
                HeaderCell(h.Cell()).Text("Observação").SemiBold();
            });
            foreach (var c in m.Checklist)
            {
                BodyCell(table.Cell()).Text(c.Text);
                BodyCell(table.Cell()).AlignCenter().Element(e => Box(e, c.Result == ChecklistResult.Done));
                BodyCell(table.Cell()).AlignCenter().Element(e => Box(e, c.Result == ChecklistResult.NonConforming));
                BodyCell(table.Cell()).Text(c.Result == ChecklistResult.NotApplicable ? "Não se aplica" : string.Empty);
            }
        });
        col.Item().PaddingTop(8).Text(InstallationPdf.Declaration).SemiBold();
        col.Item().PaddingTop(8).Row(row =>
        {
            row.RelativeItem().Column(client =>
            {
                client.Item().Text("Pelo cliente").SemiBold();
                client.Item().Text("Nome: " + (m.AcceptedByName ?? Blank));
                client.Item().Text("Documento: " + Blank);
                client.Item().Text("Data: " + (m.AcceptedOn is { } day ? PdfTheme.Date(day) : "____/____/________"));
                client.Item().Element(e => Signature(e, "Assinatura de quem aceita pelo cliente"));
            });
            row.ConstantItem(24);
            row.RelativeItem().Column(installer =>
            {
                installer.Item().Text("Pelo instalador").SemiBold();
                installer.Item().Text("Nome: " + Blank);
                installer.Item().Text("Documento: " + Blank);
                installer.Item().Text("Data: ____/____/________");
                installer.Item().Element(e => Signature(e, "Assinatura do instalador"));
            });
        });
    });

    private static void Box(IContainer c, bool marked) =>
        c.Width(9).Height(9).Border(1).BorderColor(PdfTheme.Ink).AlignCenter().AlignMiddle().Text(marked ? "X" : string.Empty).FontSize(7).Bold();
}
