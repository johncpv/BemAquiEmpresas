using System.Reflection;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.Inventory.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Inventory.Endpoints;

public sealed class InventoryModule : IModule
{
    public string Code => "inventory";

    public Assembly? PersistenceAssembly => typeof(InventoryInfrastructure).Assembly;

    public void AddServices(IServiceCollection services, IConfiguration configuration) => services.AddInventoryInfrastructure();

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapInventoryEndpoints().MapItemEndpoints().MapStockEndpoints();
}
