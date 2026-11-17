using System.Security.Claims;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Platform.Application.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Platform.Endpoints;

public sealed record AddTenantProfileRequest(string Profile, string? Segment);

/// <summary>
/// Perfis de negócio na plataforma (PERF-02, ADR 0046): o catálogo para o provisionamento, os módulos de cada plano (aviso do que o
/// plano não cobre) e a aba "Perfis" do tenant. Só a sessão do super admin, no host da plataforma, como a aba "Módulos".
/// </summary>
internal static class TenantProfileEndpoints
{
    public static IEndpointRouteBuilder MapTenantProfileEndpoints(this IEndpointRouteBuilder api)
    {
        var platform = api.MapGroup("/platform")
            .WithTags("Plataforma")
            .RequireAuthorization(PlatformSessionAuthenticationHandler.Policy)
            .AddEndpointFilter(PlatformAuthEndpoints.RequireSameOriginOnPost);

        platform.MapGet("/business-profiles", () => Results.Ok(TenantModuleService.BusinessProfiles()))
            .WithName("ListBusinessProfiles").Produces<IReadOnlyList<BusinessProfileView>>();

        platform.MapGet("/plans/modules", async (TenantModuleService service, CancellationToken ct) => Results.Ok(await service.PlanModulesAsync(ct)))
            .WithName("ListPlanModules").Produces<IReadOnlyList<PlanModulesView>>();

        var profiles = platform.MapGroup("/tenants/{tenantId:guid}/profiles");

        profiles.MapGet(string.Empty, async (Guid tenantId, TenantModuleService service, CancellationToken ct) =>
                (await service.GetProfileTabAsync(tenantId, ct)).ToHttpResult())
            .WithName("GetTenantProfiles").Produces<TenantProfilesTab>();

        profiles.MapPost(string.Empty, async (Guid tenantId, AddTenantProfileRequest request, ClaimsPrincipal user, HttpContext http, TenantModuleService service, CancellationToken ct) =>
                (await service.AddProfileAsync(tenantId, request.Profile, request.Segment, AdminId(user), Ip(http), ct)).ToHttpResult())
            .WithName("AddTenantProfile").Produces<TenantProfileChange>();

        profiles.MapDelete("/{profile}", async (Guid tenantId, string profile, string? segment, ClaimsPrincipal user, HttpContext http, TenantModuleService service, CancellationToken ct) =>
                (await service.RemoveProfileAsync(tenantId, profile, string.IsNullOrWhiteSpace(segment) ? null : segment, AdminId(user), Ip(http), ct)).ToHttpResult())
            .WithName("RemoveTenantProfile");

        return api;
    }

    private static Guid? AdminId(ClaimsPrincipal user) => Guid.TryParse(user.FindFirstValue("sub"), out var id) ? id : null;

    private static string? Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();
}
