using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Purchasing.Domain;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Purchasing.Application.Documents;

/// <summary>
/// Mapa comparativo da cotação no modelo único, em paisagem (EXP-03 CA 2 e 3): cartões por fornecedor (custo total, prazo, condição, situação),
/// a tabela item × fornecedor (preço, total da linha e custo com frete e IPI) com o menor custo de cada linha em negrito e com o rótulo "Menor
/// custo" (não depende de cor), o vencedor com o motivo (ou o sugerido, enquanto aberta), a última compra e as assinaturas.
/// </summary>
public sealed class RfqComparisonPdfDocument : PdfDocumentFrame
{
    private readonly RfqComparisonPdfModel _model;

    public RfqComparisonPdfDocument(PdfFrame frame, RfqComparisonPdfModel model)
        : base(frame, Header(model)) => _model = model;

    private static PdfDocumentHeader Header(RfqComparisonPdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var notice = model.Status == RfqStatus.Open ? "Vencedores ainda não gravados: a coluna mostra o sugerido pelo menor custo." : null;
        return new PdfDocumentHeader("Mapa comparativo da cotação", model.Number, model.IssueDate, RfqPdf.ComparisonMark(model.Status), Landscape: true, Notice: notice);
    }

    private bool Open => _model.Status != RfqStatus.Closed;

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var m = _model;
        col.Spacing(8);
        if (m.SingleQuote is { Length: > 0 } single)
        {
            col.Item().Text("1 cotação: " + single).SemiBold().FontColor(PdfTheme.WarnInk);
        }

        if (m.CancelReason is { Length: > 0 } reason)
        {
            col.Item().Text("Motivo do cancelamento: " + reason);
        }

        col.Item().Element(c => Section(c, "Fornecedores"));
        col.Item().Element(Cards);
        col.Item().Element(c => Section(c, "Itens"));
        col.Item().Element(Lines);
        col.Item().PaddingTop(24).Row(row =>
        {
            row.RelativeItem().Element(e => Signature(e, "Comprador (nome e assinatura)"));
            row.ConstantItem(36);
            row.RelativeItem().Element(e => Signature(e, "Aprovação (nome e assinatura)"));
        });
    });

    private void Cards(IContainer c) => c.Table(table =>
    {
        table.ColumnsDefinition(cols =>
        {
            for (var i = 0; i < 4; i++)
            {
                cols.RelativeColumn();
            }
        });
        foreach (var s in _model.Suppliers)
        {
            table.Cell().Padding(3).Border(s.IsCheapest ? 1.5f : 0.75f).BorderColor(s.IsCheapest ? PdfTheme.Ink : PdfTheme.Line).Padding(6).Column(card =>
            {
                card.Item().Text(s.Name).SemiBold();
                card.Item().Text(s.Situation).FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted);
                card.Item().PaddingTop(2).Text("Custo total: " + PdfTheme.Money(s.Total)).Bold();
                card.Item().Text($"Itens {PdfTheme.Money(s.Subtotal)} · frete {PdfTheme.Money(s.Freight)} · IPI {PdfTheme.Money(s.IpiAmount)}").FontSize(PdfTheme.SmallFontSize);
                card.Item().Text("Prazo: " + (s.MaxLeadDays is { } d ? $"{d} dias" : "—")).FontSize(PdfTheme.SmallFontSize);
                card.Item().Text("Condição: " + (s.PaymentTerm ?? "—")).FontSize(PdfTheme.SmallFontSize);
                if (s.IsCheapest)
                {
                    card.Item().Text("Menor custo total").Bold();
                }
            });
        }
    });

    private void Lines(IContainer c) => c.Table(table =>
    {
        var suppliers = _model.Suppliers;
        table.ColumnsDefinition(cols =>
        {
            cols.ConstantColumn(20);
            cols.RelativeColumn(3);
            cols.ConstantColumn(52);
            foreach (var _ in suppliers)
            {
                cols.RelativeColumn(2);
            }

            cols.RelativeColumn(2);
            cols.RelativeColumn(2);
        });
        table.Header(h =>
        {
            HeaderCell(h.Cell()).Text("#").SemiBold();
            HeaderCell(h.Cell()).Text("Item").SemiBold();
            HeaderCell(h.Cell()).AlignRight().Text("Qtd.").SemiBold();
            foreach (var s in suppliers)
            {
                HeaderCell(h.Cell()).AlignRight().Text(s.Name).SemiBold();
            }

            HeaderCell(h.Cell()).Text(Open ? "Sugerido" : "Vencedor").SemiBold();
            HeaderCell(h.Cell()).Text("Última compra").SemiBold();
        });
        foreach (var l in _model.Lines)
        {
            BodyCell(table.Cell()).Text(l.Sequence.ToString(PdfTheme.PtBr));
            BodyCell(table.Cell()).Column(item =>
            {
                item.Item().Text(l.ItemCode).FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted);
                item.Item().Text(l.ItemName);
            });
            BodyCell(table.Cell()).AlignRight().Text($"{PdfTheme.Quantity(l.Quantity)} {l.Unit}");
            foreach (var s in suppliers)
            {
                var cell = l.Cells.FirstOrDefault(x => x.SupplierId == s.Id);
                BodyCell(table.Cell()).AlignRight().Column(prices =>
                {
                    if (cell is null)
                    {
                        prices.Item().AlignRight().Text("Sem preço").FontColor(PdfTheme.Muted);
                        return;
                    }

                    prices.Item().AlignRight().Text(PdfTheme.Money(cell.UnitPrice) + " un.");
                    prices.Item().AlignRight().Text("Total " + PdfTheme.Money(cell.LineTotal)).FontSize(PdfTheme.SmallFontSize);
                    var landed = prices.Item().AlignRight().Text("Com frete e IPI " + PdfTheme.Money(cell.LandedCost)).FontSize(PdfTheme.SmallFontSize);
                    if (cell.IsCheapest)
                    {
                        landed.Bold();
                        prices.Item().AlignRight().Text("Menor custo").Bold();
                    }
                });
            }

            BodyCell(table.Cell()).Column(w =>
            {
                w.Item().Text(l.Winner ?? "—").SemiBold();
                if (l.Reason is { Length: > 0 } reason)
                {
                    w.Item().Text("Motivo: " + reason).FontSize(PdfTheme.SmallFontSize);
                }
            });
            BodyCell(table.Cell()).Text(l.LastPurchase ?? "—").FontSize(PdfTheme.SmallFontSize);
        }
    });
}
