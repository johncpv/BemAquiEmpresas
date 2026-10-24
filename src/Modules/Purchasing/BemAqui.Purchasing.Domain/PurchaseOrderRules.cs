using System.Globalization;
using BemAqui.Purchasing.Contracts;

namespace BemAqui.Purchasing.Domain;

/// <summary>Totais do pedido recalculados pelo servidor (CA 3): Σ linhas + frete + IPI.</summary>
public sealed record PurchaseOrderTotals(decimal Subtotal, decimal IpiAmount, decimal Total);

/// <summary>Regras puras do pedido de compra (CMP-03a), testadas sem banco.</summary>
public static class PurchaseOrderRules
{
    public const string DocType = "PC";
    public const int MaxLines = 100;
    public const int MaxNotes = 500;
    public const int MaxReason = 500;
    public const int MaxComment = 500;
    public const int ReferenceWindowDays = 30;
    public const decimal MaxQuantity = 99_999_999_999_999.9999m;
    public const decimal MaxUnitPrice = 999_999_999_999.999999m;
    public const decimal MaxMoney = 9_999_999_999_999_999.99m;
    public const decimal MaxIpiPct = 100m;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Situações que contam no valor de referência contra o fracionamento (CA 5): aprovados ou pendentes, nunca rascunho nem cancelado.</summary>
    public static readonly IReadOnlySet<PurchaseOrderStatus> CountsForReference = new HashSet<PurchaseOrderStatus>
    {
        PurchaseOrderStatus.PendingApproval, PurchaseOrderStatus.Approved, PurchaseOrderStatus.Sent, PurchaseOrderStatus.PartiallyReceived, PurchaseOrderStatus.Received,
    };

    /// <summary>Situações com saldo a receber (CA 24).</summary>
    public static readonly IReadOnlySet<PurchaseOrderStatus> OpenForReceipt = new HashSet<PurchaseOrderStatus>
    {
        PurchaseOrderStatus.Approved, PurchaseOrderStatus.Sent, PurchaseOrderStatus.PartiallyReceived,
    };

    /// <summary>Situações que geram o PDF (CMP-03b CA 1): do aprovado em diante; rascunho, aguardando aprovação e cancelado, não.</summary>
    public static readonly IReadOnlySet<PurchaseOrderStatus> PdfStatuses = new HashSet<PurchaseOrderStatus>
    {
        PurchaseOrderStatus.Approved, PurchaseOrderStatus.Sent, PurchaseOrderStatus.PartiallyReceived, PurchaseOrderStatus.Received,
    };

    /// <summary>Situações que podem ser enviadas ou reenviadas ao fornecedor (CMP-03b CA 2 e 4): o aprovado (inclusive o reaprovado) e o enviado.</summary>
    public static readonly IReadOnlySet<PurchaseOrderStatus> Dispatchable = new HashSet<PurchaseOrderStatus> { PurchaseOrderStatus.Approved, PurchaseOrderStatus.Sent };

    /// <summary>Limites do PDF (ADR 0022, CMP-03b CA 8): até 300 linhas e observação até 2.000 caracteres.</summary>
    public const int PdfMaxLines = 300;
    public const int PdfMaxNotes = 2000;

    /// <summary>Mensagem do envio ao fornecedor (CMP-03b CA 2): texto puro, até 1.000 caracteres.</summary>
    public const int MaxDispatchMessage = 1000;

    /// <summary>Até 3 endereços em cópia (CMP-03b CA 2 e 10).</summary>
    public const int MaxDispatchCc = 3;

    /// <summary>
    /// Linha na unidade do fornecedor para o PDF (CMP-03b CA 1): com a unidade do fornecedor cadastrada ("1 {unidade} = {fator} na de
    /// estoque", CAD-02b), a quantidade é dividida pelo fator (4 casas) e o preço multiplicado (6 casas); o total da linha não muda. Sem
    /// unidade do fornecedor (ou fator inválido), fica a unidade de estoque.
    /// </summary>
    public static (decimal Quantity, decimal UnitPrice, string Unit) SupplierLine(decimal quantity, decimal unitPrice, string stockUnit, string? supplierUnit, decimal factor) =>
        string.IsNullOrWhiteSpace(supplierUnit) || factor <= 0m
            ? (quantity, unitPrice, stockUnit)
            : (Math.Round(quantity / factor, 4, MidpointRounding.AwayFromZero), Math.Round(unitPrice * factor, 6, MidpointRounding.AwayFromZero), supplierUnit.Trim());

    /// <summary>Total da linha: quantidade × preço, 2 casas, <c>AwayFromZero</c> (CA 3).</summary>
    public static decimal LineTotal(decimal quantity, decimal unitPrice) => Math.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero);

    /// <summary>Subtotal = Σ linhas; IPI = subtotal × % (2 casas); total = subtotal + frete + IPI (CA 3).</summary>
    public static PurchaseOrderTotals Totals(IEnumerable<(decimal Quantity, decimal UnitPrice)> lines, decimal freight, decimal ipiPct)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var subtotal = lines.Sum(l => LineTotal(l.Quantity, l.UnitPrice));
        var ipi = Math.Round(subtotal * ipiPct / 100m, 2, MidpointRounding.AwayFromZero);
        return new PurchaseOrderTotals(subtotal, ipi, subtotal + freight + ipi);
    }

    /// <summary>
    /// Valor de referência da alçada (CA 5 e 6): o maior entre o total do pedido e a soma dos pedidos não cancelados do mesmo fornecedor
    /// nos últimos 30 dias (aprovados ou pendentes), incluindo este.
    /// </summary>
    public static decimal ReferenceAmount(decimal total, decimal otherOrdersSum30d) => Math.Max(total, otherOrdersSum30d + total);

    /// <summary>Aviso gravado no nível e mostrado na aprovação (CA 6).</summary>
    public static string SplitWarning(int orders, decimal sum) =>
        $"Possível fracionamento: {orders} pedidos para o mesmo fornecedor em 30 dias, {Money(sum)}.";

    /// <summary>Aviso âmbar "1 cotação" (CA 11, D8): só quando o pedido veio de cotação com uma resposta só; não bloqueia.</summary>
    public static string? SingleQuoteWarning(Guid? rfqId, int? quotesCount) =>
        rfqId is not null && quotesCount == 1 ? "1 cotação: este pedido veio de uma cotação com uma resposta só." : null;

    /// <summary>Descrição do título previsto da parcela (CA 12).</summary>
    public static string InstallmentDescription(string number, int installment, int count) => $"Pedido {number} — parcela {installment}/{count}";

    /// <summary>Pode ser editado ou cancelado: rascunho, em aprovação, aprovado ou enviado, sem recebimento (CA 15, 16).</summary>
    public static bool IsChangeable(PurchaseOrderStatus status, decimal receivedQuantity) =>
        receivedQuantity == 0m && status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.PendingApproval or PurchaseOrderStatus.Approved or PurchaseOrderStatus.Sent;

    /// <summary>Edição que volta à aprovação (CA 15): o pedido já saiu do rascunho.</summary>
    public static bool NeedsReapproval(PurchaseOrderStatus status) => status is PurchaseOrderStatus.PendingApproval or PurchaseOrderStatus.Approved or PurchaseOrderStatus.Sent;

    /// <summary>Data prevista padrão (CA 1): hoje + o prazo padrão de compra (ou o prazo da resposta da cotação, CA 2).</summary>
    public static DateOnly DefaultExpectedDate(DateOnly today, int leadDays) => today.AddDays(leadDays);

    /// <summary>
    /// Destino pelas linhas de SC (CA 1, R-CMP-6): o projeto quando todas são do mesmo projeto; senão "Estoque geral" (null).
    /// </summary>
    public static Guid? DestinationOf(IReadOnlyCollection<Guid?> requestLineProjects)
    {
        ArgumentNullException.ThrowIfNull(requestLineProjects);
        var distinct = requestLineProjects.Distinct().ToList();
        return distinct is [{ } only] ? only : null;
    }

    /// <summary>"Atrasado {n} d" (CA 20): previsão antes de hoje e ainda não recebido.</summary>
    public static int? DaysLate(PurchaseOrderStatus status, DateOnly expectedDate, DateOnly today) =>
        OpenForReceipt.Contains(status) && expectedDate < today ? today.DayNumber - expectedDate.DayNumber : null;

    public static string Money(decimal value) => value.ToString("C", PtBr);
}
