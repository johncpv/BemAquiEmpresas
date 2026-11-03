using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Production.Domain;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Production.Application;

/// <summary>
/// O que o PDF da inspeção de qualidade mostra (EXP-05 CA 9): OS, projeto, produto, etapa, tentativa, inspetor, data, o checklist com o
/// resultado de cada item, a decisão, a NC aberta quando reprovada e as fotos da NC só listadas (nome e data). Sem custo.
/// </summary>
public sealed record QualityInspectionPdfModel(
    string OrderNumber, string Project, string Product, string Step, int Attempt, string? Inspector, DateTimeOffset? DecidedAt, QualityResult? Result, bool SelfInspection,
    string? NonConformityNumber, string? ReturnToStep, IReadOnlyList<QualityInspectionPdfItem> Items, IReadOnlyList<QualityInspectionPdfPhoto> Photos, DateOnly IssueDate);

public sealed record QualityInspectionPdfItem(string Text, string Result, string? MarkedBy, DateTimeOffset? MarkedAt);

public sealed record QualityInspectionPdfPhoto(string FileName, DateTimeOffset CreatedAt);

/// <summary>Montagem pura do PDF da inspeção (EXP-05 CA 9): "Prévia" enquanto não decidida, os rótulos da tela e o limite de linhas.</summary>
public static class QualityInspectionPdf
{
    public const int MaxLines = 300;
    public const string NotDecidedNotice = "Inspeção ainda não decidida.";

    public static string ResultLabel(ChecklistResult? result) => result switch
    {
        ChecklistResult.Done => "Conforme",
        ChecklistResult.NonConforming => "Não conforme",
        ChecklistResult.NotApplicable => "Não se aplica",
        _ => "—",
    };

    public static string DecisionLabel(QualityResult? result) => result switch
    {
        QualityResult.Approved => "Aprovada",
        QualityResult.Rejected => "Reprovada",
        _ => "Não decidida",
    };

    /// <summary>Não decidida: "Prévia" com o aviso; decidida, sem marca.</summary>
    public static (PdfMark Mark, string? Notice) Mark(QualityResult? result) => result is null ? (PdfMark.Preview, NotDecidedNotice) : (PdfMark.None, null);

    /// <summary>A tentativa da etapa: a pedida, ou a mais recente (a em andamento vem primeiro na aba Qualidade da OS).</summary>
    public static QualityAttemptView? Pick(IReadOnlyList<QualityAttemptView> attempts, Guid stepId, int? attempt)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        var ofStep = attempts.Where(a => a.StepId == stepId).ToList();
        return attempt is { } n ? ofStep.FirstOrDefault(a => a.Attempt == n) : ofStep.FirstOrDefault();
    }

    public static QualityInspectionPdfModel Build(FabricationOrderDetail order, QualityAttemptView attempt, IReadOnlyList<AttachmentDto> photos, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(photos);
        var product = order.ProductCode is { Length: > 0 } code ? $"{code} · {order.ProductName ?? order.ItemDescription}" : order.ProductName ?? order.ItemDescription;
        var inspector = attempt.DecidedByName
            ?? (attempt.Items.Select(i => i.MarkedByName).OfType<string>().Distinct().ToList() is { Count: > 0 } markers ? string.Join(", ", markers) : null);
        var items = attempt.Items.Select(i => new QualityInspectionPdfItem(i.Text, ResultLabel(i.Result), i.MarkedByName, i.MarkedAt)).ToList();
        var listed = photos.Where(p => p.ScanStatus is ScanStatus.Clean or ScanStatus.Pending).Select(p => new QualityInspectionPdfPhoto(p.FileName, p.CreatedAt)).ToList();
        return new QualityInspectionPdfModel(order.Number, $"{order.ProjectNumber} · {order.ProjectName}", product, $"{attempt.StepCode} · {attempt.StepName}", attempt.Attempt, inspector,
            attempt.DecidedAt, attempt.Result, attempt.SelfInspection, attempt.NonConformityNumber, attempt.ReturnToStepName, items, listed, today);
    }

    /// <summary><c>{OS}-qualidade-{letra}-t{tentativa}.pdf</c>.</summary>
    public static string FileName(string orderNumber, string stepCode, int attempt) => $"{orderNumber}-qualidade-{stepCode}-t{attempt}.pdf";
}

/// <summary>Inspeção de qualidade no modelo único (EXP-05 CA 9): dados, checklist com o resultado, decisão, NC e fotos listadas; "Prévia" se não decidida.</summary>
public sealed class QualityInspectionPdfDocument : PdfDocumentFrame
{
    private static readonly string[] Columns = ["Item", "Resultado", "Marcado por"];

    private readonly QualityInspectionPdfModel _model;

    public QualityInspectionPdfDocument(PdfFrame frame, QualityInspectionPdfModel model)
        : base(frame, Header(model)) => _model = model;

    private static PdfDocumentHeader Header(QualityInspectionPdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var (mark, notice) = QualityInspectionPdf.Mark(model.Result);
        return new PdfDocumentHeader("Inspeção de qualidade", $"{model.OrderNumber} · tentativa {model.Attempt}", model.IssueDate, mark, Notice: notice);
    }

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var m = _model;
        col.Spacing(8);
        col.Item().Element(c => Fields(c, 3,
            ("OS", m.OrderNumber),
            ("Projeto", m.Project),
            ("Produto", m.Product),
            ("Etapa", m.Step),
            ("Tentativa", m.Attempt.ToString(PdfTheme.PtBr)),
            ("Inspetor", m.Inspector),
            ("Data", m.DecidedAt is { } at ? PdfTheme.DateTime(at) : null),
            ("Decisão", QualityInspectionPdf.DecisionLabel(m.Result) + (m.SelfInspection ? " · Autoinspeção" : string.Empty)),
            ("NC aberta", m.NonConformityNumber is { } nc ? nc + (m.ReturnToStep is { } back ? $" · volta para {back}" : string.Empty) : null)));
        col.Item().Element(c => Section(c, "Checklist"));
        col.Item().Element(Items);
        col.Item().Element(c => Section(c, "Fotos"));
        col.Item().Element(Photos);
        col.Item().PaddingTop(24).Row(row =>
        {
            row.RelativeItem().Element(e => Signature(e, "Inspetor (nome e assinatura)"));
            row.ConstantItem(24);
            row.RelativeItem().Element(e => Signature(e, "Data"));
        });
    });

    private void Items(IContainer c)
    {
        if (_model.Items.Count == 0)
        {
            c.Text("A etapa não tem checklist.").FontColor(PdfTheme.Muted);
            return;
        }

        c.Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(4);
                cols.ConstantColumn(80);
                cols.RelativeColumn(2);
            });
            table.Header(h =>
            {
                foreach (var title in Columns)
                {
                    HeaderCell(h.Cell()).Text(title).SemiBold();
                }
            });
            foreach (var i in _model.Items)
            {
                BodyCell(table.Cell()).Text(i.Text);
                var result = BodyCell(table.Cell()).Text(i.Result);
                if (i.Result == "Não conforme")
                {
                    result.Bold().FontColor(PdfTheme.CritInk);
                }

                BodyCell(table.Cell()).Text(i.MarkedBy is { } by ? by + (i.MarkedAt is { } at ? " · " + PdfTheme.DateTime(at) : string.Empty) : "—");
            }
        });
    }

    private void Photos(IContainer c)
    {
        if (_model.Photos.Count == 0)
        {
            c.Text("Nenhuma foto anexada.").FontColor(PdfTheme.Muted);
            return;
        }

        c.Column(col =>
        {
            col.Item().Text("As fotos não vão impressas aqui: abra-as pela NC no sistema.").FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted);
            foreach (var p in _model.Photos)
            {
                col.Item().Text(t =>
                {
                    t.Span(p.FileName).SemiBold();
                    t.Span(" · " + PdfTheme.DateTime(p.CreatedAt)[..10]);
                });
            }
        });
    }
}
