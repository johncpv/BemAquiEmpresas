using BemAqui.BuildingBlocks.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Purchasing.Application.Documents;

/// <summary>
/// Pedido de cotação para um fornecedor no modelo único (EXP-03 CA 1): os dados da cotação e do fornecedor, os itens na unidade dele e, por
/// item, os campos em branco para o preço, o prazo e a condição; embaixo, frete, IPI, condição de pagamento e validade da proposta em branco e
/// "Resposta por: {comprador}". Sem preço, sem resposta e sem dado de outro fornecedor.
/// </summary>
public sealed class RfqRequestPdfDocument : PdfDocumentFrame
{
    private static readonly string[] Columns = ["#", "Código", "Descrição", "Qtd.", "Un.", "Preço unit.", "Prazo (dias)", "Condição"];

    private static readonly string[] AnswerFields = ["Frete (R$)", "IPI (%)", "Condição de pagamento", "Validade da proposta"];

    private readonly RfqRequestPdfModel _model;

    public RfqRequestPdfDocument(PdfFrame frame, RfqRequestPdfModel model)
        : base(frame, new PdfDocumentHeader("Pedido de cotação", (model ?? throw new ArgumentNullException(nameof(model))).Number, model.IssueDate)) => _model = model;

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var m = _model;
        col.Spacing(10);
        col.Item().Element(c => Fields(c, 2,
            ("Fornecedor", m.Supplier.Name),
            ("Razão social", m.Supplier.LegalName),
            ("CNPJ/CPF", m.Supplier.Document),
            ("Contato", m.Supplier.Contact),
            ("Local de entrega", m.DeliveryAddress ?? "a combinar"),
            ("Resposta por", m.BuyerEmail is { Length: > 0 } email ? $"{m.BuyerName} · {email}" : m.BuyerName)));
        col.Item().Text("Informe, por item, o preço unitário na unidade indicada, o prazo de entrega em dias e a condição. Os itens sem preço contam como não cotados.")
            .FontColor(PdfTheme.Muted);
        col.Item().Element(Lines);
        col.Item().PaddingTop(6).Row(row =>
        {
            foreach (var label in AnswerFields)
            {
                row.RelativeItem().PaddingRight(12).Column(field =>
                {
                    field.Item().Text(label).FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted);
                    field.Item().Height(18).BorderBottom(0.75f).BorderColor(PdfTheme.Line);
                });
            }
        });
        if (m.Notes is { Length: > 0 } notes)
        {
            col.Item().Text("Observações").SemiBold();
            col.Item().Text(notes);
        }

        col.Item().PaddingTop(24).Row(row =>
        {
            row.RelativeItem().Element(e => Signature(e, "Fornecedor (nome e assinatura)"));
            row.ConstantItem(24);
            row.RelativeItem().Element(e => Signature(e, "Data da resposta"));
        });
    });

    private void Lines(IContainer c) => c.Table(table =>
    {
        table.ColumnsDefinition(cols =>
        {
            cols.ConstantColumn(22);
            cols.ConstantColumn(70);
            cols.RelativeColumn(4);
            cols.ConstantColumn(48);
            cols.ConstantColumn(30);
            cols.ConstantColumn(64);
            cols.ConstantColumn(48);
            cols.ConstantColumn(64);
        });
        table.Header(h =>
        {
            for (var i = 0; i < Columns.Length; i++)
            {
                var cell = HeaderCell(h.Cell());
                (i == 3 ? cell.AlignRight() : cell).Text(Columns[i]).SemiBold();
            }
        });
        foreach (var l in _model.Lines)
        {
            BodyCell(table.Cell()).Text(l.Sequence.ToString(PdfTheme.PtBr));
            BodyCell(table.Cell()).Column(code =>
            {
                code.Item().Text(l.SupplierCode ?? l.ItemCode);
                if (l.SupplierCode is not null)
                {
                    code.Item().Text("Nosso: " + l.ItemCode).FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted);
                }
            });
            BodyCell(table.Cell()).Text(l.Description);
            BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Quantity(l.Quantity));
            BodyCell(table.Cell()).PaddingLeft(4).Text(l.Unit);
            for (var blank = 0; blank < 3; blank++)
            {
                // Campo em branco para o fornecedor preencher à mão.
                table.Cell().PaddingVertical(3).PaddingHorizontal(3).AlignBottom().Height(16).BorderBottom(0.75f).BorderColor(PdfTheme.Line);
            }
        }
    });
}
