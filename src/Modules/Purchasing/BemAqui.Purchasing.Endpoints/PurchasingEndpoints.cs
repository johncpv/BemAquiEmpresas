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
/// Solicitações de compra (CMP-01 CA 3, 4, 12 a 17). Módulo <c>purchasing</c> com as três camadas (403 <c>module_not_released</c> e
/// <c>perm:</c>): ver com <c>purchasing.view</c>, enviar com <c>purchasing.create</c> (com <c>Idempotency-Key</c>), editar e cancelar com
/// <c>purchasing.edit</c>. A linha é sempre lida pela SC da URL (IDOR). Nenhum endpoint usa <c>purchasing.receive</c> (é da CMP-04).
/// </summary>
internal static partial class PurchasingEndpoints
{
    private static readonly string[] HistoryChildren = [nameof(PurchaseRequestLine)];

    public static IEndpointRouteBuilder MapPurchasingEndpoints(this IEndpointRouteBuilder api)
    {
        var purchasing = api.MapGroup("/purchasing").WithTags("Compras").RequireModule("purchasing");
        MapRequests(purchasing.MapGroup("/purchase-requests"));
        MapOrders(purchasing.MapGroup("/purchase-orders"));
        MapOrderOptions(purchasing);
        purchasing.MapGet("/items", async (string? q, PurchaseRequestService service, CancellationToken ct) => Results.Ok(await service.ItemOptionsAsync(q, ct)))
            .RequirePermission("purchasing.create").WithName("ListPurchaseItemOptions").Produces<IReadOnlyList<PurchaseItemOption>>();
        purchasing.MapGet("/projects", async (string? q, PurchaseRequestService service, CancellationToken ct) => Results.Ok(await service.ProjectOptionsAsync(q, ct)))
            .RequireAnyPermissionOf("purchasing.view", "purchasing.create").WithName("ListPurchaseProjectOptions").Produces<IReadOnlyList<PurchaseProjectOption>>();
        // Configurações → Parâmetros (ADR 0016 adendo M6): a seção aparece para quem vê Compras; salvar exige settings.edit (no serviço).
        purchasing.MapGet("/parameters", async (PurchasingParametersService service, CancellationToken ct) => Results.Ok(await service.GetAsync(ct)))
            .RequirePermission("purchasing.view").WithName("GetPurchasingParameters").Produces<PurchasingParametersView>();
        purchasing.MapPut("/parameters", async (PurchasingParametersInput input, PurchasingParametersService service, CancellationToken ct) => (await service.UpdateAsync(input, ct)).ToHttpResult())
            .RequirePermission("purchasing.view").WithName("UpdatePurchasingParameters").Produces<PurchasingParametersView>();
        return api;
    }

    private static void MapRequests(RouteGroupBuilder requests)
    {
        requests.MapGet(string.Empty, async (
                string? show, PurchaseRequestOrigin? origin, PurchaseRequestStatus? status, Guid? projectId, Guid? itemId, Guid? productionOrderId, string? q, int? page, int? pageSize,
                PurchaseRequestService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new PurchaseRequestListQuery(show, origin, status, projectId, itemId, productionOrderId, q), Page(page, pageSize), ct)))
            .RequirePermission("purchasing.view").WithName("ListPurchaseRequests").Produces<PagedList<PurchaseRequestSummary>>();
        requests.MapGet("/by-item", async (string? q, Guid? projectId, int? page, int? pageSize, PurchaseRequestService service, CancellationToken ct) =>
                Results.Ok(await service.ByItemAsync(q, projectId, Page(page, pageSize), ct)))
            .RequirePermission("purchasing.view").WithName("ListPurchaseRequestsByItem").Produces<PagedList<PurchaseItemRow>>();
        requests.MapGet("/defaults", async (Guid? productionOrderId, Guid? itemId, PurchaseRequestService service, CancellationToken ct) =>
                (await service.DefaultsAsync(productionOrderId, itemId, ct)).ToHttpResult())
            .RequirePermission("purchasing.create").WithName("GetPurchaseRequestDefaults").Produces<PurchaseRequestDefaults>();
        requests.MapGet("/{id:guid}", async (Guid id, PurchaseRequestService service, CancellationToken ct) => (await service.DetailAsync(id, ct)).ToHttpResult())
            .RequirePermission("purchasing.view").WithName("GetPurchaseRequest").Produces<PurchaseRequestDetail>();
        requests.MapGet("/{id:guid}/history", async (Guid id, int? page, int? pageSize, AuditHistory history, CancellationToken ct) =>
                (await history.GetWithChildrenAsync(nameof(PurchaseRequest), id.ToString(), HistoryChildren, "requestId", Page(page, pageSize), ct)).ToHttpResult())
            .RequirePermission("purchasing.view").WithName("GetPurchaseRequestHistory").Produces<PagedList<RelatedAuditEntryDto>>();
        requests.MapPost(string.Empty, async (PurchaseRequestInput input, PurchaseRequestService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/purchasing/purchase-requests/" + saved.Id, saved)))
            .RequirePermission("purchasing.create").RequireIdempotencyKey().WithName("CreatePurchaseRequest").Produces<PurchaseRequestDetail>(StatusCodes.Status201Created);
        requests.MapPut("/{id:guid}/lines/{lineId:guid}", async (Guid id, Guid lineId, PurchaseRequestLineUpdate input, PurchaseRequestService service, CancellationToken ct) =>
                (await service.UpdateLineAsync(id, lineId, input, ct)).ToHttpResult())
            .RequirePermission("purchasing.edit").WithName("UpdatePurchaseRequestLine").Produces<PurchaseRequestLineUpdated>();
        requests.MapPost("/{id:guid}/cancel", async (Guid id, PurchaseRequestCancelInput input, PurchaseRequestService service, CancellationToken ct) =>
                (await service.CancelAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("purchasing.edit").WithName("CancelPurchaseRequest").Produces<PurchaseRequestDetail>();
        requests.MapPost("/{id:guid}/lines/{lineId:guid}/cancel", async (Guid id, Guid lineId, PurchaseRequestCancelInput input, PurchaseRequestService service, CancellationToken ct) =>
                (await service.CancelLineAsync(id, lineId, input, ct)).ToHttpResult())
            .RequirePermission("purchasing.edit").WithName("CancelPurchaseRequestLine").Produces<PurchaseRequestDetail>();
    }

    private static PageRequest Page(int? page, int? pageSize) => new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize();
}
