using System.Reflection;
using System.Security.Claims;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.Identity.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Identity.Endpoints;

public sealed class IdentityModule : IModule
{
    public string Code => "identity";

    public Assembly? PersistenceAssembly => typeof(IdentityInfrastructure).Assembly;

    public void AddServices(IServiceCollection services, IConfiguration configuration) => services.AddIdentityInfrastructure();

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapAuthEndpoints();

        // Sessão atual (a FND-06c completa com nome, grupos e permissões efetivas).
        endpoints.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new CurrentSession(user.FindFirstValue("sub"), user.FindFirstValue("tid"))))
            .WithName("GetCurrentSession")
            .WithTags("Identidade");
    }
}

public sealed record CurrentSession(string? UserId, string? TenantId);
