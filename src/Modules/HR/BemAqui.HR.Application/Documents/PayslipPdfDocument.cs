using System.Globalization;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.HR.Domain;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.HR.Application.Documents;

/// <summary>
/// Funcionário no holerite (CA 1): nome, matrícula, CPF já mascarado 3+2, cargo, CBO e admissão. Nada de CPF completo, endereço, dados
/// bancários nem dependentes: o modelo nem tem esses campos.
/// </summary>
public sealed record PayslipPdfEmployee(string Name, string? Registration, string MaskedCpf, string Position, string? Cbo, DateOnly AdmissionDate);

public sealed record PayslipPdfLine(string Code, string Description, PayrollItemKind Type, string? Reference, decimal Amount);

/// <summary>Holerite em PDF (RH-02b CA 1 e 3): a empresa vem do frame; o rascunho e a simulação levam a marca padrão (EXP-05 CA 20).</summary>
public sealed record PayslipPdfModel(
    PayslipPdfEmployee Employee,
    DateOnly Period,
    PayrollRunType Type,
    IReadOnlyList<PayslipPdfLine> Lines,
    decimal Gross,
    decimal Deductions,
    decimal Net,
    decimal InssBase,
    decimal FgtsBase,
    decimal IrrfBase,
    decimal FgtsAmount,
    PdfMark Mark = PdfMark.None,
    string? Notice = null);

/// <summary>Marca do holerite (EXP-05 CA 20): folha em rascunho, "Rascunho"; folha calculada antes da virada (FLH-02a CA 15), "Simulação".</summary>
public static class PayslipPdf
{
    public const string DraftNotice = "folha não fechada";
    public const string SimulationNotice = "não é o holerite oficial";

    public static PdfMark Mark(bool draft, bool simulation) => simulation ? PdfMark.Simulation : draft ? PdfMark.Draft : PdfMark.None;

    public static string? Notice(bool draft, bool simulation) => simulation ? SimulationNotice : draft ? DraftNotice : null;
}

/// <summary>
/// Layout do holerite no modelo único (ADR 0045 §1, EXP-05 CA 19): o mesmo conteúdo da RH-02b (funcionário, rubricas, totais, líquido e bases),
/// um holerite por documento, com o cabeçalho, a faixa "Holerite {tipo} competência {MM/AAAA}" e o rodapé do frame.
/// </summary>
public sealed class PayslipPdfDocument : PdfDocumentFrame
{
    private static readonly CultureInfo PtBr = PdfTheme.PtBr;

    private static readonly string[] Columns = ["Código", "Descrição", "Referência", "Proventos", "Descontos"];

    private static readonly string[] BaseTitles = ["Base do INSS", "Base do FGTS", "FGTS do mês", "Base do IRRF"];

    private readonly PayslipPdfModel _model;

    public PayslipPdfDocument(PdfFrame frame, PayslipPdfModel model)
        : base(frame, Header(model)) => _model = model;

    private static PdfDocumentHeader Header(PayslipPdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new PdfDocumentHeader("Holerite " + PayrollRules.TypeLabel(model.Type), "competência " + PayslipDocumentRules.MonthYear(model.Period), null, model.Mark,
            Notice: model.Notice);
    }

    private static string Money(decimal v) => v.ToString("N2", PtBr);

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var model = _model;
        col.Spacing(10);
        col.Item().Background(PdfTheme.Surface).Padding(8).Table(t =>
        {
            t.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(3);
                cols.RelativeColumn(2);
                cols.RelativeColumn(2);
            });
            t.Cell().Text("Funcionário: " + model.Employee.Name).SemiBold();
            t.Cell().Text("Matrícula: " + (model.Employee.Registration ?? "—"));
            t.Cell().Text("CPF: " + model.Employee.MaskedCpf);
            t.Cell().Text("Cargo: " + model.Employee.Position);
            t.Cell().Text("CBO: " + (model.Employee.Cbo ?? "—"));
            t.Cell().Text("Admissão: " + PdfTheme.Date(model.Employee.AdmissionDate));
        });
        col.Item().Element(Lines);
        col.Item().AlignRight().Text(t =>
        {
            t.Span("Total de proventos: ").SemiBold();
            t.Span(Money(model.Gross));
            t.Span("    Total de descontos: ").SemiBold();
            t.Span(Money(model.Deductions));
        });
        col.Item().AlignRight().Background(PdfTheme.Surface).Padding(8).Text("Líquido a receber: " + model.Net.ToString("C2", PtBr)).FontSize(13).Bold();
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(cols =>
            {
                for (var i = 0; i < 4; i++)
                {
                    cols.RelativeColumn();
                }
            });
            foreach (var title in BaseTitles)
            {
                t.Cell().BorderBottom(1).BorderColor(PdfTheme.Line).PaddingVertical(3).Text(title).SemiBold();
            }

            foreach (var value in new[] { model.InssBase, model.FgtsBase, model.FgtsAmount, model.IrrfBase })
            {
                t.Cell().PaddingVertical(3).Text(Money(value));
            }
        });
    });

    private void Lines(IContainer c) => c.Table(table =>
    {
        table.ColumnsDefinition(cols =>
        {
            cols.ConstantColumn(56);
            cols.RelativeColumn(5);
            cols.ConstantColumn(70);
            cols.ConstantColumn(80);
            cols.ConstantColumn(80);
        });
        table.Header(h =>
        {
            foreach (var title in Columns)
            {
                HeaderCell(h.Cell()).Text(title).SemiBold();
            }
        });
        foreach (var l in _model.Lines)
        {
            BodyCell(table.Cell()).Text(l.Code);
            BodyCell(table.Cell()).Text(l.Type == PayrollItemKind.Information ? $"{l.Description} (informativa: {Money(l.Amount)})" : l.Description);
            BodyCell(table.Cell()).Text(l.Reference ?? string.Empty);
            BodyCell(table.Cell()).AlignRight().Text(l.Type == PayrollItemKind.Earning ? Money(l.Amount) : string.Empty);
            BodyCell(table.Cell()).AlignRight().Text(l.Type == PayrollItemKind.Deduction ? Money(l.Amount) : string.Empty);
        }
    });
}
