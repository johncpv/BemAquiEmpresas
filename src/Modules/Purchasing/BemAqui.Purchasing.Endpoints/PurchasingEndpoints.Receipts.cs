using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Purchasing.Application;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Purchasing.Endpoints;

/// <summary>
/// Recebimento manual contra o pedido (CMP-04). Os grupos de recebimento exigem também o módulo <c>inventory</c> (a entrada no estoque,
/// CA 16). Registrar com <c>purchasing.receive</c> (com <c>Idempotency-Key</c>; <c>purchasing.create</c> não basta); estornar e corrigir
/// preço com <c>purchasing.approve</c>; encerrar o saldo com <c>purchasing.edit</c>; ver com <c>purchasing.view</c>. O recebimento é lido
/// pelo pedido da URL e a linha pelo recebimento da URL (IDOR).
/// </summary>
internal static partial class PurchasingEndpoints
{
    private static readonly string[] ReceiptChildren = [nameof(GoodsReceiptLine)];

    private static void MapReceipts(RouteGroupBuilder purchasing)
    {
        var byOrder = purchasing.MapGroup("/purchase-orders").RequireModule("inventory");
        byOrder.MapGet("/{id:guid}/receipt-defaults", async (Guid id, GoodsReceiptService service, CancellationToken ct) => (await service.DefaultsAsync(id, ct)).ToHttpResult())
            .RequirePermission("purchasing.receive").WithName("GetGoodsReceiptDefaults").Produces<GoodsReceiptDefaults>();
        byOrder.MapPost("/{id:guid}/receipts/preview", async (Guid id, GoodsReceiptInput input, GoodsReceiptService service, CancellationToken ct) =>
                (await service.PreviewAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("purchasing.receive").WithName("PreviewGoodsReceipt").Produces<GoodsReceiptPreview>();
        byOrder.MapPost("/{id:guid}/receipts", async (Guid id, GoodsReceiptInput input, GoodsReceiptService service, CancellationToken ct) =>
                (await service.RegisterAsync(id, input, ct)).ToHttpResult(saved => Results.Created("/api/v1/purchasing/goods-receipts/" + saved.Id, saved)))
            .RequirePermission("purchasing.receive").RequireIdempotencyKey().WithName("RegisterGoodsReceipt").Produces<GoodsReceiptDetail>(StatusCodes.Status201Created);
        byOrder.MapGet("/{id:guid}/receipts", async (Guid id, GoodsReceiptService service, CancellationToken ct) => (await service.ByOrderAsync(id, ct)).ToHttpResult())
            .RequirePermission("purchasing.view").WithName("ListPurchaseOrderReceipts").Produces<IReadOnlyList<GoodsReceiptSummary>>();
        byOrder.MapGet("/{id:guid}/receipts/{receiptId:guid}", async (Guid id, Guid receiptId, GoodsReceiptService service, CancellationToken ct) =>
                (await service.ForOrderAsync(id, receiptId, ct)).ToHttpResult())
            .RequirePermission("purchasing.view").WithName("GetPurchaseOrderReceipt").Produces<GoodsReceiptDetail>();

        var receipts = purchasing.MapGroup("/goods-receipts").RequireModule("inventory");
        receipts.MapGet(string.Empty, async (GoodsReceiptStatus? status, Guid? partyId, Guid? purchaseOrderId, string? q, int? page, int? pageSize, GoodsReceiptService service,
                CancellationToken ct) => Results.Ok(await service.ListAsync(new GoodsReceiptListQuery(status, partyId, purchaseOrderId, q), Page(page, pageSize), ct)))
            .RequirePermission("purchasing.view").WithName("ListGoodsReceipts").Produces<PagedList<GoodsReceiptSummary>>();
        receipts.MapGet("/{id:guid}", async (Guid id, GoodsReceiptService service, CancellationToken ct) => (await service.DetailAsync(id, ct)).ToHttpResult())
            .RequirePermission("purchasing.view").WithName("GetGoodsReceipt").Produces<GoodsReceiptDetail>();
        receipts.MapGet("/{id:guid}/lines/{lineId:guid}", async (Guid id, Guid lineId, GoodsReceiptService service, CancellationToken ct) =>
                (await service.LineAsync(id, lineId, ct)).ToHttpResult())
            .RequirePermission("purchasing.view").WithName("GetGoodsReceiptLine").Produces<GoodsReceiptLineView>();
        receipts.MapGet("/{id:guid}/history", async (Guid id, int? page, int? pageSize, AuditHistory history, CancellationToken ct) =>
                (await history.GetWithChildrenAsync(nameof(GoodsReceipt), id.ToString(), ReceiptChildren, "receiptId", Page(page, pageSize), ct)).ToHttpResult())
            .RequirePermission("purchasing.view").WithName("GetGoodsReceiptHistory").Produces<PagedList<RelatedAuditEntryDto>>();
        receipts.MapGet("/{id:guid}/payables", async (Guid id, GoodsReceiptService service, CancellationToken ct) => (await service.PayablesAsync(id, ct)).ToHttpResult())
            .RequirePermission("purchasing.view").WithName("ListGoodsReceiptPayables").Produces<IReadOnlyList<GoodsReceiptPayableView>>();
        receipts.MapPost("/{id:guid}/reverse", async (Guid id, GoodsReceiptReverseInput input, GoodsReceiptService service, CancellationToken ct) =>
                (await service.ReverseAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("purchasing.approve").WithName("ReverseGoodsReceipt").Produces<GoodsReceiptDetail>();

        // FIS-04b: "Receber pela nota" a partir da NF-e de entrada conferida; exige também os módulos fiscal e inventory. A nota é a da URL
        // (IDOR: nota de outro tenant ou inexistente, 404); registrar com purchasing.receive e Idempotency-Key.
        var byInvoice = purchasing.MapGroup("/incoming-invoices").RequireModule("inventory").RequireModule("fiscal");
        byInvoice.MapGet("/{id:guid}/receipt-defaults", async (Guid id, GoodsReceiptService service, CancellationToken ct) => (await service.InvoiceDefaultsAsync(id, ct)).ToHttpResult())
            .RequirePermission("purchasing.receive").WithName("GetInvoiceReceiptDefaults").Produces<InvoiceReceiptDefaults>();
        byInvoice.MapPost("/{id:guid}/receipt/preview", async (Guid id, InvoiceReceiptInput input, GoodsReceiptService service, CancellationToken ct) =>
                (await service.InvoicePreviewAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("purchasing.receive").WithName("PreviewInvoiceReceipt").Produces<InvoiceReceiptPreview>();
        byInvoice.MapPost("/{id:guid}/receipt", async (Guid id, InvoiceReceiptInput input, GoodsReceiptService service, CancellationToken ct) =>
                (await service.RegisterFromInvoiceAsync(id, input, ct)).ToHttpResult(saved => Results.Created("/api/v1/purchasing/goods-receipts/" + saved.Id, saved)))
            .RequirePermission("purchasing.receive").RequireIdempotencyKey().WithName("RegisterInvoiceReceipt").Produces<GoodsReceiptDetail>(StatusCodes.Status201Created);
        byInvoice.MapPost("/{id:guid}/price-correction-request", async (Guid id, GoodsReceiptService service, CancellationToken ct) =>
                (await service.RequestPriceCorrectionAsync(id, ct)).ToHttpResult())
            .RequirePermission("purchasing.receive").WithName("RequestInvoicePriceCorrection").Produces<PriceCorrectionRequestResult>();

        purchasing.MapGet("/locations", async (GoodsReceiptService service, CancellationToken ct) => Results.Ok(await service.LocationOptionsAsync(ct)))
            .RequirePermission("purchasing.receive").RequireModule("inventory").WithName("ListReceiptLocationOptions").Produces<IReadOnlyList<ReceiptLocationOption>>();
        purchasing.MapGet("/items/{itemId:guid}/last-purchase", async (Guid itemId, GoodsReceiptService service, CancellationToken ct) =>
                (await service.LastPurchaseAsync(itemId, ct)).ToHttpResult())
            .RequirePermission("purchasing.view").WithName("GetItemLastPurchase").Produces<LastPurchaseView>();
    }

    /// <summary>Encerrar o saldo (CA 13, <c>purchasing.edit</c>) e corrigir o preço da linha (CA 14, <c>purchasing.approve</c>), pelo pedido da URL.</summary>
    private static void MapOrderBalance(RouteGroupBuilder orders)
    {
        orders.MapPost("/{id:guid}/close", async (Guid id, PurchaseOrderCloseInput input, PurchaseOrderService service, CancellationToken ct) =>
                (await service.CloseBalanceAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("purchasing.edit").WithName("ClosePurchaseOrderBalance").Produces<PurchaseOrderDetail>();
        orders.MapPost("/{id:guid}/lines/{lineId:guid}/correct-price", async (Guid id, Guid lineId, PurchaseOrderPriceInput input, PurchaseOrderService service, CancellationToken ct) =>
                (await service.CorrectPriceAsync(id, lineId, input, ct)).ToHttpResult())
            .RequirePermission("purchasing.approve").WithName("CorrectPurchaseOrderPrice").Produces<PurchaseOrderDetail>();
    }
}
