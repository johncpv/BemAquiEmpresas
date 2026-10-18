using System.Security.Claims;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Platform.Application.Provisioning;
using BemAqui.Platform.Application.Tenants;
using BemAqui.Platform.Infrastructure.Provisioning;
using Hangfire;
using Hangfire.States;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Platform.Endpoints;

public sealed record ProvisionTenantRequest(string Key, string LegalName, string? TradeName, string? Document, string Host, string OwnerEmail, string? Plan);

public sealed record SuspendTenantRequest(string Reason);

public sealed record ProvisionAccepted(string Key);

/// <summary>
/// Tenants no painel da plataforma (FND-03b), só no host da plataforma e com a sessão do super admin. Provisionar e
/// retomar rodam em job (fila maintenance); a tela acompanha pelo log de etapas.
/// </summary>
internal static class PlatformTenantEndpoints
{
    public static IEndpointRouteBuilder MapPlatformTenantEndpoints(this IEndpointRouteBuilder api)
    {
        var platform = api.MapGroup("/platform").WithTags("Plataforma").RequireAuthorization(PlatformSessionAuthenticationHandler.Policy).AddEndpointFilter(PlatformAuthEndpoints.RequireSameOriginOnPost);

        platform.MapGet("/plans", async (TenantAdminService service, CancellationToken ct) => Results.Ok(await service.PlansAsync(ct)))
            .WithName("ListPlans").Produces<IReadOnlyList<string>>();

        var tenants = platform.MapGroup("/tenants");
        tenants.MapGet(string.Empty, async (int? page, int? pageSize, string? sort, string? q, TenantAdminService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize, sort).Normalize(), q, ct)))
            .WithName("ListTenants").Produces<PagedList<TenantRow>>();

        tenants.MapGet("/{key}", async (string key, TenantAdminService service, CancellationToken ct) => (await service.GetAsync(key, ct)).ToHttpResult())
            .WithName("GetTenant").Produces<TenantDetail>();

        tenants.MapPost(string.Empty, async (ProvisionTenantRequest body, ClaimsPrincipal user, HttpContext http, TenantAdminService service, IBackgroundJobClient jobs, CancellationToken ct) =>
            {
                var request = new ProvisionRequest(body.Key?.Trim() ?? string.Empty, body.LegalName, body.Document, body.Host, body.OwnerEmail?.Trim() ?? string.Empty, body.Plan, TradeName: body.TradeName);
                if (await service.ValidateNewAsync(request, ct) is { } invalid)
                {
                    return invalid.ToProblem();
                }

                await service.AuditAsync(request.Key, "TenantProvisionRequested", AdminId(user), Ip(http), new { request.Key, request.LegalName, request.Host, request.Plan }, ct);
                jobs.Create<ProvisionTenantJob>(j => j.ExecuteAsync(request, CancellationToken.None), new EnqueuedState(JobQueues.Maintenance));
                return Results.Accepted("/api/v1/platform/tenants/" + request.Key, new ProvisionAccepted(request.Key));
            })
            .WithName("ProvisionTenant").Produces<ProvisionAccepted>(StatusCodes.Status202Accepted);

        tenants.MapPost("/{key}/resume", async (string key, ClaimsPrincipal user, HttpContext http, TenantAdminService service, IBackgroundJobClient jobs, CancellationToken ct) =>
            {
                var request = new ProvisionRequest(key, string.Empty, null, string.Empty, string.Empty, null, Resume: true);
                await service.AuditAsync(key, "TenantProvisionResumed", AdminId(user), Ip(http), new { key }, ct);
                jobs.Create<ProvisionTenantJob>(j => j.ExecuteAsync(request, CancellationToken.None), new EnqueuedState(JobQueues.Maintenance));
                return Results.Accepted("/api/v1/platform/tenants/" + key, new ProvisionAccepted(key));
            })
            .WithName("ResumeTenantProvisioning").Produces<ProvisionAccepted>(StatusCodes.Status202Accepted);

        tenants.MapPost("/{key}/suspend", async (string key, SuspendTenantRequest body, ClaimsPrincipal user, HttpContext http, TenantAdminService service, CancellationToken ct) =>
                (await service.SuspendAsync(key, body.Reason, AdminId(user), Ip(http), ct)).ToHttpResult())
            .WithName("SuspendTenant");

        tenants.MapPost("/{key}/reactivate", async (string key, ClaimsPrincipal user, HttpContext http, TenantAdminService service, CancellationToken ct) =>
                (await service.ReactivateAsync(key, AdminId(user), Ip(http), ct)).ToHttpResult())
            .WithName("ReactivateTenant");

        return api;
    }

    private static Guid? AdminId(ClaimsPrincipal user) => Guid.TryParse(user.FindFirstValue("sub"), out var id) ? id : null;

    private static string? Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();
}
