using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Purchasing.Application.Documents;

/// <summary>
/// O que o PDF do pedido mostra (CMP-03b CA 1 e 9): a empresa, o fornecedor, a condição, a previsão e o local de entrega (o endereço da
/// empresa, CAD-06), as linhas na unidade do fornecedor e os totais. Não existe campo de custo médio, margem, projeto ou de outro
/// fornecedor aqui.
/// </summary>
public sealed record PurchaseOrderPdfModel(
    PurchaseOrderPdfCompany Company, string Number, DateOnly IssueDate, PurchaseOrderPdfSupplier Supplier, string? PaymentTerm, DateOnly ExpectedDate, string? DeliveryAddress,
    IReadOnlyList<PurchaseOrderPdfLine> Lines, decimal Subtotal, decimal Freight, decimal IpiPct, decimal IpiAmount, decimal Total, string? Notes);

public sealed record PurchaseOrderPdfCompany(string Name, string LegalName, string? Document, string? Address, string? Contact, byte[]? Logo);

public sealed record PurchaseOrderPdfSupplier(string Name, string LegalName, string? Document);

/// <param name="SupplierCode">Código do item no fornecedor (<c>IItemSupplierCodes</c>), quando houver.</param>
public sealed record PurchaseOrderPdfLine(int Sequence, string ItemCode, string? SupplierCode, string Description, decimal Quantity, string Unit, decimal UnitPrice, decimal Total);

/// <summary>Dados de fora do pedido para montar o PDF: item, código do fornecedor e a empresa (montagem pura, testada sem banco).</summary>
public sealed record PurchaseOrderPdfItem(string Code, string Name, string UnitCode);

public sealed record PurchaseOrderPdfSupplierCode(string? Code, string? Unit, decimal Factor);

/// <summary>Montagem do modelo do PDF (CMP-03b CA 1), pura: conversão para a unidade do fornecedor, totais do pedido e endereço da empresa.</summary>
public static class PurchaseOrderPdfBuilder
{
    public static PurchaseOrderPdfModel Build(
        PurchaseOrder order, IReadOnlyList<PurchaseOrderLine> lines, IReadOnlyDictionary<Guid, PurchaseOrderPdfItem> items, IReadOnlyDictionary<Guid, PurchaseOrderPdfSupplierCode> codes,
        PurchaseOrderPdfCompany company, PurchaseOrderPdfSupplier supplier, string? paymentTerm, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(codes);
        ArgumentNullException.ThrowIfNull(company);
        var rows = lines.OrderBy(l => l.Sequence).Select(l =>
        {
            var item = items.GetValueOrDefault(l.ItemId) ?? new PurchaseOrderPdfItem(string.Empty, "Item removido", "UN");
            var code = codes.GetValueOrDefault(l.ItemId);
            var (quantity, price, unit) = PurchaseOrderRules.SupplierLine(l.Quantity, l.UnitPrice, item.UnitCode, code?.Unit, code?.Factor ?? 0m);
            return new PurchaseOrderPdfLine(l.Sequence, item.Code, string.IsNullOrWhiteSpace(code?.Code) ? null : code.Code.Trim(), item.Name, quantity, unit, price, l.LineTotal);
        }).ToList();
        return new PurchaseOrderPdfModel(
            company, order.Number, today, supplier with { Document = BrazilianDocuments.Format(supplier.Document) is { Length: > 0 } doc ? doc : null }, paymentTerm, order.ExpectedDate,
            company.Address, rows, order.Subtotal, order.Freight, order.IpiPct, order.IpiAmount, order.Total, string.IsNullOrWhiteSpace(order.Notes) ? null : order.Notes.Trim());
    }
}
