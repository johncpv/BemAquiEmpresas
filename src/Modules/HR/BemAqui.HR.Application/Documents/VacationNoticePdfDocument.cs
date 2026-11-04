using System.Globalization;
using BemAqui.BuildingBlocks.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.HR.Application.Documents;

/// <summary>Aviso de férias (FLH-03 CA 5, CLT art. 135): o funcionário, os períodos, o retorno, o abono e o pagamento, sem valor; a empresa vem do frame.</summary>
public sealed record VacationNoticeModel(
    string EmployeeName,
    string? Registration,
    string Position,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly Start,
    DateOnly End,
    int Days,
    int SoldDays,
    DateOnly PaymentDue,
    DateOnly IssuedOn);

/// <summary>
/// Layout do aviso de férias no modelo único (ADR 0045 §1, EXP-05 CA 19): o mesmo texto da FLH-03, com o cabeçalho, a faixa "Aviso de férias"
/// e o rodapé do frame, e o campo de ciência do funcionário ao lado da assinatura da empresa.
/// </summary>
public sealed class VacationNoticePdfDocument : PdfDocumentFrame
{
    private readonly VacationNoticeModel _model;

    public VacationNoticePdfDocument(PdfFrame frame, VacationNoticeModel model)
        : base(frame, new PdfDocumentHeader("Aviso de férias")) => _model = model ?? throw new ArgumentNullException(nameof(model));

    private static string D(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    protected override void ComposeContent(IContainer container) => container.PaddingVertical(10).Column(col =>
    {
        var model = _model;
        col.Spacing(10);
        col.Item().Text($"Funcionário: {model.EmployeeName}" + (model.Registration is { } reg ? $" (matrícula {reg})" : string.Empty)).SemiBold();
        col.Item().Text("Cargo: " + model.Position);
        col.Item().Text($"Comunicamos que, nos termos do art. 135 da CLT, suas férias referentes ao período aquisitivo de {D(model.PeriodStart)} a {D(model.PeriodEnd)} serão concedidas conforme abaixo.");
        col.Item().Background(PdfTheme.Surface).Padding(10).Column(box =>
        {
            box.Spacing(4);
            box.Item().Text($"Gozo: de {D(model.Start)} a {D(model.End)} ({model.Days} dias).");
            box.Item().Text($"Retorno ao trabalho: {D(model.End.AddDays(1))}.");
            box.Item().Text(model.SoldDays > 0 ? $"Abono pecuniário: {model.SoldDays} dias convertidos em dinheiro." : "Abono pecuniário: não.");
            box.Item().Text($"Pagamento das férias até {D(model.PaymentDue)}.");
        });
        col.Item().Text($"Emitido em {D(model.IssuedOn)}.");
        col.Item().PaddingTop(50).Row(row =>
        {
            row.RelativeItem().Column(c =>
            {
                c.Item().LineHorizontal(1).LineColor(PdfTheme.Line);
                c.Item().AlignCenter().Text(Frame.Company.LegalName);
            });
            row.ConstantItem(40);
            row.RelativeItem().Column(c =>
            {
                c.Item().LineHorizontal(1).LineColor(PdfTheme.Line);
                c.Item().AlignCenter().Text($"Ciente: {model.EmployeeName}");
            });
        });
    });
}
