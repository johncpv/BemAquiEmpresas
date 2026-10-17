using System.Security.Claims;
using BemAqui.BuildingBlocks.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Identity.Endpoints;

public sealed class IdentityModule : IModule
{
    public string Code => "identity";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        // Sessão atual (a FND-04 completa com nome, grupos e permissões efetivas).
        endpoints.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new CurrentSession(user.FindFirstValue("sub"), user.FindFirstValue("tid"))))
            .WithName("GetCurrentSession")
            .WithTags("Identidade");
}

public sealed record CurrentSession(string? UserId, string? TenantId);
