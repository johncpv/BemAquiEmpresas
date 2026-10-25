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
/// Inventários (EST-05 CA 14): módulo <c>inventory</c> no grupo (403 <c>module_not_released</c>); consultar e ver a contagem cega com
/// <c>inventory.view</c>; contar com <c>inventory.create</c>; abrir, ver divergências, pedir recontagem, fechar, cancelar e reabrir com
/// <c>inventory.adjust</c>. Abrir e fechar (que gera o ajuste) exigem <c>Idempotency-Key</c>. A linha é lida pelo inventário (IDOR: de
/// outro, 404).
/// </summary>
internal static class InventoryCountEndpoints
{
    private static readonly string[] HistoryChildren = [nameof(InventoryCountLine)];

    public static IEndpointRouteBuilder MapInventoryCountEndpoints(this IEndpointRouteBuilder api)
    {
        var counts = api.MapGroup("/inventory/inventory-counts").WithTags("Estoque").RequireModule("inventory");
        counts.MapGet(string.Empty, async (InventoryCountStatus? status, Guid? locationId, int? page, int? pageSize, InventoryCountService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new InventoryCountListQuery(status, locationId), Page(page, pageSize), ct)))
            .RequirePermission("inventory.view").WithName("ListInventoryCounts").Produces<PagedList<InventoryCountSummary>>();
        counts.MapGet("/{id:guid}", async (Guid id, InventoryCountService service, CancellationToken ct) => (await service.DetailAsync(id, ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("GetInventoryCount").Produces<InventoryCountDetail>();
        counts.MapGet("/{id:guid}/counting", async (Guid id, InventoryCountService service, CancellationToken ct) => (await service.CountingAsync(id, ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("GetInventoryCounting").Produces<InventoryCountingView>();
        counts.MapGet("/{id:guid}/lines/{lineId:guid}", async (Guid id, Guid lineId, InventoryCountService service, CancellationToken ct) => (await service.CountingLineAsync(id, lineId, ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("GetInventoryCountLine").Produces<InventoryCountingLine>();
        counts.MapGet("/{id:guid}/divergences", async (Guid id, UncountedPolicy? uncountedPolicy, InventoryCountService service, CancellationToken ct) =>
                (await service.DivergencesAsync(id, uncountedPolicy, ct)).ToHttpResult())
            .RequirePermission("inventory.adjust").WithName("GetInventoryCountDivergences").Produces<InventoryDivergences>();
        counts.MapGet("/{id:guid}/history", async (Guid id, int? page, int? pageSize, AuditHistory history, CancellationToken ct) =>
                (await history.GetWithChildrenAsync(nameof(InventoryCount), id.ToString(), HistoryChildren, "countId", Page(page, pageSize), ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("GetInventoryCountHistory").Produces<PagedList<RelatedAuditEntryDto>>();
        counts.MapPost(string.Empty, async (OpenInventoryCountInput input, InventoryCountService service, CancellationToken ct) =>
                (await service.OpenAsync(input, ct)).ToHttpResult(saved => Results.Created(InventoryCountService.Link + saved.Id, saved)))
            .RequirePermission("inventory.adjust").RequireIdempotencyKey().WithName("OpenInventoryCount").Produces<InventoryCountDetail>(StatusCodes.Status201Created);
        counts.MapPut("/{id:guid}/lines/{lineId:guid}", async (Guid id, Guid lineId, CountLineInput input, InventoryCountService service, CancellationToken ct) =>
                (await service.CountLineAsync(id, lineId, input, ct)).ToHttpResult())
            .RequirePermission("inventory.create").WithName("CountInventoryLine").Produces<InventoryCountingLine>();
        counts.MapPost("/{id:guid}/lines", async (Guid id, FoundLineInput input, InventoryCountService service, CancellationToken ct) =>
                (await service.AddFoundAsync(id, input, ct)).ToHttpResult(saved => Results.Created(InventoryCountService.Link + id, saved)))
            .RequirePermission("inventory.create").WithName("AddInventoryCountLine").Produces<InventoryCountingLine>(StatusCodes.Status201Created);
        counts.MapPost("/{id:guid}/recount", async (Guid id, RecountInput input, InventoryCountService service, CancellationToken ct) => (await service.RecountAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("inventory.adjust").WithName("RecountInventoryLines").Produces<InventoryDivergences>();
        counts.MapPost("/{id:guid}/close", async (Guid id, CloseInventoryCountInput input, InventoryCountService service, CancellationToken ct) => (await service.CloseAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("inventory.adjust").RequireIdempotencyKey().WithName("CloseInventoryCount").Produces<InventoryCountDetail>();
        counts.MapPost("/{id:guid}/cancel", async (Guid id, CancelInventoryCountInput input, InventoryCountService service, CancellationToken ct) => (await service.CancelAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("inventory.adjust").WithName("CancelInventoryCount").Produces<InventoryCountDetail>();
        counts.MapPost("/{id:guid}/reopen", async (Guid id, ReopenInventoryCountInput input, InventoryCountService service, CancellationToken ct) => (await service.ReopenAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("inventory.adjust").WithName("ReopenInventoryCount").Produces<InventoryCountDetail>();
        return api;
    }

    private static PageRequest Page(int? page, int? pageSize) => new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize();
}
