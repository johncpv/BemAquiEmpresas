using System.Globalization;
using System.Text;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Purchasing.Application.Documents;

/// <summary>
/// Pedido de cotação para <b>um</b> fornecedor (EXP-03 CA 1): a cotação, o fornecedor, os itens na unidade dele (com o código dele, quando há),
/// o local de entrega e quem recebe a resposta. Não existe campo de preço, de resposta, de outro fornecedor nem a última compra aqui.
/// </summary>
public sealed record RfqRequestPdfModel(
    string Number, DateOnly IssueDate, RfqRequestPdfSupplier Supplier, IReadOnlyList<RfqRequestPdfLine> Lines, string? DeliveryAddress, string? Notes, string BuyerName,
    string? BuyerEmail);

public sealed record RfqRequestPdfSupplier(string Name, string LegalName, string? Document, string? Contact);

public sealed record RfqRequestPdfLine(int Sequence, string ItemCode, string? SupplierCode, string Description, decimal Quantity, string Unit);

/// <summary>
/// Mapa comparativo (EXP-03 CA 2 e 3): os cartões por fornecedor, a tabela item × fornecedor com o menor custo e o vencedor (ou o sugerido
/// enquanto aberta), a última compra e a situação. Tudo vem do <c>RfqDetail</c> da aba Comparativo: nada é recalculado aqui.
/// </summary>
public sealed record RfqComparisonPdfModel(
    string Number, RfqStatus Status, DateOnly IssueDate, IReadOnlyList<RfqComparisonPdfSupplier> Suppliers, IReadOnlyList<RfqComparisonPdfLine> Lines, string? SingleQuote,
    string? CancelReason);

public sealed record RfqComparisonPdfSupplier(
    Guid Id, string Name, string Situation, decimal Subtotal, decimal Freight, decimal IpiAmount, decimal Total, int? MaxLeadDays, string? PaymentTerm, bool IsCheapest);

public sealed record RfqComparisonPdfCell(Guid SupplierId, decimal UnitPrice, decimal LineTotal, decimal LandedCost, bool IsCheapest);

/// <param name="Winner">O vencedor gravado (concluída) ou o sugerido (aberta).</param>
/// <param name="Reason">O motivo, quando o vencedor não é o de menor custo.</param>
public sealed record RfqComparisonPdfLine(
    int Sequence, string ItemCode, string ItemName, decimal Quantity, string Unit, IReadOnlyList<RfqComparisonPdfCell> Cells, string? Winner, string? Reason, string? LastPurchase);

/// <summary>Montagem pura dos modelos da cotação (EXP-03), testada sem banco.</summary>
public static class RfqPdf
{
    public const string NotOpenCode = "rfq_not_open";
    public const string NoResponseCode = "rfq_without_response";

    /// <summary>Mapa: aberta sai como "Prévia" (vencedores ainda não gravados), concluída sem marca e cancelada com "Cancelado".</summary>
    public static PdfMark ComparisonMark(RfqStatus status) => status switch
    {
        RfqStatus.Open => PdfMark.Preview,
        RfqStatus.Cancelled => PdfMark.Cancelled,
        _ => PdfMark.None,
    };

    public static string ReasonLabel(RfqSelectionReason reason) => reason switch
    {
        RfqSelectionReason.LeadTime => "Prazo",
        RfqSelectionReason.Quality => "Qualidade",
        RfqSelectionReason.PaymentTerm => "Condição de pagamento",
        RfqSelectionReason.ApprovedSupplier => "Fornecedor homologado",
        _ => "Outro",
    };

    /// <summary>
    /// Pedido de cotação de um fornecedor: as linhas na unidade do fornecedor (pela mesma conversão do pedido de compra) e o código dele;
    /// sem preço.
    /// </summary>
    public static RfqRequestPdfModel Request(
        Rfq rfq, IReadOnlyList<RfqLine> lines, IReadOnlyDictionary<Guid, PurchaseOrderPdfItem> items, IReadOnlyDictionary<Guid, PurchaseOrderPdfSupplierCode> codes,
        RfqRequestPdfSupplier supplier, string? deliveryAddress, string buyerName, string? buyerEmail, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(rfq);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(codes);
        ArgumentNullException.ThrowIfNull(supplier);
        var rows = lines.OrderBy(l => l.Sequence).Select(l =>
        {
            var item = items.GetValueOrDefault(l.ItemId) ?? new PurchaseOrderPdfItem(string.Empty, "Item removido", "UN");
            var code = codes.GetValueOrDefault(l.ItemId);
            var (quantity, _, unit) = PurchaseOrderRules.SupplierLine(l.Quantity, 0m, item.UnitCode, code?.Unit, code?.Factor ?? 0m);
            return new RfqRequestPdfLine(l.Sequence, item.Code, string.IsNullOrWhiteSpace(code?.Code) ? null : code.Code.Trim(), item.Name, quantity, unit);
        }).ToList();
        return new RfqRequestPdfModel(rfq.Number, today, supplier with { Document = BrazilianDocuments.Format(supplier.Document) is { Length: > 0 } doc ? doc : null }, rows,
            deliveryAddress, string.IsNullOrWhiteSpace(rfq.Notes) ? null : rfq.Notes.Trim(), buyerName, buyerEmail);
    }

    /// <summary>Mapa comparativo a partir do detalhe da tela (o mesmo cálculo da aba Comparativo).</summary>
    public static RfqComparisonPdfModel Comparison(RfqDetail detail, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(detail);
        var names = detail.Suppliers.ToDictionary(s => s.Id, s => s.PartyName);
        var suppliers = detail.Suppliers.Select(s => new RfqComparisonPdfSupplier(
            s.Id, s.PartyName, Situation(s), s.Subtotal, s.Freight, s.IpiAmount, s.Total, s.MaxLeadDays, s.PaymentTermName, s.IsCheapest)).ToList();
        var lines = detail.Lines.Select(l =>
        {
            var cells = detail.Suppliers.SelectMany(s => s.Quotes.Where(q => q.LineId == l.Id)
                .Select(q => new RfqComparisonPdfCell(s.Id, q.UnitPrice, q.LineTotal, q.LandedCost, q.IsCheapest))).ToList();
            var winnerId = detail.Status == RfqStatus.Closed ? l.SelectedSupplierId : l.CheapestSupplierId;
            var reason = detail.Status == RfqStatus.Closed && l.SelectedSupplierId is { } chosen && chosen != l.CheapestSupplierId && l.SelectionReason is { } r
                ? ReasonLabel(r) + (string.IsNullOrWhiteSpace(l.SelectionNote) ? string.Empty : ": " + l.SelectionNote.Trim())
                : null;
            var last = l.LastPurchase is { } p
                ? $"{PdfTheme.Money(p.UnitPrice)} em {PdfTheme.DateTime(p.ApprovedAt)[..10]} de {p.PartyName}"
                : null;
            return new RfqComparisonPdfLine(l.Sequence, l.ItemCode, l.ItemName, l.Quantity, l.UnitCode, cells, winnerId is { } w ? names.GetValueOrDefault(w) : null, reason, last);
        }).ToList();
        return new RfqComparisonPdfModel(detail.Number, detail.Status, today, suppliers, lines, detail.SingleQuoteWarning, detail.CancelReason);
    }

    private static string Situation(RfqSupplierView s) => s.Declined ? "Não cotou" : s.Responded ? "Respondeu" : "Aguardando resposta";

    /// <summary>Nome do arquivo do pedido de cotação: <c>{COT}-{fornecedor curto}.pdf</c>, sem acento, sem espaço e sem CNPJ.</summary>
    public static string RequestFileName(string number, string supplierName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        var shortName = Slug(supplierName);
        return shortName.Length == 0 ? number + ".pdf" : $"{number}-{shortName}.pdf";
    }

    public static string ComparisonFileName(string number) => number + "-comparativo.pdf";

    /// <summary>As duas primeiras palavras do nome, em minúsculas e sem acento (até 24 caracteres).</summary>
    internal static string Slug(string? name)
    {
        var words = (name ?? string.Empty).Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) && c < 128 ? char.ToLowerInvariant(c) : ' ').ToArray();
        var slug = string.Join('-', new string(words).Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2));
        return slug.Length > 24 ? slug[..24].TrimEnd('-') : slug;
    }
}
