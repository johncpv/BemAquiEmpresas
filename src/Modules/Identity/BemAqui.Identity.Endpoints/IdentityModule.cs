using System.Reflection;
using System.Security.Claims;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.Identity.Application;
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
        endpoints.MapProfileTwoFactorEndpoints();
        endpoints.MapGroupEndpoints();
        endpoints.MapUserEndpoints();

        // Sessão atual: identidade, nome e e-mail (permissões em /me/permissions).
        endpoints.MapGet("/me", async (ClaimsPrincipal user, Profile profile, CancellationToken ct) =>
            {
                var id = user.FindFirstValue("sub");
                var me = Guid.TryParse(id, out var userId) ? await profile.GetAsync(userId, ct) : null;
                return Results.Ok(new CurrentSession(id, user.FindFirstValue("tid"), me?.Name ?? string.Empty, me?.Email ?? string.Empty));
            })
            .WithName("GetCurrentSession").Produces<CurrentSession>()
            .WithTags("Identidade");
    }
}

public sealed record CurrentSession(string? UserId, string? TenantId, string Name, string Email);
