using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Privacy;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Identity.Application.Privacy;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Identity.Endpoints;

/// <summary>
/// Configurações → Privacidade (LGPD-01a, <c>/api/v1/settings/privacy-requests</c>): módulo <c>settings</c> (camada 1), <c>settings.view</c> para
/// ler e <c>settings.edit</c> para gravar (camadas 2 e 3) e, no serviço, só o Proprietário (403 <c>forbidden</c> para os demais, ADR 0018). O
/// download da exportação sai com <c>no-store</c>, só pelo link de uso único.
/// </summary>
internal static class PrivacyEndpoints
{
    public static IEndpointRouteBuilder MapPrivacyEndpoints(this IEndpointRouteBuilder api)
    {
        var privacy = api.MapGroup("/settings/privacy-requests").WithTags("Configurações").RequireModule("settings")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });

        privacy.MapGet(string.Empty, async (PrivacyRequestStatus? status, PrivacyRequestKind? kind, int? page, int? pageSize, PrivacyRequestService service, CancellationToken ct) =>
                (await service.ListAsync(status, kind, new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)).ToHttpResult())
            .RequirePermission("settings.view").WithName("ListPrivacyRequests").Produces<PagedList<PrivacyRequestRow>>();
        privacy.MapPost(string.Empty, async (CreatePrivacyRequestInput input, PrivacyRequestService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(r => Results.Created($"/api/v1/settings/privacy-requests/{r.Id}", r)))
            .RequirePermission("settings.edit").WithName("CreatePrivacyRequest").Produces<PrivacyRequestDetail>(StatusCodes.Status201Created);
        privacy.MapGet("/subjects", async (PersonalDataSubjectKind kind, string? q, PrivacyRequestService service, CancellationToken ct) =>
                (await service.SearchSubjectsAsync(kind, q, ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("SearchPrivacySubjects").Produces<IReadOnlyList<PersonalDataSubjectMatch>>();
        privacy.MapGet("/settings", async (PrivacyRequestService service, CancellationToken ct) => (await service.GetSettingsAsync(ct)).ToHttpResult())
            .RequirePermission("settings.view").WithName("GetPrivacySettings").Produces<PrivacySettingsDto>();
        privacy.MapPut("/settings", async (PrivacySettingsInput input, PrivacyRequestService service, CancellationToken ct) => (await service.SaveSettingsAsync(input, ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("SavePrivacySettings").Produces<PrivacySettingsDto>();
        privacy.MapGet("/{id:guid}", async (Guid id, PrivacyRequestService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("settings.view").WithName("GetPrivacyRequest").Produces<PrivacyRequestDetail>();
        privacy.MapGet("/{id:guid}/history", async (Guid id, PrivacyRequestService service, CancellationToken ct) => (await service.HistoryAsync(id, ct)).ToHttpResult())
            .RequirePermission("settings.view").WithName("GetPrivacyRequestHistory").Produces<IReadOnlyList<PrivacyHistoryEntry>>();
        privacy.MapPost("/{id:guid}/identity", async (Guid id, VerifyIdentityInput input, PrivacyRequestService service, CancellationToken ct) =>
                (await service.VerifyIdentityAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("VerifyPrivacySubjectIdentity").Produces<PrivacyRequestDetail>();
        privacy.MapPost("/{id:guid}/conclude", async (Guid id, ConcludePrivacyRequestInput input, PrivacyRequestService service, CancellationToken ct) =>
                (await service.ConcludeAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("ConcludePrivacyRequest").Produces<PrivacyRequestDetail>();
        privacy.MapPost("/{id:guid}/refuse", async (Guid id, RefusePrivacyRequestInput input, PrivacyRequestService service, CancellationToken ct) =>
                (await service.RefuseAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("RefusePrivacyRequest").Produces<PrivacyRequestDetail>();
        privacy.MapPost("/{id:guid}/correction", async (Guid id, PrivacyVersionInput input, PrivacyRequestService service, CancellationToken ct) =>
                (await service.OpenCorrectionAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("OpenPrivacyCorrection").Produces<PrivacyCorrectionTarget>();
        privacy.MapPost("/{id:guid}/export", async (Guid id, PrivacyVersionInput input, PrivacyRequestService service, CancellationToken ct) =>
                (await service.StartExportAsync(id, input, ct)).ToHttpResult(r => Results.Accepted($"/api/v1/settings/privacy-requests/{id}", r)))
            .RequirePermission("settings.edit").WithName("ExportPrivacySubjectData").Produces<PrivacyExportStarted>(StatusCodes.Status202Accepted);
        privacy.MapPost("/{id:guid}/export/link", async (Guid id, PrivacyRequestService service, CancellationToken ct) =>
                (await service.CreateDownloadLinkAsync(id, ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("CreatePrivacyExportLink").Produces<PrivacyDownloadLink>();
        privacy.MapGet("/{id:guid}/export/download", async (Guid id, string? token, PrivacyRequestService service, CancellationToken ct) =>
                (await service.DownloadAsync(id, token, ct)).ToHttpResult(f => Results.File(f.Content, f.ContentType, f.FileName)))
            .RequirePermission("settings.edit").WithName("DownloadPrivacyExport").Produces(StatusCodes.Status200OK, contentType: PrivacyRequestService.ZipContentType);
        return api;
    }
}
