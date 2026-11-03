using System.Globalization;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Purchasing.Contracts;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Purchasing.Application.Documents;

/// <summary>
/// Layout do pedido de compra no modelo único (ADR 0045 §1, EXP-03 CA 4): o mesmo conteúdo da CMP-03b (fornecedor, condição, previsão, local de
/// entrega, linhas na unidade do fornecedor, totais e observação), com o cabeçalho, a faixa de título e o rodapé do frame. "Rascunho" antes de
/// aprovado, "Cancelado" se cancelado (CA 5). Um dos três arquivos do Purchasing que conhecem o QuestPDF (ADR 0022).
/// </summary>
public sealed class PurchaseOrderPdfDocument : PdfDocumentFrame
{
    private static readonly CultureInfo PtBr = PdfTheme.PtBr;

    private static readonly string[] Columns = ["#", "Código", "Descrição", "Qtd.", "Un.", "Preço unit.", "Total"];

    private readonly PurchaseOrderPdfModel _model;

    public PurchaseOrderPdfDocument(PdfFrame frame, PurchaseOrderPdfModel model)
        : base(frame, Header(model)) => _model = model;

    private static PdfDocumentHeader Header(PurchaseOrderPdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new PdfDocumentHeader("Pedido de compra", model.Number, model.IssueDate, PurchaseOrderPdf.Mark(model.Status), Notice: PurchaseOrderPdf.Notice(model.Status));
    }

    /// <summary>Preço unitário com 2 a 4 casas (o preço guardado tem até 6; a unidade do fornecedor pode precisar de mais de 2).</summary>
    private static string Price(decimal v) => "R$ " + Math.Round(v, 4, MidpointRounding.AwayFromZero).ToString("#,##0.00##", PtBr);

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var model = _model;
        col.Spacing(10);
        col.Item().Background(PdfTheme.Surface).Padding(8).Column(info =>
        {
            info.Item().Text("Fornecedor: " + model.Supplier.Name).SemiBold();
            if (!string.Equals(model.Supplier.LegalName, model.Supplier.Name, StringComparison.Ordinal))
            {
                info.Item().Text("Razão social: " + model.Supplier.LegalName);
            }

            if (model.Supplier.Document is { } doc)
            {
                info.Item().Text("CNPJ/CPF: " + doc);
            }

            info.Item().Text("Condição de pagamento: " + (model.PaymentTerm ?? "a combinar"));
            info.Item().Text("Previsão de entrega: " + PdfTheme.Date(model.ExpectedDate));
            info.Item().Text("Local de entrega: " + (model.DeliveryAddress ?? "a combinar"));
        });
        col.Item().Element(Lines);
        col.Item().AlignRight().Width(220).Element(Totals);
        if (!string.IsNullOrWhiteSpace(model.Notes))
        {
            col.Item().Text("Observações").SemiBold();
            col.Item().Text(model.Notes);
        }
    });

    private void Lines(IContainer c) => c.Table(table =>
    {
        table.ColumnsDefinition(cols =>
        {
            cols.ConstantColumn(22);
            cols.ConstantColumn(70);
            cols.RelativeColumn(5);
            cols.ConstantColumn(52);
            cols.ConstantColumn(30);
            cols.ConstantColumn(72);
            cols.ConstantColumn(76);
        });
        table.Header(h =>
        {
            for (var i = 0; i < Columns.Length; i++)
            {
                var cell = HeaderCell(h.Cell());
                (i is 3 or >= 5 ? cell.AlignRight() : cell).Text(Columns[i]).SemiBold();
            }
        });
        foreach (var l in _model.Lines)
        {
            BodyCell(table.Cell()).Text(l.Sequence.ToString(PtBr));
            BodyCell(table.Cell()).Column(code =>
            {
                code.Item().Text(l.ItemCode);
                if (l.SupplierCode is { } supplierCode)
                {
                    code.Item().Text("Forn.: " + supplierCode).FontSize(PdfTheme.SmallFontSize);
                }
            });
            BodyCell(table.Cell()).Text(l.Description);
            BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Quantity(l.Quantity));
            BodyCell(table.Cell()).PaddingLeft(4).Text(l.Unit);
            BodyCell(table.Cell()).AlignRight().Text(Price(l.UnitPrice));
            BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Money(l.Total));
        }
    });

    private void Totals(IContainer c) => c.Table(table =>
    {
        table.ColumnsDefinition(cols =>
        {
            cols.RelativeColumn();
            cols.ConstantColumn(96);
        });
        void Row(string label, string value, bool strong = false)
        {
            var left = table.Cell().PaddingVertical(2).Text(label);
            var right = table.Cell().PaddingVertical(2).AlignRight().Text(value);
            if (strong)
            {
                left.FontSize(12).Bold();
                right.FontSize(12).Bold();
            }
        }

        Row("Subtotal", PdfTheme.Money(_model.Subtotal));
        Row("Frete", PdfTheme.Money(_model.Freight));
        Row($"IPI ({_model.IpiPct.ToString("0.##", PtBr)}%)", PdfTheme.Money(_model.IpiAmount));
        Row("Total", PdfTheme.Money(_model.Total), strong: true);
    });
}

/// <summary>Marca do PDF do pedido por situação (EXP-03 CA 5), sem banco.</summary>
public static class PurchaseOrderPdf
{
    public const string DraftNotice = "Pedido não aprovado: não envie ao fornecedor.";

    /// <summary>Rascunho e aguardando aprovação saem com "Rascunho"; cancelado com "Cancelado"; do aprovado em diante, sem marca.</summary>
    public static PdfMark Mark(PurchaseOrderStatus status) => status switch
    {
        PurchaseOrderStatus.Draft or PurchaseOrderStatus.PendingApproval => PdfMark.Draft,
        PurchaseOrderStatus.Cancelled => PdfMark.Cancelled,
        _ => PdfMark.None,
    };

    public static string? Notice(PurchaseOrderStatus status) => Mark(status) == PdfMark.Draft ? DraftNotice : null;
}
