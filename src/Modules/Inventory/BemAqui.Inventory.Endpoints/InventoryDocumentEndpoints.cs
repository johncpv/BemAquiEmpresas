using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Application.Documents;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Inventory.Endpoints;

/// <summary>
/// PDFs e exportação do estoque (EXP-04 CA 10 a 13 e 15, ADR 0045): os documentos com <c>inventory.view</c> (a mesma da tela) e as listas em
/// <c>GET …/export?format=xlsx|pdf</c> com os mesmos filtros do <c>GET</c> da lista. Teto, auditoria, nome e <c>no-store</c> no
/// <see cref="ListExportService"/>; nos documentos, no <see cref="InventoryDocumentService"/>.
/// </summary>
internal static class InventoryDocumentEndpoints
{
    private const string PdfType = "application/pdf";

    public static IEndpointRouteBuilder MapInventoryDocumentEndpoints(this IEndpointRouteBuilder api)
    {
        var inventory = api.MapGroup("/inventory").WithTags("Estoque").RequireModule("inventory");
        inventory.MapGet("/requisitions/{id:guid}/pdf", async (Guid id, HttpContext http, InventoryDocumentService service, CancellationToken ct) => File(http, await service.RequisitionAsync(id, ct)))
            .RequirePermission("inventory.view").WithName("DownloadRequisitionPdf").Produces(StatusCodes.Status200OK, contentType: PdfType);
        inventory.MapGet("/adjustments/{id:guid}/pdf", async (Guid id, HttpContext http, InventoryDocumentService service, CancellationToken ct) => File(http, await service.AdjustmentAsync(id, ct)))
            .RequirePermission("inventory.view").WithName("DownloadStockAdjustmentPdf").Produces(StatusCodes.Status200OK, contentType: PdfType);
        inventory.MapGet("/inventory-counts/{id:guid}/count-sheet/pdf", async (Guid id, HttpContext http, InventoryDocumentService service, CancellationToken ct) =>
                File(http, await service.CountSheetAsync(id, ct)))
            .RequirePermission("inventory.view").WithName("DownloadInventoryCountSheetPdf").Produces(StatusCodes.Status200OK, contentType: PdfType).ProducesProblem(StatusCodes.Status409Conflict);
        inventory.MapGet("/inventory-counts/{id:guid}/pdf", async (Guid id, HttpContext http, InventoryDocumentService service, CancellationToken ct) => File(http, await service.CountResultAsync(id, ct)))
            .RequirePermission("inventory.view").WithName("DownloadInventoryCountResultPdf").Produces(StatusCodes.Status200OK, contentType: PdfType).ProducesProblem(StatusCodes.Status409Conflict);

        inventory.MapGet("/balances/export", (string? format, string? q, Guid? locationId, Guid? categoryId, ItemType? type, StockSituation? situation, HttpContext http,
                InventoryExportService service, ListExportService export, CancellationToken ct) =>
                export.ExportAsync(http, format, async (max, c) => Result<ExportData<BalanceExportRow>>.Success(
                    await service.BalancesAsync(new BalanceListQuery(q, locationId, categoryId, type, situation), max, c)), ct))
            .RequirePermission("inventory.view").WithName("ExportStockBalances");
        inventory.MapGet("/movements/export", (string? format, Guid? itemId, Guid? locationId, StockMovementType? type, DateOnly? from, DateOnly? to, Guid? projectId, Guid? productionOrderId,
                string? sourceType, Guid? sourceId, string? origin, string? number, HttpContext http, InventoryExportService service, ListExportService export, CancellationToken ct) =>
                export.ExportAsync(http, format, async (max, c) => Result<ExportData<MovementRow>>.Success(await service.MovementsAsync(
                    new MovementListQuery(itemId, locationId, type, from, to, projectId, productionOrderId, sourceType, sourceId, origin, number), max, c)), ct))
            .RequirePermission("inventory.view").WithName("ExportStockMovements");
        inventory.MapGet("/adjustments/export", (string? format, StockAdjustmentStatus? status, bool? awaitingMe, StockAdjustmentMode? mode, Guid? locationId, HttpContext http,
                InventoryExportService service, ListExportService export, CancellationToken ct) =>
                export.ExportAsync(http, format, async (max, c) => Result<ExportData<AdjustmentExportRow>>.Success(
                    await service.AdjustmentsAsync(new StockAdjustmentListQuery(status, awaitingMe == true, mode, locationId), max, c)), ct))
            .RequirePermission("inventory.view").WithName("ExportStockAdjustments");
        inventory.MapGet("/requisitions/export", (string? format, MaterialRequisitionStatus? status, bool? awaitingMe, Guid? productionOrderId, MaterialRequisitionKind? kind, HttpContext http,
                InventoryExportService service, ListExportService export, CancellationToken ct) =>
                export.ExportAsync(http, format, async (max, c) => Result<ExportData<RequisitionSummary>>.Success(
                    await service.RequisitionsAsync(new RequisitionListQuery(status, awaitingMe == true, productionOrderId, kind), max, c)), ct))
            .RequirePermission("inventory.view").WithName("ExportMaterialRequisitions");
        inventory.MapGet("/inventory-counts/export", (string? format, InventoryCountStatus? status, Guid? locationId, HttpContext http, InventoryExportService service, ListExportService export,
                CancellationToken ct) =>
                export.ExportAsync(http, format, async (max, c) => Result<ExportData<InventoryCountSummary>>.Success(await service.CountsAsync(new InventoryCountListQuery(status, locationId), max, c)), ct))
            .RequirePermission("inventory.view").WithName("ExportInventoryCounts");
        return api;
    }

    private static IResult File(HttpContext http, Result<InventoryPdfFile> result)
    {
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
        return Results.File(result.Value.Content, PdfType, result.Value.FileName);
    }
}
