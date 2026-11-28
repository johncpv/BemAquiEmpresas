using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Commercial.Application;
using BemAqui.Commercial.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Commercial.Endpoints;

internal static partial class CommercialEndpoints
{
    private static readonly string[] StageTemplateHistoryChildren = [nameof(StageTemplateStep), nameof(StageTemplateItem)];

    /// <summary>
    /// Modelos de etapas do projeto de serviço (EXE-02): módulo <c>projects</c> com o recurso por perfil <c>projects.service_execution</c> (a
    /// Konebras recebe 403 <c>module_not_released</c>). Ver e lookups com <c>projects.view</c>; criar, editar, padrão, sugeridos e vínculo com
    /// <c>projects.edit</c>; excluir com <c>projects.delete</c>. Nenhuma ação nova.
    /// </summary>
    private static void MapStageTemplates(IEndpointRouteBuilder api)
    {
        var routes = api.MapGroup("/stage-templates").WithTags("Modelos de etapas").RequireModule("projects")
            .RequireProfileFeature("projects", BusinessProfileCatalog.ServiceExecutionFeature);
        routes.MapGet(string.Empty, async (string? q, string? situation, int? page, int? pageSize, StageTemplateService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new StageTemplateListQuery(q, situation), new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)))
            .RequirePermission("projects.view").WithName("ListStageTemplates").Produces<PagedList<StageTemplateSummary>>();
        routes.MapGet("/lookups/positions", async (StageTemplateService service, CancellationToken ct) => Results.Ok(await service.PositionsAsync(ct)))
            .RequirePermission("projects.view").WithName("ListStageTemplatePositions").Produces<IReadOnlyList<StagePositionOption>>();
        routes.MapGet("/lookups/segments", () => Results.Ok(StageTemplateService.SegmentOptions()))
            .RequirePermission("projects.view").WithName("ListStageTemplateSegments").Produces<IReadOnlyList<StageSegmentOption>>();
        routes.MapGet("/lookups/items", async (string? q, StageTemplateService service, CancellationToken ct) => Results.Ok(await service.ItemOptionsAsync(q, ct)))
            .RequirePermission("projects.view").WithName("ListStageTemplateItemOptions").Produces<IReadOnlyList<StageItemOption>>();
        routes.MapGet("/by-item/{itemId:guid}", async (Guid itemId, StageTemplateService service, CancellationToken ct) => Results.Ok(await service.ForItemAsync(itemId, ct)))
            .RequirePermission("projects.view").WithName("GetItemStageTemplate").Produces<ItemStageTemplateView>();
        routes.MapPost("/suggested", async (StageTemplateService service, CancellationToken ct) => Results.Ok(new StageTemplateSeedResult(await service.SeedSuggestedAsync(ct))))
            .RequirePermission("projects.edit").WithName("SeedSuggestedStageTemplates").Produces<StageTemplateSeedResult>();
        routes.MapGet("/{id:guid}", async (Guid id, StageTemplateService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("projects.view").WithName("GetStageTemplate").Produces<StageTemplateDetail>();
        routes.MapGet("/{id:guid}/history", async (Guid id, int? page, int? pageSize, AuditHistory history, CancellationToken ct) =>
                (await history.GetWithChildrenAsync(nameof(StageTemplate), id.ToString(), StageTemplateHistoryChildren, "templateId",
                    new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)).ToHttpResult())
            .RequirePermission("projects.view").WithName("GetStageTemplateHistory").Produces<PagedList<RelatedAuditEntryDto>>();
        routes.MapPost(string.Empty, async (StageTemplateInput input, StageTemplateService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/stage-templates/" + saved.Id, saved)))
            .RequirePermission("projects.edit").WithName("CreateStageTemplate").Produces<StageTemplateDetail>(StatusCodes.Status201Created);
        routes.MapPut("/{id:guid}", async (Guid id, StageTemplateUpdate input, StageTemplateService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("projects.edit").WithName("UpdateStageTemplate").Produces<StageTemplateDetail>();
        routes.MapPost("/{id:guid}/default", async (Guid id, StageTemplateVersionInput input, StageTemplateService service, CancellationToken ct) => (await service.SetDefaultAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("projects.edit").WithName("SetDefaultStageTemplate").Produces<StageTemplateDetail>();
        routes.MapPost("/{id:guid}/inactivate", async (Guid id, StageTemplateVersionInput input, StageTemplateService service, CancellationToken ct) => (await service.InactivateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("projects.edit").WithName("InactivateStageTemplate").Produces<StageTemplateDetail>();
        routes.MapPost("/{id:guid}/activate", async (Guid id, StageTemplateVersionInput input, StageTemplateService service, CancellationToken ct) => (await service.ActivateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("projects.edit").WithName("ActivateStageTemplate").Produces<StageTemplateDetail>();
        routes.MapDelete("/{id:guid}", async (Guid id, uint version, StageTemplateService service, CancellationToken ct) => (await service.DeleteAsync(id, version, ct)).ToHttpResult())
            .RequirePermission("projects.delete").WithName("DeleteStageTemplate");

        var items = routes.MapGroup("/{id:guid}/items");
        items.MapGet(string.Empty, async (Guid id, StageTemplateService service, CancellationToken ct) => (await service.ListItemsAsync(id, ct)).ToHttpResult())
            .RequirePermission("projects.view").WithName("ListStageTemplateItems").Produces<IReadOnlyList<StageTemplateItemView>>();
        items.MapPost(string.Empty, async (Guid id, StageTemplateItemLinkInput input, StageTemplateService service, CancellationToken ct) =>
                (await service.LinkItemAsync(id, input, ct)).ToHttpResult(saved => Results.Created($"/api/v1/stage-templates/{id}/items/{saved.Id}", saved)))
            .RequirePermission("projects.edit").WithName("LinkStageTemplateItem").Produces<StageTemplateItemView>(StatusCodes.Status201Created);
        items.MapDelete("/{linkId:guid}", async (Guid id, Guid linkId, StageTemplateService service, CancellationToken ct) => (await service.UnlinkItemAsync(id, linkId, ct)).ToHttpResult())
            .RequirePermission("projects.edit").WithName("UnlinkStageTemplateItem");
    }
}
