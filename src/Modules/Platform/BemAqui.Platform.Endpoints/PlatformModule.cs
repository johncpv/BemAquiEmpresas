using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Platform.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Platform.Endpoints;

public sealed class PlatformModule : IModule
{
    public string Code => "platform";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddPlatformInfrastructure(configuration);
        PlatformSessionAuthenticationHandler.Register(services);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Rota anônima (ADR 0009): só o que a tela de login precisa mostrar antes de autenticar.
        endpoints.MapGet("/public/tenant-context", (TenantContextHolder context) =>
                context.Resolved is { } t
                    ? Results.Ok(new TenantPublicContext(t.TradeName ?? t.LegalName, LogoUrl: null, t.Tenant.Status.ToString()))
                    : Results.NotFound())
            .AllowAnonymous()
            .WithName("GetTenantPublicContext")
            .WithTags("Plataforma");

        endpoints.MapPlatformAuthEndpoints();
        endpoints.MapTenantModuleEndpoints();
    }
}

public sealed record TenantPublicContext(string Name, string? LogoUrl, string Status);
