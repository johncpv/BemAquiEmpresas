using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Production.Application;
using BemAqui.Production.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Production.Endpoints;

internal static partial class ProductionEndpoints
{
    private static readonly string[] OrderHistoryChildren = [nameof(ProductionOrderStep), nameof(ProductionOrderChecklistItem)];

    /// <summary>
    /// OS de fabricação (PRD-01): ver com production.view, gerar com production.create, etapas, troca de roteiro e cancelamento
    /// com production.edit (a iniciada pede production.approve no serviço). OS, etapa e anexo conferidos pelo id da OS (IDOR).
    /// </summary>
    private static void MapFabricationOrders(RouteGroupBuilder orders)
    {
        orders.MapGet(string.Empty, async (Guid? projectId, Guid? partyId, string? line, string? situation, bool? late, int? page, int? pageSize, FabricationOrderService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new FabricationListQuery(projectId, partyId, line, situation, late == true), new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)))
            .RequirePermission("production.view").WithName("ListFabricationOrders").Produces<FabricationOrderPage>();
        orders.MapGet("/pending-items", async (Guid? projectId, int? page, int? pageSize, FabricationOrderService service, CancellationToken ct) =>
                Results.Ok(await service.PendingAsync(projectId, new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)))
            .RequirePermission("production.view").WithName("ListPendingFabricationItems").Produces<PendingFabricationPage>();
        orders.MapPost(string.Empty, async (FabricationGenerateInput input, FabricationOrderService service, CancellationToken ct) =>
                (await service.GenerateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/production/production-orders/" + saved.Id, saved)))
            .RequirePermission("production.create").RequireIdempotencyKey().WithName("GenerateFabricationOrder").Produces<FabricationOrderDetail>(StatusCodes.Status201Created);
        orders.MapPost("/batch", async (FabricationBatchInput input, FabricationOrderService service, CancellationToken ct) => (await service.GenerateManyAsync(input, ct)).ToHttpResult())
            .RequirePermission("production.create").RequireIdempotencyKey().WithName("GenerateFabricationOrders").Produces<FabricationBatchResult>();
        orders.MapGet("/{id:guid}", async (Guid id, FabricationOrderService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("production.view").WithName("GetFabricationOrder").Produces<FabricationOrderDetail>();
        orders.MapPut("/{id:guid}/steps", async (Guid id, FabricationStepsInput input, FabricationOrderService service, CancellationToken ct) => (await service.SaveStepsAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("production.edit").WithName("SaveFabricationOrderSteps").Produces<FabricationOrderDetail>();
        orders.MapGet("/{id:guid}/routings", async (Guid id, FabricationOrderService service, CancellationToken ct) => (await service.RoutingOptionsAsync(id, ct)).ToHttpResult())
            .RequirePermission("production.edit").WithName("ListFabricationRoutingOptions").Produces<IReadOnlyList<FabricationRoutingOption>>();
        orders.MapPost("/{id:guid}/routing", async (Guid id, FabricationRoutingInput input, FabricationOrderService service, CancellationToken ct) => (await service.ChangeRoutingAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("production.edit").WithName("ChangeFabricationRouting").Produces<FabricationOrderDetail>();
        orders.MapPost("/{id:guid}/cancel", async (Guid id, FabricationCancelInput input, FabricationOrderService service, CancellationToken ct) => (await service.CancelAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("production.edit").WithName("CancelFabricationOrder").Produces<FabricationOrderDetail>();
        orders.MapGet("/{id:guid}/history", async (Guid id, int? page, int? pageSize, AuditHistory history, CancellationToken ct) =>
                (await history.GetWithChildrenAsync(nameof(ProductionOrder), id.ToString(), OrderHistoryChildren, "orderId", new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)).ToHttpResult())
            .RequirePermission("production.view").WithName("GetFabricationOrderHistory").Produces<PagedList<RelatedAuditEntryDto>>();
    }
}