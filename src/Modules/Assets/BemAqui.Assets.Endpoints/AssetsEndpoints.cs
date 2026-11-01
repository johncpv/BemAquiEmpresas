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
        MapMovements(root.MapGroup("/assets"));
        MapLabels(root);
        MapChoices(root.MapGroup("/choices"));
        MapSites(root.MapGroup("/sites"));
        return api;
    }

    /// <summary>
    /// Etiqueta e leitura (PAT-01b): a leitura pela etiqueta confere <c>assets.view</c> na rota, antes de buscar (CA 7), e fica fora da sessão
    /// por PIN (sem <c>[PinSessionAllowed]</c>, CA 10). Imprimir com <c>assets.view</c>, reimprimir com <c>assets.edit</c>; o tamanho da
    /// etiqueta grava com <c>settings.edit</c>, conferido no serviço (como os parâmetros da Produção).
    /// </summary>
    private static void MapLabels(RouteGroupBuilder root)
    {
        var assets = root.MapGroup("/assets");
        assets.MapGet("/by-tag/{tag}", async (string tag, AssetService service, CancellationToken ct) => (await service.ByTagAsync(tag, ct)).ToHttpResult())
            .RequirePermission("assets.view").WithName("GetAssetByTag").Produces<AssetCardView>();
        assets.MapGet("/{id:guid}/card", async (Guid id, AssetService service, CancellationToken ct) => (await service.CardAsync(id, ct)).ToHttpResult())
            .RequirePermission("assets.view").WithName("GetAssetCard").Produces<AssetCardView>();
        assets.MapGet("/{id:guid}/label", async (Guid id, AssetLabelService service, HttpContext http, CancellationToken ct) => Pdf(http, await service.LabelAsync(id, false, ct)))
            .RequirePermission("assets.view").WithName("PrintAssetLabel");
        assets.MapGet("/{id:guid}/label/reprint", async (Guid id, AssetLabelService service, HttpContext http, CancellationToken ct) => Pdf(http, await service.LabelAsync(id, true, ct)))
            .RequirePermission("assets.edit").WithName("ReprintAssetLabel");

        var batches = root.MapGroup("/label-batches");
        batches.MapPost(string.Empty, async (AssetLabelBatchInput input, AssetLabelService service, CancellationToken ct) => (await service.StartBatchAsync(input, ct)).ToHttpResult())
            .RequirePermission("assets.view").WithName("StartAssetLabelBatch").Produces<AssetLabelBatchResult>();
        batches.MapGet("/{attachmentId:guid}", async (Guid attachmentId, AssetLabelService service, HttpContext http, CancellationToken ct) => Pdf(http, await service.BatchFileAsync(attachmentId, ct)))
            .RequirePermission("assets.view").WithName("DownloadAssetLabelBatch");

        var parameters = root.MapGroup("/parameters");
        parameters.MapGet(string.Empty, async (AssetParametersService service, CancellationToken ct) => Results.Ok(await service.GetAsync(ct)))
            .RequirePermission("assets.view").WithName("GetAssetParameters").Produces<AssetParametersView>();
        parameters.MapPut(string.Empty, async (AssetParametersInput input, AssetParametersService service, CancellationToken ct) => (await service.UpdateAsync(input, ct)).ToHttpResult())
            .RequirePermission("assets.view").WithName("UpdateAssetParameters").Produces<AssetParametersView>();
    }

    /// <summary>
    /// Movimentação e baixa (PAT-02): ler com <c>assets.view</c>; registrar, transferir em lote, baixar e reativar com <c>assets.edit</c>. O
    /// movimento é imutável: só leitura e inclusão, sem rota de edição nem de exclusão (CA 3); o movimento é lido pela rota do bem (IDOR, CA 9).
    /// </summary>
    private static void MapMovements(RouteGroupBuilder assets)
    {
        assets.MapGet("/{id:guid}/movements", async (Guid id, AssetMovementService service, CancellationToken ct) => (await service.ListAsync(id, ct)).ToHttpResult())
            .RequirePermission("assets.view").WithName("ListAssetMovements").Produces<IReadOnlyList<AssetMovementView>>();
        assets.MapGet("/{id:guid}/movements/{movementId:guid}", async (Guid id, Guid movementId, AssetMovementService service, CancellationToken ct) => (await service.GetAsync(id, movementId, ct)).ToHttpResult())
            .RequirePermission("assets.view").WithName("GetAssetMovement").Produces<AssetMovementView>();
        assets.MapPost("/{id:guid}/movements", async (Guid id, AssetMovementInput input, AssetMovementService service, CancellationToken ct) => (await service.MoveAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("assets.edit").WithName("MoveAsset").Produces<AssetMovementResult>();
        assets.MapPost("/{id:guid}/dispose", async (Guid id, AssetDisposalInput input, AssetMovementService service, CancellationToken ct) => (await service.DisposeAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("assets.edit").WithName("DisposeAsset").Produces<AssetMovementResult>();
        assets.MapPost("/{id:guid}/reactivate", async (Guid id, AssetReactivationInput input, AssetMovementService service, CancellationToken ct) => (await service.ReactivateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("assets.edit").WithName("ReactivateAsset").Produces<AssetMovementResult>();
        assets.MapPost("/transfer/preview", async (AssetTransferInput input, AssetMovementService service, CancellationToken ct) => (await service.PreviewTransferAsync(input, ct)).ToHttpResult())
            .RequirePermission("assets.edit").WithName("PreviewAssetTransfer").Produces<AssetTransferPreview>();
        assets.MapPost("/transfer", async (AssetTransferInput input, AssetMovementService service, CancellationToken ct) => (await service.TransferAsync(input, ct)).ToHttpResult())
            .RequirePermission("assets.edit").WithName("TransferAssets").Produces<AssetTransferResult>();
    }

    private static IResult Pdf(HttpContext http, Result<AssetLabelFile> result)
    {
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
        return Results.File(result.Value.Content, AssetLabelService.PdfContentType, result.Value.FileName);
    }

    private static void MapAssets(RouteGroupBuilder assets)
    {
        assets.MapGet(string.Empty, async (string? q, AssetType? type, Guid? siteId, Guid? workCenterId, Guid? responsibleEmployeeId, AssetStatus? status, bool? responsibleTerminated, int? page, int? pageSize, string? sort,
                AssetService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new AssetListQuery(q, type, siteId, workCenterId, responsibleEmployeeId, status, responsibleTerminated == true), new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize, sort).Normalize(), ct)))
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
        choices.MapGet("/buyers", async (string? q, AssetMovementService service, CancellationToken ct) => Results.Ok(await service.BuyerChoicesAsync(q, ct)))
            .RequirePermission("assets.edit").WithName("ListAssetBuyerChoices").Produces<IReadOnlyList<AssetBuyerChoice>>();
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
