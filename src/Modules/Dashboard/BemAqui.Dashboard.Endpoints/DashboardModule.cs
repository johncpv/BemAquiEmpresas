using BemAqui.BuildingBlocks.Modules;
using BemAqui.Dashboard.Application;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Dashboard.Endpoints;

/// <summary>
/// Módulo <c>dashboard</c> (ADR 0028 §1): só de composição, sem <c>Domain</c>, <c>Infrastructure</c>, schema nem tabela; por
/// isso não tem <see cref="IModule.PersistenceAssembly"/> nem seeder (o <c>dashboard.view</c> já vem do catálogo e do seed dos grupos).
/// </summary>
public sealed class DashboardModule : IModule
{
    public string Code => "dashboard";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDashboard();
        services.Configure<RateLimiterOptions>(o => o.AddPolicy<string, DashboardRefreshPolicy>(DashboardRefreshPolicy.Name));
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapDashboardEndpoints().MapSupervisorEndpoints();
}
