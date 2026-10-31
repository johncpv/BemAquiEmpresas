using BemAqui.Assets.Application;
using BemAqui.Assets.Contracts;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Assets.Endpoints;

/// <summary>
/// Patrimônio (PAT-01a): bens, foto e documentos pela rota do bem, seletores pelos Contracts dos donos e locais. Módulo <c>assets</c>
/// (403 <c>module_not_released</c>) e <c>assets.view/create/edit/delete</c>; respostas sem cache (o valor de aquisição só vai para quem
/// tem <c>assets.edit</c>).
/// </summary>
internal static class AssetsEndpoints
{
    public static IEndpointRouteBuilder MapAssetsEndpoints(this IEndpointRouteBuilder api)
    {
        var root = api.MapGroup("/assets").WithTags("Patrimônio").RequireModule(AssetService.Module)
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });
        MapAssets(root.MapGroup("/assets"));
        MapChoices(root.MapGroup("/choices"));
        MapSites(root.MapGroup("/sites"));
        return api;
    }

    private static void MapAssets(RouteGroupBuilder assets)
    {
        assets.MapGet(string.Empty, async (string? q, AssetType? type, Guid? siteId, Guid? workCenterId, Guid? responsibleEmployeeId, AssetStatus? status, int? page, int? pageSize, string? sort,
                AssetService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new AssetListQuery(q, type, siteId, workCenterId, responsibleEmployeeId, status), new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize, sort).Normalize(), ct)))
            .RequirePermission("assets.view").WithName("ListAssets").Produces<PagedList<AssetListItem>>();
        assets.MapGet("/suggestions", async (Guid workCenterId, AssetService service, CancellationToken ct) => Results.Ok(await service.SuggestAsync(workCenterId, ct)))
            .RequirePermission("assets.view").WithName("SuggestAssetPlacement").Produces<AssetSuggestionView>();
        assets.MapGet("/{id:guid}", async (Guid id, AssetService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("assets.view").WithName("GetAsset").Produces<AssetDetail>();
        assets.MapPost(string.Empty, async (AssetInput input, AssetService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/assets/assets/" + saved.Id, saved)))
            .RequirePermission("assets.create").WithName("CreateAsset").Produces<AssetDetail>(StatusCodes.Status201Created);
        assets.MapPut("/{id:guid}", async (Guid id, AssetInput input, AssetService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("assets.edit").WithName("UpdateAsset").Produces<AssetDetail>();
        assets.MapDelete("/{id:guid}", async (Guid id, AssetService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission("assets.delete").WithName("DeleteAsset");

        assets.MapGet("/{id:guid}/attachments", async (Guid id, AssetService service, CancellationToken ct) => (await service.FilesAsync(id, ct)).ToHttpResult())
            .RequirePermission("assets.view").WithName("ListAssetFiles").Produces<AssetFiles>();
        assets.MapPost("/{id:guid}/photo", async (Guid id, IFormFile file, AssetService service, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                return (await service.UploadPhotoAsync(id, file.FileName, content, ct)).ToHttpResult(dto => Results.Created($"/api/v1/assets/assets/{id}/attachments/{dto.Id}/content", dto));
            })
            .RequirePermission("assets.edit").DisableAntiforgery().WithName("UploadAssetPhoto").Produces<AttachmentDto>(StatusCodes.Status201Created);
        assets.MapPost("/{id:guid}/documents", async (Guid id, IFormFile file, AssetService service, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                return (await service.UploadDocumentAsync(id, file.FileName, content, ct)).ToHttpResult(dto => Results.Created($"/api/v1/assets/assets/{id}/attachments/{dto.Id}/content", dto));
            })
            .RequirePermission("assets.edit").DisableAntiforgery().WithName("UploadAssetDocument").Produces<AttachmentDto>(StatusCodes.Status201Created);
        assets.MapGet("/{id:guid}/attachments/{attachmentId:guid}/content", async (Guid id, Guid attachmentId, AssetService service, HttpContext http, CancellationToken ct) =>
            {
                var result = await service.FileAsync(id, attachmentId, ct);
                if (result.IsFailure)
                {
                    return result.Error!.ToProblem();
                }

                AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
                return Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
            })
            .RequirePermission("assets.view").WithName("DownloadAssetFile");
    }

    private static void MapChoices(RouteGroupBuilder choices)
    {
        choices.MapGet("/responsibles", async (AssetService service, CancellationToken ct) => Results.Ok(await service.ResponsibleChoicesAsync(ct)))
            .RequirePermission("assets.view").WithName("ListAssetResponsibleChoices").Produces<IReadOnlyList<AssetResponsibleChoice>>();
        choices.MapGet("/work-centers", async (AssetService service, CancellationToken ct) => Results.Ok(await service.WorkCenterChoicesAsync(ct)))
            .RequirePermission("assets.view").WithName("ListAssetWorkCenterChoices").Produces<IReadOnlyList<AssetWorkCenterChoice>>();
        choices.MapGet("/suppliers", async (string? q, AssetService service, CancellationToken ct) => Results.Ok(await service.SupplierChoicesAsync(q, ct)))
            .RequirePermission("assets.view").WithName("ListAssetSupplierChoices").Produces<IReadOnlyList<AssetSupplierChoice>>();
        choices.MapGet("/incoming-invoices", async (string? q, AssetService service, CancellationToken ct) => Results.Ok(await service.InvoiceChoicesAsync(q, ct)))
            .RequirePermission("assets.view").WithName("ListAssetInvoiceChoices").Produces<IReadOnlyList<AssetInvoiceChoice>>();
    }

    private static void MapSites(RouteGroupBuilder sites)
    {
        sites.MapGet(string.Empty, async (bool? includeInactive, AssetSiteService service, CancellationToken ct) => Results.Ok(await service.ListAsync(includeInactive == true, ct)))
            .RequirePermission("assets.view").WithName("ListAssetSites").Produces<IReadOnlyList<AssetSiteView>>();
        sites.MapPost(string.Empty, async (AssetSiteInput input, AssetSiteService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/assets/sites/" + saved.Id, saved)))
            .RequirePermission("assets.edit").WithName("CreateAssetSite").Produces<AssetSiteView>(StatusCodes.Status201Created);
        sites.MapPut("/{id:guid}", async (Guid id, AssetSiteInput input, AssetSiteService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("assets.edit").WithName("UpdateAssetSite").Produces<AssetSiteView>();
        sites.MapDelete("/{id:guid}", async (Guid id, AssetSiteService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission("assets.delete").WithName("DeleteAssetSite");
    }
}
