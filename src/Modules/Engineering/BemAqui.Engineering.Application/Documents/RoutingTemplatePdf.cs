using System.Text;
using BemAqui.BuildingBlocks.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Engineering.Application.Documents;

/// <summary>O que o PDF do roteiro mostra (EXP-05 CA 4): nome, tipo, situação, os produtos ligados e as etapas com o checklist.</summary>
public sealed record RoutingTemplatePdfModel(
    string Name, string? ProductType, bool IsActive, decimal TotalStandardHours, IReadOnlyList<string> Products, IReadOnlyList<RoutingTemplatePdfStep> Steps, DateOnly IssueDate);

public sealed record RoutingTemplatePdfStep(string Code, string Name, string? WorkCenter, decimal StandardHours, bool IsQualityStep, bool RequiresApproval, IReadOnlyList<string> Checklist);

/// <summary>Montagem pura do PDF do roteiro (EXP-05 CA 4): faixa "Roteiro inativo", limite de linhas e nome do arquivo.</summary>
public static class RoutingTemplatePdf
{
    public const int MaxLines = 300;
    public const string InactiveNotice = "Roteiro inativo";

    public static int Lines(RoutingTemplateDetail template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return template.Steps.Count + template.Steps.Sum(s => s.Checklist.Count);
    }

    public static RoutingTemplatePdfModel Build(RoutingTemplateDetail template, IReadOnlyList<RoutingItemView> products, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(products);
        var steps = template.Steps.OrderBy(s => s.Sequence).Select(s => new RoutingTemplatePdfStep(
            s.Code, s.Name, s.WorkCenterName, s.StandardHours, s.IsQualityStep, s.RequiresApproval, s.Checklist.OrderBy(c => c.Sequence).Select(c => c.Text).ToList())).ToList();
        return new RoutingTemplatePdfModel(template.Name, template.ProductType, template.IsActive, template.TotalStandardHours,
            products.Select(p => $"{p.Code} · {p.Name}").ToList(), steps, today);
    }

    /// <summary><c>roteiro-{nome}.pdf</c>, só com letras, dígitos e hífen.</summary>
    public static string FileName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var slug = new StringBuilder();
        foreach (var ch in name.Normalize(NormalizationForm.FormD))
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                slug.Append(char.ToLowerInvariant(ch));
            }
            else if ((char.IsWhiteSpace(ch) || ch is '-' or '_') && slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var text = slug.ToString().Trim('-');
        return $"roteiro-{(text.Length == 0 ? "sem-nome" : text)}.pdf";
    }
}

/// <summary>Roteiro no modelo único (EXP-05 CA 4): as etapas com letra, centro, horas padrão, qualidade e o checklist; inativo leva a faixa.</summary>
public sealed class RoutingTemplatePdfDocument : PdfDocumentFrame
{
    private readonly RoutingTemplatePdfModel _model;

    public RoutingTemplatePdfDocument(PdfFrame frame, RoutingTemplatePdfModel model)
        : base(frame, Header(model)) => _model = model;

    private static PdfDocumentHeader Header(RoutingTemplatePdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new PdfDocumentHeader("Roteiro de fabricação", model.Name, model.IssueDate, Notice: model.IsActive ? null : RoutingTemplatePdf.InactiveNotice);
    }

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var m = _model;
        col.Spacing(8);
        col.Item().Element(c => Fields(c, 3,
            ("Roteiro", m.Name),
            ("Tipo de produto", m.ProductType),
            ("Situação", m.IsActive ? "Ativo" : "Inativo"),
            ("Etapas", m.Steps.Count == 0 ? null : string.Join(" ", m.Steps.Select(s => s.Code))),
            ("Horas padrão", PdfTheme.Quantity(m.TotalStandardHours) + " h")));
        if (m.Steps.Count > 0)
        {
            col.Item().Element(e => PdfStageIndicator.Compose(e, m.Steps.Select(s => s.Code).ToList(), 0));
        }

        col.Item().Element(c => Section(c, "Produtos ligados"));
        col.Item().Text(m.Products.Count == 0 ? "Nenhum produto ligado a este roteiro." : string.Join(" · ", m.Products)).FontColor(m.Products.Count == 0 ? PdfTheme.Muted : PdfTheme.Ink);
        col.Item().Element(c => Section(c, "Etapas"));
        col.Item().Element(Steps);
    });

    private void Steps(IContainer c) => c.Column(col =>
    {
        if (_model.Steps.Count == 0)
        {
            col.Item().Text("O roteiro ainda não tem etapas.").FontColor(PdfTheme.Muted);
            return;
        }

        foreach (var s in _model.Steps)
        {
            col.Item().ShowEntire().BorderBottom(0.5f).BorderColor(PdfTheme.Line).PaddingVertical(5).Row(row =>
            {
                row.ConstantItem(26).Background(PdfTheme.Rail).AlignCenter().AlignMiddle().Text(s.Code).FontSize(12).Bold().FontColor(PdfTheme.Amber);
                row.ConstantItem(8);
                row.RelativeItem().Column(step =>
                {
                    step.Item().Text(t =>
                    {
                        t.Span(s.Name).SemiBold();
                        if (s.IsQualityStep)
                        {
                            t.Span("  · Qualidade").SemiBold();
                        }
                    });
                    step.Item().Text(string.Join(" · ", new[]
                    {
                        s.WorkCenter ?? "Sem centro", PdfTheme.Quantity(s.StandardHours) + " h padrão", s.RequiresApproval ? "Pede aprovação" : null,
                    }.Where(x => x is not null))).FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted);
                    foreach (var item in s.Checklist)
                    {
                        step.Item().PaddingTop(2).Row(check =>
                        {
                            check.ConstantItem(9).Height(9).Border(1).BorderColor(PdfTheme.Ink);
                            check.ConstantItem(5);
                            check.RelativeItem().Text(item);
                        });
                    }
                });
            });
        }
    });
}
