using BemAqui.BuildingBlocks.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Production.Application;

/// <summary>
/// OS de instalação no modelo único (EXP-04 CA 7): dados da OS, do cliente e da obra, o instalador, a previsão, o indicador de etapa, as
/// etapas e o checklist da entrega técnica (caixa em branco ou marcada). Sem o valor combinado com o instalador.
/// </summary>
public sealed class InstallationOrderPdfDocument : PdfDocumentFrame
{
    private readonly InstallationPdfModel _model;

    public InstallationOrderPdfDocument(PdfFrame frame, InstallationPdfModel model)
        : base(frame, Header(model)) => _model = model;

    private static PdfDocumentHeader Header(InstallationPdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var (mark, notice) = InstallationPdf.Mark(model.Status, model.CancelReason);
        return new PdfDocumentHeader("Ordem de serviço de instalação", model.Number, model.IssueDate, mark, Notice: notice);
    }

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var m = _model;
        col.Spacing(8);
        col.Item().Element(c => Fields(c, 3,
            ("Situação", m.StatusLabel),
            ("Projeto", $"{m.ProjectNumber} · {m.ProjectName}"),
            ("Cliente", m.CustomerDocument is { Length: > 0 } doc ? $"{m.CustomerName} · {doc}" : m.CustomerName),
            ("Endereço da obra", m.SiteAddress),
            ("Instalador", m.InstallerName),
            ("Previsão", $"{PdfTheme.Date(m.PlannedStart)} a {PdfTheme.Date(m.PlannedEnd)}"),
            ("Início", m.StartedOn is { } s ? PdfTheme.Date(s) : null),
            ("Aceite", m.AcceptedOn is { } a ? $"{PdfTheme.Date(a)} · {m.AcceptedByName}" : null)));
        if (m.Letters.Count > 0 && m.Status != Domain.ProductionOrderStatus.Cancelled)
        {
            col.Item().Row(r =>
            {
                r.AutoItem().AlignMiddle().PaddingRight(6).Text("Etapa atual").FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted);
                r.AutoItem().Element(e => PdfStageIndicator.Compose(e, m.Letters, m.CurrentIndex));
            });
        }

        col.Item().Element(c => Section(c, "Etapas"));
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(24);
                cols.RelativeColumn(4);
                cols.RelativeColumn(2);
                cols.ConstantColumn(80);
            });
            table.Header(h =>
            {
                HeaderCell(h.Cell()).Text("#").SemiBold();
                HeaderCell(h.Cell()).Text("Etapa").SemiBold();
                HeaderCell(h.Cell()).Text("Situação").SemiBold();
                HeaderCell(h.Cell()).AlignRight().Text("Concluída em").SemiBold();
            });
            foreach (var s in m.Steps)
            {
                BodyCell(table.Cell()).Text(s.Sequence.ToString(PdfTheme.PtBr));
                BodyCell(table.Cell()).Text(s.Name);
                BodyCell(table.Cell()).Text(s.StatusLabel);
                BodyCell(table.Cell()).AlignRight().Text(s.CompletedOn is { } d ? PdfTheme.Date(d) : "—");
            }
        });
        col.Item().Element(c => Section(c, "Checklist da entrega técnica"));
        col.Item().Column(list =>
        {
            if (m.Checklist.Count == 0)
            {
                list.Item().Text("A OS não tem checklist.").FontColor(PdfTheme.Muted);
            }

            foreach (var item in m.Checklist)
            {
                list.Item().PaddingTop(2).Row(check =>
                {
                    check.ConstantItem(9).Height(9).Border(1).BorderColor(PdfTheme.Ink).AlignCenter().AlignMiddle().Text(item.ResultLabel is null ? string.Empty : "X").FontSize(7).Bold();
                    check.ConstantItem(5);
                    check.RelativeItem().Text(t =>
                    {
                        t.Span(item.Text);
                        if (item.ResultLabel is { } label)
                        {
                            t.Span(" · " + label).FontColor(PdfTheme.Muted);
                        }
                    });
                });
            }
        });
        col.Item().PaddingTop(24).Row(row =>
        {
            row.RelativeItem().Element(e => Signature(e, "Instalador (nome e assinatura)"));
            row.ConstantItem(24);
            row.RelativeItem().Element(e => Signature(e, "Responsável pela empresa"));
        });
    });
}
