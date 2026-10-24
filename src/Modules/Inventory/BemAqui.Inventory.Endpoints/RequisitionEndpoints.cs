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
/// Requisição de material da OS e devolução (EST-03 CA 15): módulo <c>inventory</c> (403 <c>module_not_released</c>); baixar, devolver,
/// dar como perda e confirmar ou recusar pedido com <c>inventory.create</c>; aprovar e reprovar com <c>inventory.approve</c>; estornar
/// com <c>inventory.edit</c> ou <c>inventory.approve</c> (o serviço aplica a regra do CA 14). Todo comando que cria movimento exige
/// <c>Idempotency-Key</c>. A requisição é lida pela OS e a linha pela requisição (IDOR: de outra, 404).
/// </summary>
internal static class RequisitionEndpoints
{
    private static readonly string[] HistoryChildren = [nameof(MaterialRequisitionLine)];

    public static IEndpointRouteBuilder MapRequisitionEndpoints(this IEndpointRouteBuilder api)
    {
        var inventory = api.MapGroup("/inventory").WithTags("Estoque").RequireModule("inventory");
        MapRequisitions(inventory.MapGroup("/requisitions"));
        MapOrder(inventory.MapGroup("/production-orders/{id:guid}"));
        return api;
    }

    private static void MapRequisitions(RouteGroupBuilder requisitions)
    {
        requisitions.MapGet(string.Empty, async (MaterialRequisitionStatus? status, bool? awaitingMe, Guid? productionOrderId, MaterialRequisitionKind? kind, int? page, int? pageSize,
                MaterialRequisitionService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new RequisitionListQuery(status, awaitingMe == true, productionOrderId, kind), Page(page, pageSize), ct)))
            .RequirePermission("inventory.view").WithName("ListMaterialRequisitions").Produces<PagedList<RequisitionSummary>>();
        requisitions.MapGet("/to-pick", async (MaterialRequisitionService service, CancellationToken ct) => Results.Ok(await service.ToPickAsync(ct)))
            .RequirePermission("inventory.view").WithName("ListRequisitionsToPick").Produces<IReadOnlyList<PickingRow>>();
        requisitions.MapGet("/item-stock/{itemId:guid}", async (Guid itemId, MaterialRequisitionService service, CancellationToken ct) => (await service.ItemStockAsync(itemId, ct)).ToHttpResult())
            .RequirePermission("inventory.create").WithName("GetRequisitionItemStock").Produces<RequisitionItemStock>();
        requisitions.MapGet("/{id:guid}", async (Guid id, MaterialRequisitionService service, CancellationToken ct) => (await service.DetailAsync(id, ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("GetMaterialRequisition").Produces<RequisitionDetail>();
        requisitions.MapGet("/{id:guid}/lines/{lineId:guid}", async (Guid id, Guid lineId, MaterialRequisitionService service, CancellationToken ct) => (await service.LineAsync(id, lineId, ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("GetMaterialRequisitionLine").Produces<RequisitionLineView>();
        requisitions.MapGet("/{id:guid}/history", async (Guid id, int? page, int? pageSize, AuditHistory history, CancellationToken ct) =>
                (await history.GetWithChildrenAsync(nameof(MaterialRequisition), id.ToString(), HistoryChildren, "requisitionId", Page(page, pageSize), ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("GetMaterialRequisitionHistory").Produces<PagedList<RelatedAuditEntryDto>>();
        requisitions.MapPost("/{id:guid}/confirm", async (Guid id, RequisitionConfirmInput input, MaterialRequisitionService service, CancellationToken ct) => (await service.ConfirmAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("inventory.create").RequireIdempotencyKey().WithName("ConfirmMaterialRequest").Produces<RequisitionDetail>();
        requisitions.MapPost("/{id:guid}/decline", async (Guid id, RequisitionDeclineInput input, MaterialRequisitionService service, CancellationToken ct) => (await service.DeclineAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("inventory.create").WithName("DeclineMaterialRequest").Produces<RequisitionDetail>();
        requisitions.MapPost("/{id:guid}/approve", async (Guid id, RequisitionDecisionInput input, MaterialRequisitionService service, CancellationToken ct) => (await service.ApproveAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("inventory.approve").RequireIdempotencyKey().WithName("ApproveMaterialRequisition").Produces<RequisitionDetail>();
        requisitions.MapPost("/{id:guid}/reject", async (Guid id, RequisitionDecisionInput input, MaterialRequisitionService service, CancellationToken ct) => (await service.RejectAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("inventory.approve").WithName("RejectMaterialRequisition").Produces<RequisitionDetail>();
        requisitions.MapPost("/{id:guid}/reverse", async (Guid id, RequisitionReverseInput input, MaterialRequisitionService service, CancellationToken ct) => (await service.ReverseAsync(id, input, ct)).ToHttpResult())
            .RequireAnyPermission("inventory.edit", "inventory.approve").RequireIdempotencyKey().WithName("ReverseMaterialRequisition").Produces<RequisitionDetail>();
    }

    private static void MapOrder(RouteGroupBuilder order)
    {
        order.MapGet("/requisitions", async (Guid id, MaterialRequisitionService service, CancellationToken ct) => (await service.OrderRequisitionsAsync(id, ct)).ToHttpResult())
            .RequireAnyPermissionOf("inventory.view", "production.view").WithName("ListOrderRequisitions").Produces<OrderRequisitions>();
        order.MapGet("/requisitions/draft", async (Guid id, MaterialRequisitionService service, CancellationToken ct) => (await service.DraftAsync(id, ct)).ToHttpResult())
            .RequirePermission("inventory.create").WithName("GetRequisitionDraft").Produces<RequisitionDraft>();
        order.MapGet("/requisitions/{reqId:guid}", async (Guid id, Guid reqId, MaterialRequisitionService service, CancellationToken ct) => (await service.OrderRequisitionAsync(id, reqId, ct)).ToHttpResult())
            .RequireAnyPermissionOf("inventory.view", "production.view").WithName("GetOrderRequisition").Produces<RequisitionDetail>();
        order.MapPost("/requisitions", async (Guid id, RequisitionInput input, MaterialRequisitionService service, CancellationToken ct) =>
                (await service.IssueAsync(id, input, ct)).ToHttpResult(saved => Results.Created(MaterialRequisitionService.Link + saved.Id, saved)))
            .RequirePermission("inventory.create").RequireIdempotencyKey().WithName("CreateMaterialRequisition").Produces<RequisitionDetail>(StatusCodes.Status201Created);
        order.MapPost("/returns", async (Guid id, ReturnInput input, MaterialRequisitionService service, CancellationToken ct) =>
                (await service.ReturnAsync(id, input, ct)).ToHttpResult(saved => Results.Created(MaterialRequisitionService.Link + saved.Id, saved)))
            .RequirePermission("inventory.create").RequireIdempotencyKey().WithName("ReturnMaterialToStock").Produces<RequisitionDetail>(StatusCodes.Status201Created);
        order.MapPost("/losses", async (Guid id, LossInput input, MaterialRequisitionService service, CancellationToken ct) =>
                (await service.LossAsync(id, input, ct)).ToHttpResult(saved => Results.Created(MaterialRequisitionService.Link + saved.Id, saved)))
            .RequirePermission("inventory.create").RequireIdempotencyKey().WithName("RecordMaterialLoss").Produces<RequisitionDetail>(StatusCodes.Status201Created);
        // CA 10: linha Material da aba Custos da OS, pelo motor (não pela razão do Commercial).
        order.MapGet("/material-cost", async (Guid id, MaterialRequisitionService service, CancellationToken ct) => (await service.MaterialCostAsync(id, ct)).ToHttpResult())
            .RequireAnyPermissionOf("inventory.view", "production.view").RequirePermission("projects.view_cost").WithName("GetOrderMaterialCost").Produces<OrderMaterialCostView>();
    }

    private static PageRequest Page(int? page, int? pageSize) => new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize();
}
