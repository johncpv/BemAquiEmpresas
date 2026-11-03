using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Purchasing.Application;
using BemAqui.Purchasing.Application.Documents;
using BemAqui.Purchasing.Application.Exports;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Purchasing.Endpoints;

/// <summary>
/// Exportação das listas de compras e comprovante de recebimento (EXP-04 CA 9 e 15, ADR 0045 §3): <c>GET …/export?format=xlsx</c> com os
/// mesmos filtros do <c>GET</c> da lista e a mesma <c>purchasing.view</c>; o recebimento em PDF com a permissão da tela, sem custo. Teto,
/// auditoria, nome do arquivo e <c>no-store</c> no <see cref="ListExportService"/>.
/// </summary>
internal static partial class PurchasingEndpoints
{
    private static void MapExports(RouteGroupBuilder purchasing)
    {
        purchasing.MapGet("/purchase-requests/export", (string? format, string? show, PurchaseRequestOrigin? origin, PurchaseRequestStatus? status, Guid? projectId, Guid? itemId,
                Guid? productionOrderId, string? q, HttpContext http, PurchasingExportService service, ListExportService export, CancellationToken ct) =>
                export.ExportAsync(http, format, async (max, c) => Result<ExportData<PurchaseRequestExportRow>>.Success(
                    await service.RequestsAsync(new PurchaseRequestListQuery(show, origin, status, projectId, itemId, productionOrderId, q), max, c)), ct))
            .RequirePermission("purchasing.view").WithName("ExportPurchaseRequests");
        purchasing.MapGet("/rfqs/export", (string? format, string? show, RfqStatus? status, string? q, HttpContext http, PurchasingExportService service, ListExportService export,
                CancellationToken ct) => export.ExportAsync(http, format, async (max, c) => Result<ExportData<RfqExportRow>>.Success(
                    await service.RfqsAsync(new RfqListQuery(show, status, q), max, c)), ct))
            .RequirePermission("purchasing.view").WithName("ExportRfqs");
        purchasing.MapGet("/purchase-orders/export", (string? format, string? show, PurchaseOrderStatus? status, Guid? partyId, Guid? projectId, string? q, HttpContext http,
                PurchasingExportService service, ListExportService export, CancellationToken ct) => export.ExportAsync(http, format, async (max, c) =>
                    Result<ExportData<PurchaseOrderExportRow>>.Success(await service.OrdersAsync(new PurchaseOrderListQuery(show, status, partyId, projectId, q), max, c)), ct))
            .RequirePermission("purchasing.view").WithName("ExportPurchaseOrders");

        var receipts = purchasing.MapGroup("/goods-receipts").RequireModule("inventory");
        receipts.MapGet("/to-receive/export", (string? format, string? q, HttpContext http, PurchasingExportService service, ListExportService export, CancellationToken ct) =>
                export.ExportAsync(http, format, async (max, c) => Result<ExportData<ToReceiveExportRow>>.Success(await service.ToReceiveAsync(q, max, c)), ct))
            .RequirePermission("purchasing.view").WithName("ExportGoodsReceiptsToReceive");
        receipts.MapGet("/export", (string? format, GoodsReceiptStatus? status, Guid? partyId, Guid? purchaseOrderId, string? q, HttpContext http, PurchasingExportService service,
                ListExportService export, CancellationToken ct) => export.ExportAsync(http, format, async (max, c) => Result<ExportData<ReceivedExportRow>>.Success(
                    await service.ReceivedAsync(new GoodsReceiptListQuery(status, partyId, purchaseOrderId, q), max, c)), ct))
            .RequirePermission("purchasing.view").WithName("ExportGoodsReceipts");
        // CA 9: comprovante de recebimento (itens, lote, local, assinaturas), sem custo de entrada, com a permissão da tela.
        receipts.MapGet("/{id:guid}/pdf", async (Guid id, HttpContext http, GoodsReceiptDocumentService service, CancellationToken ct) => Pdf(http, await service.PdfAsync(id, ct)))
            .RequirePermission("purchasing.view").WithName("DownloadGoodsReceiptPdf").Produces(StatusCodes.Status200OK, contentType: "application/pdf");
    }
}
