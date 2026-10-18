using System.Security.Claims;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Platform.Application.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Platform.Endpoints;

public sealed record SaveModuleReleaseRequest(IReadOnlyList<ModuleReleaseChange> Modules);

/// <summary>Aba "Módulos" do tenant no painel da plataforma (FND-06 CA 5 a 8; a tela vem na FND-03b).</summary>
internal static class TenantModuleEndpoints
{
    public static IEndpointRouteBuilder MapTenantModuleEndpoints(this IEndpointRouteBuilder api)
    {
        var tenants = api.MapGroup("/platform/tenants/{tenantId:guid}/modules")
            .WithTags("Plataforma")
            .RequireAuthorization(PlatformSessionAuthenticationHandler.Policy)
            .AddEndpointFilter(PlatformAuthEndpoints.RequireSameOriginOnPost);

        tenants.MapGet(string.Empty, async (Guid tenantId, TenantModuleService service, CancellationToken ct) =>
                (await service.GetAsync(tenantId, ct)).ToHttpResult())
            .WithName("GetTenantModules").Produces<IReadOnlyList<ModuleRelease>>();

        tenants.MapPut(string.Empty, async (Guid tenantId, SaveModuleReleaseRequest request, ClaimsPrincipal user, HttpContext http, TenantModuleService service, CancellationToken ct) =>
                (await service.SaveAsync(tenantId, request.Modules, Guid.TryParse(user.FindFirstValue("sub"), out var adminId) ? adminId : null, http.Connection.RemoteIpAddress?.ToString(), ct)).ToHttpResult())
            .WithName("SaveTenantModules");

        return api;
    }
}
