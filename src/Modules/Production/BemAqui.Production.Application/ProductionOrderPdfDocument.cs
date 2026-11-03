using BemAqui.BuildingBlocks.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Production.Application;

/// <summary>
/// Ficha da OS de fabricação no modelo único (EXP-03 CA 10 a 15): dados da OS com o indicador de etapa impresso e o QR da OS; as etapas com
/// centro, horas, previsão, situação, checklist (caixa em branco, ou marcada na etapa concluída) e o QR da etapa para o operador; os
/// materiais da aba Material; os desenhos e documentos só listados; o "Visto do supervisor". Sem custo, preço nem margem.
/// </summary>
public sealed class ProductionOrderPdfDocument : PdfDocumentFrame
{
    private const float OrderQr = 62;
    private const float StepQr = 46;

    private readonly ProductionOrderPdfModel _model;

    public ProductionOrderPdfDocument(PdfFrame frame, ProductionOrderPdfModel model)
        : base(frame, Header(model)) => _model = model;

    private static PdfDocumentHeader Header(ProductionOrderPdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var (mark, notice) = ProductionOrderPdf.Mark(model.Status, model.Started, model.CancelReason);
        return new PdfDocumentHeader("Ordem de serviço de fabricação", model.Number, model.IssueDate, mark, Notice: notice);
    }

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var m = _model;
        col.Spacing(8);
        col.Item().Row(row =>
        {
            row.RelativeItem().Column(info =>
            {
                info.Spacing(6);
                info.Item().Element(c => Fields(c, 3,
                    ("Situação", m.StatusLabel),
                    ("Projeto", $"{m.ProjectNumber} · {m.ProjectName}"),
                    ("Cliente", m.PartyName),
                    ("Produto", m.ProductCode is { Length: > 0 } code ? $"{code} · {m.ProductDescription}" : m.ProductDescription),
                    ("Quantidade", PdfTheme.Quantity(m.Quantity)),
                    ("Data prometida", m.PromisedDate is { } p ? PdfTheme.Date(p) : null),
                    ("Fim previsto", m.PlannedEnd),
                    ("Roteiro", m.RoutingName ?? (m.Letters.Count == 0 ? "Sem roteiro" : null)),
                    ("Etapas", m.Letters.Count == 0 ? null : string.Join(" ", m.Letters))));
                if (m.Letters.Count > 0)
                {
                    info.Item().Row(r =>
                    {
                        r.AutoItem().AlignMiddle().PaddingRight(6).Text("Etapa atual").FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted);
                        r.AutoItem().Element(e => PdfStageIndicator.Compose(e, m.Letters, m.CurrentIndex));
                    });
                }
            });
            row.ConstantItem(12);
            row.ConstantItem(OrderQr + 8).Column(qr =>
            {
                qr.Item().Element(e => PdfQr.Compose(e, m.OrderUrl, OrderQr));
                qr.Item().Text("Abrir a OS").FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted);
            });
        });
        col.Item().Element(c => Section(c, "Etapas"));
        col.Item().Element(Steps);
        col.Item().Element(c => Section(c, "Materiais"));
        col.Item().Element(Materials);
        col.Item().Element(c => Section(c, "Desenhos e documentos"));
        col.Item().Element(Files);
        col.Item().PaddingTop(24).Row(row =>
        {
            row.RelativeItem().Element(e => Signature(e, "Visto do supervisor (nome e assinatura)"));
            row.ConstantItem(24);
            row.RelativeItem().Element(e => Signature(e, "Data"));
        });
    });

    private void Steps(IContainer c) => c.Column(col =>
    {
        if (_model.Steps.Count == 0)
        {
            col.Item().Text("Sem roteiro: a OS ainda não tem etapas.").FontColor(PdfTheme.Muted);
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
                        s.WorkCenter ?? "Sem centro", PdfTheme.Quantity(s.StandardHours) + " h padrão", s.Forecast is { } f ? "Previsto " + f : null, s.StatusLabel,
                    }.Where(x => x is not null))).FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted);
                    foreach (var item in s.Checklist)
                    {
                        step.Item().PaddingTop(2).Row(check =>
                        {
                            check.ConstantItem(9).Height(9).Border(1).BorderColor(PdfTheme.Ink).AlignCenter().AlignMiddle().Text(s.Done ? "X" : string.Empty).FontSize(7).Bold();
                            check.ConstantItem(5);
                            check.RelativeItem().Text(item);
                        });
                    }
                });
                row.ConstantItem(8);
                row.ConstantItem(StepQr).Element(e => PdfQr.Compose(e, s.Url, StepQr));
            });
        }
    });

    private void Materials(IContainer c)
    {
        var material = _model.Material;
        if (material.Note is { } note)
        {
            c.Text(note).FontColor(PdfTheme.Muted);
            return;
        }

        if (material.Lines.Count == 0)
        {
            c.Text("A BOM liberada não usa material de estoque.").FontColor(PdfTheme.Muted);
            return;
        }

        string[] columns = ["Item", "Necessidade", "Un.", "Reservado", "Baixado", "Falta", "Local"];
        c.Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(4);
                cols.ConstantColumn(60);
                cols.ConstantColumn(28);
                cols.ConstantColumn(56);
                cols.ConstantColumn(52);
                cols.ConstantColumn(48);
                cols.RelativeColumn(2);
            });
            table.Header(h =>
            {
                for (var i = 0; i < columns.Length; i++)
                {
                    var cell = HeaderCell(h.Cell());
                    (i is 1 or 3 or 4 or 5 ? cell.AlignRight() : cell).Text(columns[i]).SemiBold();
                }
            });
            foreach (var l in material.Lines)
            {
                BodyCell(table.Cell()).Text($"{l.ItemCode} · {l.ItemName}");
                BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Quantity(l.Required));
                BodyCell(table.Cell()).PaddingLeft(3).Text(l.Unit);
                BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Quantity(l.Reserved));
                BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Quantity(l.Issued));
                var shortage = BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Quantity(l.Shortage));
                if (l.Shortage > 0)
                {
                    shortage.Bold();
                }

                BodyCell(table.Cell()).Text(l.Locations ?? "—");
            }
        });
    }

    private void Files(IContainer c)
    {
        if (_model.Files.Count == 0)
        {
            c.Text("Nenhum desenho ou documento anexado.").FontColor(PdfTheme.Muted);
            return;
        }

        c.Column(col =>
        {
            col.Item().Text("Os desenhos não vão impressos aqui: abra-os pela aba Documentos da OS (o QR acima leva à OS).").FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted);
            foreach (var f in _model.Files)
            {
                col.Item().Text(t =>
                {
                    t.Span(f.FileName).SemiBold();
                    t.Span($" · {f.Type} · {f.Size} · {PdfTheme.DateTime(f.CreatedAt)[..10]}");
                    if (f.Pending)
                    {
                        t.Span(" · Em verificação").FontColor(PdfTheme.WarnInk);
                    }
                });
            }
        });
    }
}
