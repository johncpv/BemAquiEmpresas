using BemAqui.BuildingBlocks.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Production.Application;

/// <summary>
/// Não conformidade no modelo único (EXP-04 CA 14): número, situação, OS, projeto, etapa de qualidade, tentativa, idade, responsável, etapa
/// de retorno, descrição, causa, ação, resultado, o checklist da inspeção, as fotos e anexos listados (sem embutir) e o custo estimado só
/// para quem tem <c>projects.view_cost</c>. Aberta ou em ação: faixa "Em aberto".
/// </summary>
public sealed class NonConformityPdfDocument(PdfFrame frame, NonConformityPdfModel model) : PdfDocumentFrame(frame, NonConformityPdf.Header(model))
{
    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var m = model;
        col.Spacing(8);
        var fields = new List<(string, string?)>
        {
            ("Situação", m.StatusLabel),
            ("OS", m.OrderNumber),
            ("Projeto", $"{m.ProjectNumber} · {m.ProjectName}"),
            ("Cliente", m.PartyName),
            ("Etapa de qualidade", m.StepName),
            ("Tentativa", m.Attempt.ToString(PdfTheme.PtBr)),
            ("Idade", m.AgeDays == 1 ? "1 dia" : $"{m.AgeDays} dias"),
            ("Responsável", m.ResponsibleName),
            ("Etapa de retorno", m.ReturnToStepName),
            ("Aberta por", m.OpenedByName is { } by ? $"{by} em {PdfTheme.DateTime(m.OpenedAt)}" : PdfTheme.DateTime(m.OpenedAt)),
            ("Encerrada", m.ClosedAt is { } at ? $"{m.ClosedByName} em {PdfTheme.DateTime(at)}" : null),
        };
        if (m.CostVisible)
        {
            fields.Add(("Custo estimado", m.EstimatedCost is { } cost ? PdfTheme.Money(cost) : null));
        }

        col.Item().Element(c => Fields(c, 3, [.. fields]));
        Text(col, "Descrição", m.Description);
        Text(col, "Causa", m.Cause);
        Text(col, "Ação corretiva", m.Action);
        Text(col, "Resultado", m.Result);
        col.Item().Element(c => Section(c, "Checklist da inspeção"));
        col.Item().Column(list =>
        {
            if (m.Checklist.Count == 0)
            {
                list.Item().Text("A inspeção não tem checklist.").FontColor(PdfTheme.Muted);
            }

            foreach (var item in m.Checklist)
            {
                list.Item().Text(t =>
                {
                    t.Span(item.Text);
                    t.Span(" · " + (item.ResultLabel ?? "Sem marca")).SemiBold();
                });
            }
        });
        col.Item().Element(c => Section(c, "Fotos e anexos"));
        col.Item().Column(list =>
        {
            if (m.Files.Count == 0)
            {
                list.Item().Text("Nenhuma foto ou anexo.").FontColor(PdfTheme.Muted);
            }

            foreach (var f in m.Files)
            {
                list.Item().Text(t =>
                {
                    t.Span(f.FileName).SemiBold();
                    t.Span(" · " + PdfTheme.DateTime(f.CreatedAt)[..10]);
                });
            }
        });
    });

    private static void Text(ColumnDescriptor col, string title, string? value)
    {
        col.Item().Element(c => Section(c, title));
        col.Item().Text(string.IsNullOrWhiteSpace(value) ? "—" : value);
    }
}
