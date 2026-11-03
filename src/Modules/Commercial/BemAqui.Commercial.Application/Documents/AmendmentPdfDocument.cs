using BemAqui.BuildingBlocks.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Commercial.Application.Documents;

/// <summary>
/// O aditivo no modelo único (EXP-04 CA 5): contrato de origem, motivo, antes → depois de valor e prazo, marcos do acréscimo, títulos
/// reduzidos, a aprovação e as assinaturas. Rascunho e aguardando aprovação com "Rascunho"; reprovado com "Cancelado".
/// </summary>
public sealed class AmendmentPdfDocument : PdfDocumentFrame
{
    private readonly AmendmentPdfModel _model;

    public AmendmentPdfDocument(PdfFrame frame, AmendmentPdfModel model, DateOnly? date)
        : base(frame, Header(model, date)) => _model = model;

    private static PdfDocumentHeader Header(AmendmentPdfModel model, DateOnly? date)
    {
        ArgumentNullException.ThrowIfNull(model);
        var (mark, notice) = ContractPdf.Mark(model.Status, model.RejectionComment);
        return new PdfDocumentHeader("Aditivo de contrato", $"{model.ContractNumber} · Aditivo {model.Number}", date ?? model.IssueDate, mark, Notice: notice);
    }

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var m = _model;
        col.Spacing(8);
        col.Item().Element(c => Fields(c, 3,
            ("Contrato de origem", m.ContractNumber),
            ("Situação", m.StatusLabel),
            ("Projeto", m.ProjectNumber),
            ("Cliente", m.PartyLegalName),
            ("CNPJ/CPF", m.PartyDocument),
            ("Registrado por", m.CreatedBy)));
        col.Item().Element(c => Section(c, "Motivo e escopo"));
        col.Item().Text(m.Description);

        col.Item().Element(c => Section(c, "Antes e depois"));
        col.Item().Element(c => Fields(c, 3,
            ("Valor do contrato", $"{PdfTheme.Money(m.ValueBefore)} → {PdfTheme.Money(m.ValueAfter)}"),
            ("Variação do valor", ContractPdfTables.Signed(m.ValueAfter - m.ValueBefore)),
            ("Data prometida", m.PromisedBefore is { } b && m.PromisedAfter is { } a ? $"{PdfTheme.Date(b)} → {PdfTheme.Date(a)}" : ContractPdfTables.Days(m.DaysDelta))));

        if (m.Milestones.Count > 0)
        {
            col.Item().Element(c => Section(c, "Recebíveis a acrescentar"));
            col.Item().Element(c => ContractPdfTables.Milestones(c, m.Milestones, withParcel: false));
        }

        if (m.Reductions.Count > 0)
        {
            col.Item().Element(c => Section(c, "Recebíveis futuros ajustados"));
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(3);
                    cols.RelativeColumn(2);
                    cols.RelativeColumn(2);
                });
                table.Header(h =>
                {
                    HeaderCell(h.Cell()).Text("Título").SemiBold();
                    HeaderCell(h.Cell()).AlignRight().Text("Valor atual").SemiBold();
                    HeaderCell(h.Cell()).AlignRight().Text("Novo valor").SemiBold();
                });
                foreach (var r in m.Reductions)
                {
                    BodyCell(table.Cell()).Text(r.Number);
                    BodyCell(table.Cell()).AlignRight().Text(r.CurrentAmount is { } current ? PdfTheme.Money(current) : "—");
                    BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Money(r.NewAmount));
                }
            });
        }

        col.Item().Element(c => Section(c, "Aprovação"));
        col.Item().Text(m.ApprovedAt is { } at
            ? $"Aprovado por {m.ApprovedBy ?? "—"} em {PdfTheme.DateTime(at)}{(m.SelfApproved ? " (pelo próprio autor)" : string.Empty)}."
            : m.RejectionComment is { Length: > 0 } reason ? "Reprovado: " + reason : "Ainda não aprovado.");

        col.Item().PaddingTop(28).Row(row =>
        {
            row.RelativeItem().Element(e => Signature(e, "Contratante: " + m.PartyLegalName));
            row.ConstantItem(24);
            row.RelativeItem().Element(e => Signature(e, "Contratada: " + Frame.Company.LegalName));
        });
    });
}
