using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Finance.Application.Exports;
using BemAqui.Finance.Contracts;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Finance.Application.Documents;

/// <summary>
/// Ficha do título a receber no modelo único (EXP-05 CA 7): dados, origem, conta e centro de custo, valores, baixas, saldo e boleto.
/// "Cancelado" com o motivo na faixa; previsto com a faixa "Previsto: aguarda …".
/// </summary>
public sealed class ReceivablePdfDocument : PdfDocumentFrame
{
    private readonly ReceivablePdfModel _model;

    public ReceivablePdfDocument(PdfFrame frame, ReceivablePdfModel model)
        : base(frame, Header(model)) => _model = model;

    private static PdfDocumentHeader Header(ReceivablePdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var r = model.Title;
        return new PdfDocumentHeader("Título a receber", r.Number, r.IssueDate, TitlePdf.Mark(r.Status == ReceivableStatus.Cancelled), Notice: TitlePdf.ReceivableNotice(r));
    }

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var r = _model.Title;
        col.Spacing(8);
        col.Item().Element(c => Fields(c, 3,
            ("Cliente", r.PartyName),
            ("Situação", FinanceExports.ReceivableSituation(r)),
            ("Origem", TitlePdf.OriginLabel(r.Origin)),
            ("Descrição", r.Description),
            ("Conta do plano", r.AccountName),
            ("Centro de custo", _model.CostCenter),
            ("Projeto", r.ProjectNumber),
            ("Emissão", PdfTheme.Date(r.IssueDate)),
            ("Vencimento", r.DueDate is { } due ? PdfTheme.Date(due) : FinanceExports.Awaits(r)),
            ("Valor", PdfTheme.Money(r.Amount)),
            ("Recebido", PdfTheme.Money(r.PaidAmount)),
            ("Saldo", PdfTheme.Money(r.Balance))));
        col.Item().Element(c => Section(c, "Baixas"));
        col.Item().Element(c => TitlePdfParts.Settlements(c, _model.Settlements, "Nenhuma baixa registrada."));
        col.Item().Element(c => Section(c, "Boleto"));
        if (_model.Slip is { } slip)
        {
            col.Item().Element(c => Fields(c, 3,
                ("Situação", slip.Situation),
                ("Vencimento do boleto", PdfTheme.Date(slip.DueDate)),
                ("Valor do boleto", PdfTheme.Money(slip.Amount)),
                ("Linha digitável", slip.DigitableLine)));
        }
        else
        {
            col.Item().Text("Sem boleto emitido.").FontColor(PdfTheme.Muted);
        }
    });
}

/// <summary>
/// Ficha do título a pagar no modelo único (EXP-05 CA 7): dados, origem, retenções, baixas pela ordem de pagamento, saldo e as ordens em que
/// o título entrou. O salário de cada funcionário segue a regra da tela (sem a ação, o serviço recusa antes de montar o PDF).
/// </summary>
public sealed class PayablePdfDocument : PdfDocumentFrame
{
    private readonly PayablePdfModel _model;

    public PayablePdfDocument(PdfFrame frame, PayablePdfModel model)
        : base(frame, Header(model)) => _model = model;

    private static PdfDocumentHeader Header(PayablePdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var p = model.Title;
        return new PdfDocumentHeader("Título a pagar", p.Number, p.IssueDate, TitlePdf.Mark(p.Status == PayableStatus.Cancelled), Notice: TitlePdf.PayableNotice(p));
    }

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var p = _model.Title;
        col.Spacing(8);
        col.Item().Element(c => Fields(c, 3,
            ("Favorecido", p.PartyName),
            ("Situação", PayableExport.Situation(p)),
            ("Origem", TitlePdf.OriginLabel(p.Origin)),
            ("Descrição", p.Description),
            ("Conta do plano", p.AccountName),
            ("Centro de custo", _model.CostCenter),
            ("Projeto", p.ProjectNumber),
            ("Forma de pagamento", TitlePdf.PayableMethod(p.PaymentMethod)),
            ("Emissão", PdfTheme.Date(p.IssueDate)),
            ("Vencimento", p.DueDate is { } due ? PdfTheme.Date(due) + (p.IsEstimate ? " (estimado)" : string.Empty) : null)));
        col.Item().Element(c => Section(c, "Valores e retenções"));
        col.Item().Element(c => Fields(c, 4,
            ("Valor", PdfTheme.Money(p.Amount)),
            ("ISS retido", PdfTheme.Money(p.RetentionIss)),
            ("INSS retido", PdfTheme.Money(p.RetentionInss)),
            ("IR retido", PdfTheme.Money(p.RetentionIr)),
            ("PIS/COFINS/CSLL retidos", PdfTheme.Money(p.RetentionPcc)),
            ("Líquido", PdfTheme.Money(p.NetAmount)),
            ("Pago", PdfTheme.Money(p.PaidAmount)),
            ("Saldo", PdfTheme.Money(p.Balance))));
        col.Item().Element(c => Section(c, "Baixas"));
        col.Item().Element(c => TitlePdfParts.Settlements(c, _model.Settlements, "Nenhum pagamento registrado."));
        col.Item().Element(c => Section(c, "Ordem de pagamento"));
        if (_model.Orders.Count == 0)
        {
            col.Item().Text("O título ainda não entrou numa ordem de pagamento.").FontColor(PdfTheme.Muted);
        }
        else
        {
            foreach (var order in _model.Orders)
            {
                col.Item().Text($"{order.Number} · {order.Situation} · data prevista {PdfTheme.Date(order.ExpectedDate)}");
            }
        }
    });
}

/// <summary>Partes comuns das duas fichas.</summary>
internal static class TitlePdfParts
{
    private static readonly string[] Columns = ["Data", "Valor", "Juros", "Multa", "Desconto", "Total", "Conta", "Situação"];

    public static void Settlements(IContainer c, IReadOnlyList<TitlePdfSettlement> settlements, string empty)
    {
        if (settlements.Count == 0)
        {
            c.Text(empty).FontColor(PdfTheme.Muted);
            return;
        }

        c.Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.ConstantColumn(58);
                cols.RelativeColumn();
                cols.RelativeColumn();
                cols.RelativeColumn();
                cols.RelativeColumn();
                cols.RelativeColumn();
                cols.RelativeColumn(2);
                cols.RelativeColumn(1.4f);
            });
            table.Header(h =>
            {
                for (var i = 0; i < Columns.Length; i++)
                {
                    var cell = PdfTableCells.Header(h.Cell());
                    (i is >= 1 and <= 5 ? cell.AlignRight() : cell).Text(Columns[i]).SemiBold();
                }
            });
            foreach (var s in settlements)
            {
                PdfTableCells.Body(table.Cell()).Text(PdfTheme.Date(s.Date));
                PdfTableCells.Body(table.Cell()).AlignRight().Text(PdfTheme.Money(s.Principal));
                PdfTableCells.Body(table.Cell()).AlignRight().Text(PdfTheme.Money(s.Interest));
                PdfTableCells.Body(table.Cell()).AlignRight().Text(PdfTheme.Money(s.Fine));
                PdfTableCells.Body(table.Cell()).AlignRight().Text(PdfTheme.Money(s.Discount));
                PdfTableCells.Body(table.Cell()).AlignRight().Text(PdfTheme.Money(s.Total));
                PdfTableCells.Body(table.Cell()).Text(s.BankAccount ?? "—");
                PdfTableCells.Body(table.Cell()).Column(d =>
                {
                    d.Item().Text(s.Reversed ? "Estornada" : "Valendo");
                    if (s.Note is { Length: > 0 } note)
                    {
                        d.Item().Text(note).FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted);
                    }
                });
            }
        });
    }
}

/// <summary>As células de tabela do frame, para as partes que não herdam dele (mesmos tokens do <see cref="PdfDocumentFrame"/>).</summary>
internal static class PdfTableCells
{
    public static IContainer Header(IContainer c) => c.Background(PdfTheme.Surface).BorderBottom(1).BorderColor(PdfTheme.Line).PaddingVertical(4).PaddingHorizontal(3);

    public static IContainer Body(IContainer c) => c.BorderBottom(0.5f).BorderColor(PdfTheme.Surface).PaddingVertical(3).PaddingHorizontal(3);
}
