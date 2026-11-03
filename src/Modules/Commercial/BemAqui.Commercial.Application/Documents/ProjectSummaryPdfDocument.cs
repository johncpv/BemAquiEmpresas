using BemAqui.BuildingBlocks.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Commercial.Application.Documents;

/// <summary>
/// Resumo do projeto no modelo único (EXP-05 CA 5): dados e o indicador de etapa impresso, itens, OS, cronograma dos marcos e os blocos Custos e
/// Financeiro só quando o modelo os traz (permissão de quem baixa). Cancelado com a marca "Cancelado".
/// </summary>
public sealed class ProjectSummaryPdfDocument : PdfDocumentFrame
{
    private readonly ProjectSummaryPdfModel _model;

    public ProjectSummaryPdfDocument(PdfFrame frame, ProjectSummaryPdfModel model)
        : base(frame, Header(model)) => _model = model;

    private static PdfDocumentHeader Header(ProjectSummaryPdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var (mark, notice) = ProjectSummaryPdf.Mark(model.Status);
        return new PdfDocumentHeader("Resumo do projeto", model.Number, model.IssueDate, mark, Notice: notice);
    }

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var m = _model;
        col.Spacing(8);
        col.Item().Element(c => Fields(c, 3,
            ("Projeto", $"{m.Number} · {m.Name}"),
            ("Cliente", m.Customer),
            ("Responsável", m.Manager),
            ("Obra", m.SiteAddress),
            ("Contrato", m.ContractNumber),
            ("Situação", m.StatusLabel),
            ("Entrega prometida", m.PromisedDate is { } p ? PdfTheme.Date(p) : null),
            ("Entrega prevista", m.ForecastDate is { } f ? PdfTheme.Date(f) : null),
            ("Atraso", m.DelayDays is { } d ? $"{d} {(d == 1 ? "dia" : "dias")}" : "Sem atraso")));
        if (m.StageIndex >= 0)
        {
            col.Item().Row(r =>
            {
                r.AutoItem().AlignMiddle().PaddingRight(6).Text("Etapa").FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted);
                r.AutoItem().Element(e => PdfStageIndicator.Compose(e, ProjectSummaryPdf.Letters, m.StageIndex));
                r.AutoItem().AlignMiddle().PaddingLeft(6).Text(m.StatusLabel);
            });
        }

        col.Item().Element(c => Section(c, "Itens"));
        col.Item().Element(Items);
        if (m.Orders is { } orders)
        {
            col.Item().Element(c => Section(c, "OS de fabricação"));
            col.Item().Element(c => Orders(c, orders));
        }

        col.Item().Element(c => Section(c, "Cronograma (marcos do contrato)"));
        col.Item().Element(Milestones);
        if (m.Costs is { } costs)
        {
            col.Item().Element(c => Section(c, "Custos"));
            col.Item().Element(c => Costs(c, costs));
        }

        if (m.Finance is { } finance)
        {
            col.Item().Element(c => Section(c, "Financeiro"));
            col.Item().Element(c => Fields(c, 3,
                ("A receber", PdfTheme.Money(finance.ToReceive)),
                ("Recebido", PdfTheme.Money(finance.Received)),
                ("% recebido do contratado", PdfTheme.Percent(finance.ReceivedPct))));
        }
    });

    private void Items(IContainer c)
    {
        if (_model.Items.Count == 0)
        {
            c.Text("O projeto não tem itens.").FontColor(PdfTheme.Muted);
            return;
        }

        c.Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(24);
                cols.RelativeColumn();
                cols.ConstantColumn(60);
            });
            table.Header(h =>
            {
                HeaderCell(h.Cell()).Text("#").SemiBold();
                HeaderCell(h.Cell()).Text("Descrição").SemiBold();
                HeaderCell(h.Cell()).AlignRight().Text("Qtd.").SemiBold();
            });
            foreach (var i in _model.Items)
            {
                BodyCell(table.Cell()).Text(i.Sequence.ToString(PdfTheme.PtBr));
                BodyCell(table.Cell()).Text(i.Description);
                BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Quantity(i.Quantity));
            }
        });
    }

    private static void Orders(IContainer c, IReadOnlyList<ProjectSummaryPdfOrder> orders)
    {
        if (orders.Count == 0)
        {
            c.Text("Nenhuma OS de fabricação ainda.").FontColor(PdfTheme.Muted);
            return;
        }

        c.Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(90);
                cols.RelativeColumn(3);
                cols.RelativeColumn(2);
                cols.ConstantColumn(100);
            });
            table.Header(h =>
            {
                HeaderCell(h.Cell()).Text("OS").SemiBold();
                HeaderCell(h.Cell()).Text("Produto").SemiBold();
                HeaderCell(h.Cell()).Text("Etapa atual").SemiBold();
                HeaderCell(h.Cell()).Text("Situação").SemiBold();
            });
            foreach (var o in orders)
            {
                BodyCell(table.Cell()).Text(o.Number);
                BodyCell(table.Cell()).Text(o.Product);
                BodyCell(table.Cell()).Text(o.CurrentStep ?? "—");
                BodyCell(table.Cell()).Text(o.Status);
            }
        });
    }

    private void Milestones(IContainer c)
    {
        if (_model.Milestones.Count == 0)
        {
            c.Text("O contrato não tem marcos.").FontColor(PdfTheme.Muted);
            return;
        }

        c.Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(24);
                cols.RelativeColumn(3);
                cols.ConstantColumn(50);
                cols.ConstantColumn(80);
                cols.RelativeColumn(2);
            });
            table.Header(h =>
            {
                HeaderCell(h.Cell()).Text("#").SemiBold();
                HeaderCell(h.Cell()).Text("Marco").SemiBold();
                HeaderCell(h.Cell()).AlignRight().Text("%").SemiBold();
                HeaderCell(h.Cell()).AlignRight().Text("Valor").SemiBold();
                HeaderCell(h.Cell()).Text("Quando").SemiBold();
            });
            foreach (var m in _model.Milestones)
            {
                BodyCell(table.Cell()).Text(m.Sequence.ToString(PdfTheme.PtBr));
                BodyCell(table.Cell()).Text(m.Description);
                BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Quantity(m.Percent) + "%");
                BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Money(m.Amount));
                BodyCell(table.Cell()).Text(m.When);
            }
        });
    }

    private static void Costs(IContainer c, ProjectSummaryPdfCosts costs) => c.Column(col =>
    {
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(2);
                cols.RelativeColumn();
                cols.RelativeColumn();
                cols.RelativeColumn();
                cols.RelativeColumn();
            });
            table.Header(h =>
            {
                HeaderCell(h.Cell()).Text("Natureza").SemiBold();
                HeaderCell(h.Cell()).AlignRight().Text("Orçado").SemiBold();
                HeaderCell(h.Cell()).AlignRight().Text("Realizado").SemiBold();
                HeaderCell(h.Cell()).AlignRight().Text("Projetado").SemiBold();
                HeaderCell(h.Cell()).AlignRight().Text("Desvio").SemiBold();
            });
            foreach (var l in costs.Lines.Append(costs.Total))
            {
                var total = ReferenceEquals(l, costs.Total);
                void Cell(string text, bool right)
                {
                    var cell = BodyCell(table.Cell());
                    var t = (right ? cell.AlignRight() : cell).Text(text);
                    if (total)
                    {
                        t.SemiBold();
                    }
                }

                Cell(l.Nature, false);
                Cell(PdfTheme.Money(l.Budgeted), true);
                Cell(PdfTheme.Money(l.Realized), true);
                Cell(PdfTheme.Money(l.Projected), true);
                Cell(PdfTheme.Money(l.Deviation), true);
            }
        });
        col.Item().PaddingTop(4).Text(string.Join(" · ", new[]
        {
            "Margem orçada " + Pct(costs.BudgetedMarginPct), "realizada " + Pct(costs.RealizedMarginPct), "projetada " + Pct(costs.ProjectedMarginPct),
        }));
    });

    private static string Pct(decimal? value) => value is { } v ? PdfTheme.Percent(v) : "—";
}
