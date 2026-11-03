using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Finance.Application;

/// <summary>
/// O que o PDF da ordem de pagamento mostra (EXP-01): a ordem, os títulos com o favorecido e o destino do dinheiro (o retrato do envio, já
/// mascarado como na tela), o total, as alçadas e as decisões (quem e quando) e as assinaturas. Nunca a conta de salário em claro.
/// </summary>
public sealed record PaymentOrderPdfModel(PaymentOrderDetail Order, string? BankAccountName);

/// <summary>Ordem de pagamento no modelo único (ADR 0045 §1): "Rascunho" antes de aprovada, "Cancelado" se cancelada ou estornada.</summary>
public sealed class PaymentOrderPdfDocument : PdfDocumentFrame
{
    private static readonly string[] ItemColumns = ["Título", "Favorecido e destino", "Vencimento", "Líquido", "Retenções", "A pagar"];

    private readonly PaymentOrderPdfModel _model;

    public PaymentOrderPdfDocument(PdfFrame frame, PaymentOrderPdfModel model)
        : base(frame, Header(model)) => _model = model;

    private static PdfDocumentHeader Header(PaymentOrderPdfModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var s = model.Order.Summary;
        return new PdfDocumentHeader("Ordem de pagamento", s.Number, s.ExpectedDate, PaymentOrderPdf.Mark(s.Status));
    }

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var order = _model.Order;
        var s = order.Summary;
        col.Spacing(8);
        col.Item().Element(c => Fields(c, 3,
            ("Situação", PaymentOrderPdf.StatusLabel(s.Status)),
            ("Total", PdfTheme.Money(s.Total)),
            ("Títulos", s.Items.ToString(PdfTheme.PtBr)),
            ("Solicitante", s.RequesterName),
            ("Enviada em", PdfTheme.DateTime(s.SubmittedAt)),
            ("Data prevista", PdfTheme.Date(s.ExpectedDate)),
            ("Paga em", order.PaidOn is { } paid ? PdfTheme.Date(paid) : null),
            ("Conta de saída", _model.BankAccountName),
            ("Aprovação", s.SelfApproved ? "Aprovada pelo próprio solicitante" : null)));
        if (order.CancelReason is { Length: > 0 } cancel)
        {
            col.Item().Text("Motivo do cancelamento: " + cancel);
        }

        if (order.ReversalReason is { Length: > 0 } reversal)
        {
            col.Item().Text("Motivo do estorno: " + reversal);
        }

        col.Item().Element(c => Section(c, "Títulos e destino do dinheiro"));
        col.Item().Element(Items);
        col.Item().Element(c => Section(c, "Aprovações"));
        col.Item().Element(Approvals);
        col.Item().PaddingTop(24).Row(row =>
        {
            row.RelativeItem().Element(e => Signature(e, "Solicitado por (nome e assinatura)"));
            row.ConstantItem(24);
            row.RelativeItem().Element(e => Signature(e, "Aprovado por (nome e assinatura)"));
            row.ConstantItem(24);
            row.RelativeItem().Element(e => Signature(e, "Pago por (nome e assinatura)"));
        });
    });

    private void Items(IContainer c) => c.Table(table =>
    {
        table.ColumnsDefinition(cols =>
        {
            cols.ConstantColumn(78);
            cols.RelativeColumn(5);
            cols.ConstantColumn(58);
            cols.ConstantColumn(66);
            cols.ConstantColumn(58);
            cols.ConstantColumn(66);
        });
        table.Header(h =>
        {
            for (var i = 0; i < ItemColumns.Length; i++)
            {
                var cell = HeaderCell(h.Cell());
                (i >= 3 ? cell.AlignRight() : cell).Text(ItemColumns[i]).SemiBold();
            }
        });
        foreach (var item in _model.Order.Items)
        {
            BodyCell(table.Cell()).Text(item.PayableNumber);
            BodyCell(table.Cell()).Column(d =>
            {
                d.Item().Text(item.PartyName).SemiBold();
                d.Item().Text(item.Description).FontSize(PdfTheme.SmallFontSize);
                d.Item().Text("Para: " + PaymentOrderPdf.Destination(item.Payment)).FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted);
                foreach (var warning in item.Warnings)
                {
                    d.Item().Text("Atenção: " + warning).FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.WarnInk);
                }
            });
            BodyCell(table.Cell()).Text(item.DueDate is { } due ? PdfTheme.Date(due) : "—");
            BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Money(item.NetAmount));
            BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Money(item.Retentions));
            BodyCell(table.Cell()).AlignRight().Text(PdfTheme.Money(item.ApprovedTotal));
        }

        table.Cell().ColumnSpan(5).PaddingVertical(4).AlignRight().Text("Total").SemiBold();
        table.Cell().PaddingVertical(4).AlignRight().Text(PdfTheme.Money(_model.Order.Summary.Total)).Bold();
    });

    private void Approvals(IContainer c) => c.Column(col =>
    {
        col.Spacing(3);
        foreach (var level in _model.Order.Levels ?? [])
        {
            col.Item().Text($"Nível {level.Level}: {level.ApproverName} · {level.Status}");
        }

        if (_model.Order.Decisions.Count == 0)
        {
            col.Item().Text("Aguardando a primeira decisão.").FontColor(PdfTheme.Muted);
            return;
        }

        foreach (var d in _model.Order.Decisions)
        {
            col.Item().Text(t =>
            {
                t.Span(PaymentOrderPdf.DecisionLabel(d.Decision)).SemiBold();
                t.Span($" por {d.DecidedByName ?? "—"} em {PdfTheme.DateTime(d.DecidedAt)}");
                if (d.SelfApproved)
                {
                    t.Span(" (pelo próprio solicitante)");
                }

                if (d.Comment is { Length: > 0 } comment)
                {
                    t.Span(" · " + comment).FontColor(PdfTheme.Muted);
                }
            });
        }
    });
}

/// <summary>Rótulos do PDF da OP: os mesmos da tela (<c>payment-orders.ts</c>).</summary>
public static class PaymentOrderPdf
{
    /// <summary>Antes de aprovada (aguardando ou reprovada) é rascunho; cancelada ou estornada, cancelado; aprovada ou paga, sem marca.</summary>
    public static PdfMark Mark(PaymentOrderStatus status) => status switch
    {
        PaymentOrderStatus.Pending or PaymentOrderStatus.Rejected => PdfMark.Draft,
        PaymentOrderStatus.Cancelled or PaymentOrderStatus.Reversed => PdfMark.Cancelled,
        _ => PdfMark.None,
    };

    public static string StatusLabel(PaymentOrderStatus status) => status switch
    {
        PaymentOrderStatus.Pending => "Aguardando aprovação",
        PaymentOrderStatus.Approved => "Aprovada",
        PaymentOrderStatus.Rejected => "Reprovada",
        PaymentOrderStatus.Paid => "Paga",
        PaymentOrderStatus.Cancelled => "Cancelada",
        _ => "Estornada",
    };

    public static string DecisionLabel(PaymentOrderDecision decision) => decision switch
    {
        PaymentOrderDecision.Approved => "Aprovada",
        PaymentOrderDecision.Rejected => "Reprovada",
        _ => "Reenviada",
    };

    /// <summary>Para onde vai o dinheiro, com os dados já mascarados do retrato (igual ao "Para:" da tela).</summary>
    public static string Destination(PaymentDetailsInput p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return p.Kind switch
        {
            PaymentMethodKind.Pix => $"Pix {p.PixKey} · {p.HolderName}",
            PaymentMethodKind.Boleto => $"Boleto {p.Barcode}",
            PaymentMethodKind.EmployeeAccount => $"Conta do funcionário {p.Account} (os dados ficam só no \"Pagar\")",
            PaymentMethodKind.Ted => $"TED banco {p.BankCode} ag. {p.Agency} conta {p.Account} · {p.HolderName}",
            _ => p.Description ?? "Outra forma",
        };
    }
}
