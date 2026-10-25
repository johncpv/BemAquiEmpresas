using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Inventory.Endpoints;

/// <summary>
/// Ajustes de estoque (EST-01b CA 16): módulo <c>inventory</c> no grupo (403 <c>module_not_released</c>); consultar com
/// <c>inventory.view</c>; lançar e cancelar o próprio com <c>inventory.adjust</c>; aprovar, reprovar e estornar com
/// <c>inventory.approve</c>. Todo comando que cria ajuste ou movimento exige <c>Idempotency-Key</c>. A linha é lida pelo ajuste
/// (IDOR: de outro, 404).
/// </summary>
internal static class AdjustmentEndpoints
{
    private static readonly string[] HistoryChildren = [nameof(StockAdjustmentLine)];

    public static IEndpointRouteBuilder MapAdjustmentEndpoints(this IEndpointRouteBuilder api)
    {
        var adjustments = api.MapGroup("/inventory/adjustments").WithTags("Estoque").RequireModule("inventory");
        adjustments.MapGet(string.Empty, async (StockAdjustmentStatus? status, bool? awaitingMe, StockAdjustmentMode? mode, Guid? locationId, int? page, int? pageSize,
                StockAdjustmentService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new StockAdjustmentListQuery(status, awaitingMe == true, mode, locationId), Page(page, pageSize), ct)))
            .RequirePermission("inventory.view").WithName("ListStockAdjustments").Produces<PagedList<StockAdjustmentSummary>>();
        adjustments.MapGet("/{id:guid}", async (Guid id, StockAdjustmentService service, CancellationToken ct) => (await service.DetailAsync(id, ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("GetStockAdjustment").Produces<StockAdjustmentDetail>();
        adjustments.MapGet("/{id:guid}/lines/{lineId:guid}", async (Guid id, Guid lineId, StockAdjustmentService service, CancellationToken ct) => (await service.LineAsync(id, lineId, ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("GetStockAdjustmentLine").Produces<StockAdjustmentLineView>();
        adjustments.MapGet("/{id:guid}/history", async (Guid id, int? page, int? pageSize, AuditHistory history, CancellationToken ct) =>
                (await history.GetWithChildrenAsync(nameof(StockAdjustment), id.ToString(), HistoryChildren, "adjustmentId", Page(page, pageSize), ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("GetStockAdjustmentHistory").Produces<PagedList<RelatedAuditEntryDto>>();
        adjustments.MapPost(string.Empty, async (StockAdjustmentInput input, StockAdjustmentService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created(StockAdjustmentService.Link + saved.Id, saved)))
            .RequirePermission("inventory.adjust").RequireIdempotencyKey().WithName("CreateStockAdjustment").Produces<StockAdjustmentDetail>(StatusCodes.Status201Created);
        // CA 12: o ajuste não se edita (aprovado: outro ajuste ou o estorno; pendente: cancelar e lançar de novo).
        adjustments.MapPut("/{id:guid}", async (Guid id, StockAdjustmentService service, CancellationToken ct) => (await service.EditAsync(id, ct)).ToHttpResult())
            .RequirePermission("inventory.adjust").WithName("UpdateStockAdjustment").ProducesProblem(StatusCodes.Status409Conflict);
        adjustments.MapPost("/{id:guid}/approve", async (Guid id, StockAdjustmentDecisionInput input, StockAdjustmentService service, CancellationToken ct) => (await service.ApproveAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("inventory.approve").RequireIdempotencyKey().WithName("ApproveStockAdjustment").Produces<StockAdjustmentDetail>();
        adjustments.MapPost("/{id:guid}/reject", async (Guid id, StockAdjustmentDecisionInput input, StockAdjustmentService service, CancellationToken ct) => (await service.RejectAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("inventory.approve").WithName("RejectStockAdjustment").Produces<StockAdjustmentDetail>();
        adjustments.MapPost("/{id:guid}/cancel", async (Guid id, StockAdjustmentReasonInput input, StockAdjustmentService service, CancellationToken ct) => (await service.CancelAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("inventory.adjust").WithName("CancelStockAdjustment").Produces<StockAdjustmentDetail>();
        adjustments.MapPost("/{id:guid}/reverse", async (Guid id, StockAdjustmentReasonInput input, StockAdjustmentService service, CancellationToken ct) => (await service.ReverseAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("inventory.approve").RequireIdempotencyKey().WithName("ReverseStockAdjustment").Produces<StockAdjustmentDetail>();
        return api;
    }

    private static PageRequest Page(int? page, int? pageSize) => new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize();
}
