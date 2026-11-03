using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Purchasing.Contracts;

namespace BemAqui.Purchasing.Application.Documents;

/// <summary>
/// O que o comprovante de recebimento mostra (EXP-04 CA 9): número, data, pedido, fornecedor, nota do fornecedor, itens com lote e local,
/// quem recebeu, a marca do Proprietário único e o estorno. <b>Não existe</b> campo de custo, preço nem valor aqui: o papel vai para o
/// almoxarifado (decisão do PO de 03/11/2026, conferida por teste no record).
/// </summary>
public sealed record GoodsReceiptPdfModel(
    string Number, DateOnly ReceivedOn, string PurchaseOrderNumber, string SupplierName, string? SupplierInvoice, string? ProjectNumber, string? ReceivedByName,
    bool SoleOwner, string? Notes, bool Reversed, string? ReversalReason, DateTimeOffset? ReversedAt, string? ReversedByName, IReadOnlyList<GoodsReceiptPdfLine> Lines);

/// <param name="SupplierQuantity">"10 CX" quando recebido na unidade do fornecedor; null sem conversão.</param>
public sealed record GoodsReceiptPdfLine(int Sequence, string ItemCode, string ItemName, decimal Quantity, string Unit, string? SupplierQuantity, string? Lot, string Location);

/// <summary>Montagem pura do comprovante (EXP-04 CA 9), testada sem banco: marca do estorno e linhas sem custo.</summary>
public static class GoodsReceiptPdf
{
    public const string SoleOwnerNotice = "Recebido pelo Proprietário único";

    /// <summary>Estornado: "Cancelado" com o motivo do estorno; registrado: sem marca.</summary>
    public static (PdfMark Mark, string? Notice) Mark(bool reversed, string? reason) => reversed
        ? (PdfMark.Cancelled, string.IsNullOrWhiteSpace(reason) ? "Recebimento estornado" : "Recebimento estornado: " + reason.Trim())
        : (PdfMark.None, null);

    /// <param name="supplierInvoice">"NF-e 123 série 1", quando o recebimento veio pela nota.</param>
    public static GoodsReceiptPdfModel Build(GoodsReceiptDetail detail, string? supplierInvoice)
    {
        ArgumentNullException.ThrowIfNull(detail);
        var lines = detail.Lines.Select(l => new GoodsReceiptPdfLine(
            l.Sequence, l.ItemCode, l.ItemName, l.Quantity, l.UnitCode,
            l.SupplierUnit is { Length: > 0 } unit && l.SupplierQuantity is { } q ? $"{PdfTheme.Quantity(q)} {unit}" : null, l.LotNumber, l.LocationName)).ToList();
        return new GoodsReceiptPdfModel(
            detail.Number, detail.ReceivedOn, detail.PurchaseOrderNumber, detail.PartyName, supplierInvoice, detail.ProjectNumber, detail.ReceivedByName, detail.SoleOwner,
            detail.Notes, detail.Status == GoodsReceiptStatus.Reversed, detail.ReversalReason, detail.ReversedAt, detail.ReversedByName, lines);
    }
}
