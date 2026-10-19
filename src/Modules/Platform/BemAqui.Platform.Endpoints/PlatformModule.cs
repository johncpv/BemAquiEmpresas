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
        // Nome e logo vêm do perfil da empresa no banco do tenant (CAD-06 CA 4 e 7); o logo só como URL, nunca data URI.
        endpoints.MapGet("/public/tenant-context", async (TenantContextHolder context, IServiceProvider services, CancellationToken ct) =>
            {
                if (context.Resolved is not { } t)
                {
                    return Results.NotFound();
                }

                TenantBrand? brand = null;
                try
                {
                    brand = t.Tenant.Status == TenantStatus.Active && services.GetService<ITenantBranding>() is { } branding ? await branding.GetAsync(ct) : null;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A tela de login nunca cai por causa do perfil (ex.: banco do tenant ainda não migrado): vale o catálogo.
                }

                var logo = brand?.LogoVersion is { } version ? "/api/v1/public/tenant-logo?v=" + version : null;
                return Results.Ok(new TenantPublicContext(brand?.Name ?? t.TradeName ?? t.LegalName, logo, t.Tenant.Status.ToString()));
            })
            .AllowAnonymous()
            .WithName("GetTenantPublicContext").Produces<TenantPublicContext>()
            .WithTags("Plataforma");

        endpoints.MapPlatformAuthEndpoints();
        endpoints.MapTenantModuleEndpoints();
        endpoints.MapPlatformTenantEndpoints();
    }
}

public sealed record TenantPublicContext(string Name, string? LogoUrl, string Status);
