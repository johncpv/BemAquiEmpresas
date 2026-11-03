using System.Globalization;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.HR.Domain;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.HR.Application.Documents;

/// <summary>Linha do termo: código, descrição, referência, tipo e valor.</summary>
public sealed record TerminationTermLine(string Code, string Name, string? Reference, PayrollItemKind Type, decimal Amount);

/// <summary>
/// Termo de rescisão para conferência (FLH-05 CA 6 e 7): o funcionário, o tipo, o aviso, as datas, as verbas e os descontos, o líquido, o FGTS
/// e a multa a recolher e o prazo do pagamento; a empresa vem do frame. Nunca é o termo oficial: sai sempre com a marca "Simulação".
/// </summary>
public sealed record TerminationTermModel(
    string EmployeeName,
    string? Registration,
    string Position,
    DateOnly Admission,
    DateOnly TerminationDate,
    string KindLabel,
    string NoticeLabel,
    int NoticeDays,
    IReadOnlyList<TerminationTermLine> Lines,
    decimal Gross,
    decimal Deductions,
    decimal Net,
    decimal Fgts,
    decimal Fine,
    DateOnly PaymentDeadline,
    DateOnly IssuedOn);

/// <summary>
/// Layout do termo de conferência no modelo único (ADR 0045 §1, EXP-05 CA 19 e 20): o mesmo conteúdo da FLH-05, com o cabeçalho e o rodapé do
/// frame. A marca de conferência própria virou a marca padrão "Simulação", com o aviso "o termo oficial é emitido pela contadora" na faixa de
/// todas as páginas.
/// </summary>
public sealed class TerminationTermPdfDocument : PdfDocumentFrame
{
    public const string ConferenceMark = "Documento de conferência interna: o termo oficial é emitido pela contadora.";

    private static readonly CultureInfo PtBr = PdfTheme.PtBr;

    private static readonly string[] Columns = ["Código", "Descrição", "Referência", "Proventos", "Descontos"];

    private readonly TerminationTermModel _model;

    public TerminationTermPdfDocument(PdfFrame frame, TerminationTermModel model)
        : base(frame, new PdfDocumentHeader("Termo de rescisão para conferência", Mark: PdfMark.Simulation, Notice: ConferenceMark)) =>
        _model = model ?? throw new ArgumentNullException(nameof(model));

    private static string D(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string M(decimal v) => v.ToString("N2", PtBr);

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var model = _model;
        col.Spacing(8);
        col.Item().Text($"Funcionário: {model.EmployeeName}" + (model.Registration is { } reg ? $" (matrícula {reg})" : string.Empty)).SemiBold();
        col.Item().Text($"Cargo: {model.Position} · Admissão: {D(model.Admission)} · Desligamento: {D(model.TerminationDate)}");
        col.Item().Text($"Tipo: {model.KindLabel} · Aviso prévio: {model.NoticeLabel} ({model.NoticeDays} dias)");
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(60);
                c.RelativeColumn(4);
                c.RelativeColumn(1.4f);
                c.RelativeColumn(1.4f);
                c.RelativeColumn(1.4f);
            });
            table.Header(h =>
            {
                foreach (var title in Columns)
                {
                    HeaderCell(h.Cell()).Text(title).SemiBold();
                }
            });
            foreach (var line in model.Lines)
            {
                BodyCell(table.Cell()).Text(line.Code);
                BodyCell(table.Cell()).Text(line.Name + (line.Type == PayrollItemKind.Information ? " (informativo)" : string.Empty));
                BodyCell(table.Cell()).Text(line.Reference ?? string.Empty);
                BodyCell(table.Cell()).AlignRight().Text(line.Type == PayrollItemKind.Earning ? M(line.Amount) : line.Type == PayrollItemKind.Information ? $"({M(line.Amount)})" : string.Empty);
                BodyCell(table.Cell()).AlignRight().Text(line.Type == PayrollItemKind.Deduction ? M(line.Amount) : string.Empty);
            }
        });
        col.Item().Background(PdfTheme.Surface).Padding(8).Column(box =>
        {
            box.Spacing(3);
            box.Item().Text($"Total de proventos: R$ {M(model.Gross)} · Total de descontos: R$ {M(model.Deductions)}");
            box.Item().Text($"Líquido a pagar: R$ {M(model.Net)}").SemiBold();
            box.Item().Text($"FGTS a recolher (do mês e rescisório): R$ {M(model.Fgts)} · Multa do FGTS: R$ {M(model.Fine)}");
            box.Item().Text($"Prazo do pagamento: {D(model.PaymentDeadline)} (CLT art. 477 §6º).");
        });
        col.Item().Text($"Emitido em {D(model.IssuedOn)}.");
    });
}
