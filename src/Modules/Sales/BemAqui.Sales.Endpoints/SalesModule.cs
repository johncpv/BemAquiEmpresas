using System.Reflection;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.Sales.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Sales.Endpoints;

/// <summary>Núcleo de vendas (VND-01): dono do schema <c>sales</c> e das rotas <c>/api/v1/sales/…</c> (02, seção 4.1).</summary>
public sealed class SalesModule : IModule
{
    public string Code => "sales";

    public Assembly? PersistenceAssembly => typeof(SalesInfrastructure).Assembly;

    public void AddServices(IServiceCollection services, IConfiguration configuration) => services.AddSalesInfrastructure();

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapSalesEndpoints();
}
