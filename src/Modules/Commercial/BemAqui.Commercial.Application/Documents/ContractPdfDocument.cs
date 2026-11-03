using BemAqui.BuildingBlocks.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Commercial.Application.Documents;

/// <summary>
/// Resumo do contrato no modelo único (EXP-04 CA 4 e 6): dados do cliente e do projeto, itens com preço, marcos de pagamento com a situação da
/// parcela, aditivos aprovados e as assinaturas "Contratante" e "Contratada". Cancelado com a marca e o motivo; implantação com a faixa.
/// </summary>
public sealed class ContractPdfDocument : PdfDocumentFrame
{
    private readonly ContractPdfModel _model;

    public ContractPdfDocument(PdfFrame frame, ContractPdfModel model)
        : base(frame, Header(model)) => _model = model;

    private static PdfDocumentHeader Header(ContractPdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var (mark, notice) = ContractPdf.Mark(model.Status, model.IsOnboarding, model.CancelReason);
        return new PdfDocumentHeader("Contrato", model.Number, model.SignedAt, mark, Notice: notice);
    }

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var m = _model;
        col.Spacing(8);
        col.Item().Element(c => Fields(c, 3,
            ("Situação", m.StatusLabel),
            ("Cliente", m.PartyLegalName),
            ("CNPJ/CPF", m.PartyDocument),
            ("Endereço do cliente", m.PartyAddress),
            ("Projeto", m.ProjectNumber is null ? null : $"{m.ProjectNumber} · {m.ProjectName}"),
            ("Orçamento de origem", m.QuoteRevision > 0 ? $"{m.QuoteNumber} rev. {m.QuoteRevision}" : m.QuoteNumber),
            ("Assinatura", PdfTheme.Date(m.SignedAt)),
            ("Valor do contrato", PdfTheme.Money(m.TotalValue)),
            ("Valor vigente", m.CurrentValue != m.TotalValue ? PdfTheme.Money(m.CurrentValue) + " (com os aditivos)" : PdfTheme.Money(m.CurrentValue)),
            ("Data prometida", m.PromisedDate is { } p ? PdfTheme.Date(p) : m.DeliveryDays is { } d ? $"{d} dias corridos" : null),
            ("Endereço da obra", m.SiteAddress)));

        col.Item().Element(c => Section(c, "Itens"));
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(22);
                cols.RelativeColumn(5);
                cols.ConstantColumn(56);
                cols.ConstantColumn(80);
                cols.ConstantColumn(80);
            });
            table.Header(h =>
            {
                HeaderCell(h.Cell()).Text("#").SemiBold();
                HeaderCell(h.Cell()).Text("Descrição").SemiBold();
                HeaderCell(h.Cell()).AlignRight().Text("Qtd.").SemiBold();
                HeaderCell(h.Cell()).AlignRight().Text("Preço unit.").SemiBold();
                HeaderCell(h.Cell()).AlignRight().Text("Total").SemiBold();
            });
            foreach (var i in m.Items)
            {
                BodyCell(table.Cell()).Text(i.Sequence.ToString(PdfTheme.PtBr));
                BodyCell(table.Cell()).Text(i.Description);
                BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Quantity(i.Quantity));
                BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Money(i.UnitPrice));
                BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Money(i.Total));
            }
        });

        col.Item().Element(c => Section(c, "Marcos de pagamento"));
        col.Item().Element(c => ContractPdfTables.Milestones(c, m.Milestones, withParcel: true));

        if (m.Amendments.Count > 0)
        {
            col.Item().Element(c => Section(c, "Aditivos aprovados"));
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(56);
                    cols.ConstantColumn(64);
                    cols.RelativeColumn(4);
                    cols.ConstantColumn(80);
                    cols.ConstantColumn(56);
                });
                table.Header(h =>
                {
                    HeaderCell(h.Cell()).Text("Aditivo").SemiBold();
                    HeaderCell(h.Cell()).Text("Aprovado em").SemiBold();
                    HeaderCell(h.Cell()).Text("Descrição").SemiBold();
                    HeaderCell(h.Cell()).AlignRight().Text("Valor").SemiBold();
                    HeaderCell(h.Cell()).AlignRight().Text("Prazo").SemiBold();
                });
                foreach (var a in m.Amendments)
                {
                    BodyCell(table.Cell()).Text("Aditivo " + a.Number.ToString(PdfTheme.PtBr));
                    BodyCell(table.Cell()).Text(a.ApprovedOn is { } on ? PdfTheme.Date(on) : "—");
                    BodyCell(table.Cell()).Text(a.Description);
                    BodyCell(table.Cell()).AlignRight().Text(ContractPdfTables.Signed(a.ValueDelta));
                    BodyCell(table.Cell()).AlignRight().Text(ContractPdfTables.Days(a.DaysDelta));
                }
            });
        }

        col.Item().PaddingTop(28).Row(row =>
        {
            row.RelativeItem().Element(e => Signature(e, "Contratante: " + m.PartyLegalName));
            row.ConstantItem(24);
            row.RelativeItem().Element(e => Signature(e, "Contratada: " + Frame.Company.LegalName));
        });
    });
}

internal static class ContractPdfTables
{
    public static string Signed(decimal value) => value > 0 ? "+" + PdfTheme.Money(value) : PdfTheme.Money(value);

    public static string Days(int days) => days switch
    {
        0 => "Sem mudança",
        > 0 => $"+{days} dias",
        _ => $"{days} dias",
    };

    public static void Milestones(IContainer c, IReadOnlyList<ContractPdfMilestone> milestones, bool withParcel) => c.Table(table =>
    {
        table.ColumnsDefinition(cols =>
        {
            cols.ConstantColumn(22);
            cols.RelativeColumn(4);
            cols.ConstantColumn(48);
            cols.ConstantColumn(80);
            cols.RelativeColumn(3);
            if (withParcel)
            {
                cols.RelativeColumn(2);
            }
        });
        table.Header(h =>
        {
            Head(h.Cell()).Text("#").SemiBold();
            Head(h.Cell()).Text("Marco").SemiBold();
            Head(h.Cell()).AlignRight().Text("%").SemiBold();
            Head(h.Cell()).AlignRight().Text("Valor").SemiBold();
            Head(h.Cell()).Text("Prazo").SemiBold();
            if (withParcel)
            {
                Head(h.Cell()).Text("Parcela").SemiBold();
            }
        });
        foreach (var m in milestones)
        {
            Body(table.Cell()).Text(m.Sequence.ToString(PdfTheme.PtBr));
            Body(table.Cell()).Text(m.Description);
            Body(table.Cell()).AlignRight().Text(m.Percent.ToString("0.##", PdfTheme.PtBr) + "%");
            Body(table.Cell()).AlignRight().Text(PdfTheme.Money(m.Amount));
            Body(table.Cell()).Text(m.Due);
            if (withParcel)
            {
                Body(table.Cell()).Text(m.Parcel);
            }
        }
    });

    private static IContainer Head(IContainer c) => c.Background(PdfTheme.Surface).BorderBottom(1).BorderColor(PdfTheme.Line).PaddingVertical(4).PaddingHorizontal(3);

    private static IContainer Body(IContainer c) => c.BorderBottom(0.5f).BorderColor(PdfTheme.Surface).PaddingVertical(3).PaddingHorizontal(3);
}
