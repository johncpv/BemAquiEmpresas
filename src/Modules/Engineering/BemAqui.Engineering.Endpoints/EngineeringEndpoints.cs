using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Engineering.Application;
using BemAqui.Engineering.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Engineering.Endpoints;

/// <summary>Roteiros de fabricação (ENG-01). Módulo <c>engineering</c> com as três camadas de acesso em todo endpoint (CA 15).</summary>
internal static class EngineeringEndpoints
{
    private static readonly string[] HistoryChildren = [nameof(RoutingTemplateStep), nameof(RoutingTemplateItem)];

    public static IEndpointRouteBuilder MapEngineeringEndpoints(this IEndpointRouteBuilder api)
    {
        var engineering = api.MapGroup("/engineering").WithTags("Engenharia").RequireModule("engineering");
        MapRoutingTemplates(engineering.MapGroup("/routing-templates"));
        MapBoms(engineering.MapGroup("/boms"));
        return api;
    }

    private static readonly string[] BomHistoryChildren = [nameof(BomLine)];

    /// <summary>
    /// BOM da OS (ENG-02 CA 19): ver com engineering.view, criar com engineering.create, salvar, copiar e revisar com engineering.edit,
    /// liberar com engineering.approve e descartar o rascunho com engineering.delete. A OS é conferida pelo IProductionOrders; a linha,
    /// pela BOM da URL (IDOR).
    /// </summary>
    private static void MapBoms(RouteGroupBuilder boms)
    {
        boms.MapGet(string.Empty, async (string? situation, string? q, int? page, int? pageSize, BomService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new BomListQuery(situation, q), new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)))
            .RequirePermission("engineering.view").WithName("ListBoms").Produces<PagedList<BomOrderSummary>>();
        boms.MapGet("/orders/{orderId:guid}", async (Guid orderId, BomService service, CancellationToken ct) => (await service.GetOrderAsync(orderId, ct)).ToHttpResult())
            .RequirePermission("engineering.view").WithName("GetOrderBom").Produces<BomOrderView>();
        boms.MapGet("/orders/{orderId:guid}/history", async (Guid orderId, int? page, int? pageSize, AuditHistory history, CancellationToken ct) =>
                (await history.GetByParentAsync(nameof(Bom), "productionOrderId", orderId.ToString(), BomHistoryChildren, "bomId",
                    new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)).ToHttpResult())
            .RequirePermission("engineering.view").WithName("GetOrderBomHistory").Produces<PagedList<RelatedAuditEntryDto>>();
        boms.MapGet("/orders/{orderId:guid}/item-options", async (Guid orderId, string? q, BomService service, CancellationToken ct) => (await service.ItemOptionsAsync(orderId, q, ct)).ToHttpResult())
            .RequirePermission("engineering.view").WithName("ListBomItemOptions").Produces<IReadOnlyList<BomItemOption>>();
        boms.MapGet("/orders/{orderId:guid}/copy-sources", async (Guid orderId, string? q, BomService service, CancellationToken ct) => (await service.CopySourcesAsync(orderId, q, ct)).ToHttpResult())
            .RequirePermission("engineering.view").WithName("ListBomCopySources").Produces<IReadOnlyList<BomCopySource>>();
        boms.MapGet("/compare", async (Guid from, Guid to, BomService service, CancellationToken ct) => (await service.CompareAsync(from, to, ct)).ToHttpResult())
            .RequirePermission("engineering.view").WithName("CompareBoms").Produces<BomComparison>();
        boms.MapGet("/{id:guid}", async (Guid id, BomService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("engineering.view").WithName("GetBom").Produces<BomDetail>();
        boms.MapGet("/{id:guid}/lines/{lineId:guid}", async (Guid id, Guid lineId, BomService service, CancellationToken ct) => (await service.GetLineAsync(id, lineId, ct)).ToHttpResult())
            .RequirePermission("engineering.view").WithName("GetBomLine").Produces<BomLineView>();
        boms.MapGet("/{id:guid}/release-preview", async (Guid id, BomService service, CancellationToken ct) => (await service.ReleasePreviewAsync(id, ct)).ToHttpResult())
            .RequirePermission("engineering.view").WithName("GetBomReleasePreview").Produces<BomReleasePreview>();
        boms.MapPost(string.Empty, async (BomCreateInput input, BomService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/engineering/boms/" + saved.Id, saved)))
            .RequirePermission("engineering.create").RequireIdempotencyKey().WithName("CreateBom").Produces<BomDetail>(StatusCodes.Status201Created);
        boms.MapPut("/{id:guid}", async (Guid id, BomUpdate input, BomService service, CancellationToken ct) => (await service.SaveAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("engineering.edit").WithName("SaveBom").Produces<BomDetail>();
        boms.MapPost("/{id:guid}/copy", async (Guid id, BomCopyInput input, BomService service, CancellationToken ct) => (await service.CopyAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("engineering.edit").WithName("CopyBom").Produces<BomDetail>();
        boms.MapPost("/{id:guid}/revise", async (Guid id, BomVersionInput input, BomService service, CancellationToken ct) =>
                (await service.ReviseAsync(id, input, ct)).ToHttpResult(saved => Results.Created("/api/v1/engineering/boms/" + saved.Id, saved)))
            .RequirePermission("engineering.edit").RequireIdempotencyKey().WithName("ReviseBom").Produces<BomDetail>(StatusCodes.Status201Created);
        boms.MapPost("/{id:guid}/release", async (Guid id, BomVersionInput input, BomService service, CancellationToken ct) => (await service.ReleaseAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("engineering.approve").WithName("ReleaseBom").Produces<BomDetail>();
        boms.MapPost("/{id:guid}/discard", async (Guid id, BomVersionInput input, BomService service, CancellationToken ct) => (await service.DiscardAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("engineering.delete").WithName("DiscardBom");
    }

    private static void MapRoutingTemplates(RouteGroupBuilder routes)
    {
        routes.MapGet(string.Empty, async (string? q, string? situation, string? productType, int? page, int? pageSize, RoutingTemplateService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new RoutingTemplateListQuery(q, situation, productType), new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)))
            .RequirePermission("engineering.view").WithName("ListRoutingTemplates").Produces<PagedList<RoutingTemplateSummary>>();
        routes.MapGet("/product-types", async (RoutingTemplateService service, CancellationToken ct) => Results.Ok(await service.ProductTypesAsync(ct)))
            .RequirePermission("engineering.view").WithName("ListRoutingProductTypes").Produces<IReadOnlyList<string>>();
        routes.MapGet("/work-centers", async (RoutingTemplateService service, CancellationToken ct) => Results.Ok(await service.WorkCentersAsync(ct)))
            .RequirePermission("engineering.view").WithName("ListRoutingWorkCenters").Produces<IReadOnlyList<RoutingWorkCenterOption>>();
        routes.MapGet("/suggested-work-centers", async (RoutingTemplateService service, CancellationToken ct) => Results.Ok(await service.SuggestedWorkCentersAsync(ct)))
            .RequirePermission("engineering.view").WithName("ListSuggestedRoutingWorkCenters").Produces<IReadOnlyList<SuggestedWorkCenter>>();
        routes.MapGet("/products", async (string? q, RoutingTemplateService service, CancellationToken ct) => Results.Ok(await service.ProductOptionsAsync(q, ct)))
            .RequirePermission("engineering.view").WithName("ListRoutingProductOptions").Produces<IReadOnlyList<RoutingProductOption>>();
        routes.MapGet("/{id:guid}", async (Guid id, RoutingTemplateService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("engineering.view").WithName("GetRoutingTemplate").Produces<RoutingTemplateDetail>();
        routes.MapGet("/{id:guid}/duplicate", async (Guid id, RoutingTemplateService service, CancellationToken ct) => (await service.DuplicateAsync(id, ct)).ToHttpResult())
            .RequirePermission("engineering.create").WithName("DuplicateRoutingTemplate").Produces<RoutingTemplateInput>();
        routes.MapGet("/{id:guid}/steps/{stepId:guid}", async (Guid id, Guid stepId, RoutingTemplateService service, CancellationToken ct) => (await service.GetStepAsync(id, stepId, ct)).ToHttpResult())
            .RequirePermission("engineering.view").WithName("GetRoutingTemplateStep").Produces<RoutingStepView>();
        routes.MapGet("/{id:guid}/history", async (Guid id, int? page, int? pageSize, AuditHistory history, CancellationToken ct) =>
                (await history.GetWithChildrenAsync(nameof(RoutingTemplate), id.ToString(), HistoryChildren, "templateId", new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)).ToHttpResult())
            .RequirePermission("engineering.view").WithName("GetRoutingTemplateHistory").Produces<PagedList<RelatedAuditEntryDto>>();
        routes.MapPost(string.Empty, async (RoutingTemplateInput input, RoutingTemplateService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/engineering/routing-templates/" + saved.Id, saved)))
            .RequirePermission("engineering.create").WithName("CreateRoutingTemplate").Produces<RoutingTemplateDetail>(StatusCodes.Status201Created);
        routes.MapPut("/{id:guid}", async (Guid id, RoutingTemplateUpdate input, RoutingTemplateService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("engineering.edit").WithName("UpdateRoutingTemplate").Produces<RoutingTemplateDetail>();
        routes.MapPost("/{id:guid}/inactivate", async (Guid id, RoutingTemplateService service, CancellationToken ct) => (await service.InactivateAsync(id, ct)).ToHttpResult())
            .RequirePermission("engineering.edit").WithName("InactivateRoutingTemplate").Produces<RoutingTemplateDetail>();
        routes.MapPost("/{id:guid}/activate", async (Guid id, RoutingTemplateService service, CancellationToken ct) => (await service.ActivateAsync(id, ct)).ToHttpResult())
            .RequirePermission("engineering.edit").WithName("ActivateRoutingTemplate").Produces<RoutingTemplateDetail>();
        routes.MapDelete("/{id:guid}", async (Guid id, RoutingTemplateService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission("engineering.delete").WithName("DeleteRoutingTemplate");

        var items = routes.MapGroup("/{id:guid}/items");
        items.MapGet(string.Empty, async (Guid id, RoutingTemplateService service, CancellationToken ct) => (await service.ListItemsAsync(id, ct)).ToHttpResult())
            .RequirePermission("engineering.view").WithName("ListRoutingTemplateItems").Produces<IReadOnlyList<RoutingItemView>>();
        items.MapPost(string.Empty, async (Guid id, RoutingItemLinkInput input, RoutingTemplateService service, CancellationToken ct) =>
                (await service.LinkItemAsync(id, input, ct)).ToHttpResult(saved => Results.Created($"/api/v1/engineering/routing-templates/{id}/items/{saved.Id}", saved)))
            .RequirePermission("engineering.edit").WithName("LinkRoutingTemplateItem").Produces<RoutingItemView>(StatusCodes.Status201Created);
        items.MapDelete("/{itemId:guid}", async (Guid id, Guid itemId, RoutingTemplateService service, CancellationToken ct) => (await service.UnlinkItemAsync(id, itemId, ct)).ToHttpResult())
            .RequirePermission("engineering.edit").WithName("UnlinkRoutingTemplateItem");
    }
}