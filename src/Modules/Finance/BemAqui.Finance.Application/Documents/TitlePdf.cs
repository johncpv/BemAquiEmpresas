using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Finance.Application.Exports;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Application.Documents;

/// <summary>Baixa na ficha do título (EXP-05 CA 7): data, valor, juros, multa, desconto, conta e se foi estornada.</summary>
public sealed record TitlePdfSettlement(DateOnly Date, decimal Principal, decimal Interest, decimal Fine, decimal Discount, decimal Total, string? BankAccount, bool Reversed, string? Note);

/// <summary>Boleto do título a receber, como a aba Boleto mostra: situação e linha digitável (quando há).</summary>
public sealed record TitlePdfSlip(string Situation, string? DigitableLine, DateOnly DueDate, decimal Amount);

/// <summary>Ordem de pagamento em que o título a pagar entrou: número e situação.</summary>
public sealed record TitlePdfOrder(string Number, string Situation, DateOnly ExpectedDate);

/// <summary>O que a ficha do título a receber mostra (EXP-05 CA 7), lido pelos mesmos serviços da tela.</summary>
public sealed record ReceivablePdfModel(ReceivableDetail Title, string? CostCenter, IReadOnlyList<TitlePdfSettlement> Settlements, TitlePdfSlip? Slip);

/// <summary>O que a ficha do título a pagar mostra (EXP-05 CA 7): retenções, baixas pela OP e as ordens de pagamento.</summary>
public sealed record PayablePdfModel(PayableDetail Title, string? CostCenter, IReadOnlyList<TitlePdfSettlement> Settlements, IReadOnlyList<TitlePdfOrder> Orders);

/// <summary>Rótulos e marcas da ficha do título: os mesmos da tela (<c>receivables.ts</c>, <c>payables.ts</c>).</summary>
public static class TitlePdf
{
    /// <summary>Cancelado → "Cancelado" (com o motivo na faixa); os demais, sem marca.</summary>
    public static PdfMark Mark(bool cancelled) => cancelled ? PdfMark.Cancelled : PdfMark.None;

    /// <summary>Faixa do título: "Previsto: aguarda {marco}" ou "Previsto: vencimento estimado em {data}"; no cancelado, o motivo.</summary>
    public static string? ReceivableNotice(ReceivableDetail r)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (r.Status == ReceivableStatus.Cancelled)
        {
            return string.IsNullOrWhiteSpace(r.CancelReason) ? null : "Motivo: " + r.CancelReason.Trim();
        }

        if (r.Status != ReceivableStatus.Planned)
        {
            return null;
        }

        var awaits = FinanceExports.Awaits(r);
        return awaits is not null ? "Previsto: aguarda " + awaits["Aguarda: ".Length..]
            : r.DueDate is { } due ? "Previsto: vencimento estimado em " + PdfTheme.Date(due) : "Previsto: aguarda o marco do contrato";
    }

    public static string? PayableNotice(PayableDetail p)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (p.Status == PayableStatus.Cancelled)
        {
            return string.IsNullOrWhiteSpace(p.CancelReason) ? null : "Motivo: " + p.CancelReason.Trim();
        }

        if (p.Status != PayableStatus.Planned)
        {
            return null;
        }

        return p.DueDate is { } due ? "Previsto: vencimento estimado em " + PdfTheme.Date(due) : "Previsto: aguarda a data do documento de origem";
    }

    public static string OriginLabel(DocumentOrigin origin) => origin switch
    {
        DocumentOrigin.Manual => "Manual",
        DocumentOrigin.Opening => "Implantação",
        DocumentOrigin.Contract => "Contrato",
        DocumentOrigin.Recurring => "Recorrente",
        DocumentOrigin.Installer => "Instalação",
        DocumentOrigin.Purchase => "Pedido de compra",
        DocumentOrigin.Payroll => "Folha",
        DocumentOrigin.Maintenance => "Manutenção",
        DocumentOrigin.GoodsReceipt => "Recebimento de compra",
        DocumentOrigin.PayrollForecast => "Folha prevista",
        DocumentOrigin.IncomingInvoice => "Nota de entrada",
        var other => other.ToString(),
    };

    public static string PayableMethod(PaymentMethodKind? kind) => kind is { } k ? PayableExport.MethodLabel(k) : "Não informada";
}
