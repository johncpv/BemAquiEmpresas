using System.Reflection;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.Production.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Production.Endpoints;

public sealed class ProductionModule : IModule
{
    public string Code => "production";

    public Assembly? PersistenceAssembly => typeof(ProductionInfrastructure).Assembly;

    public void AddServices(IServiceCollection services, IConfiguration configuration) => services.AddProductionInfrastructure();

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapProductionEndpoints();
}
