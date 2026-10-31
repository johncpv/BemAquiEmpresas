using System.Reflection;
using BemAqui.Assets.Infrastructure;
using BemAqui.BuildingBlocks.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Assets.Endpoints;

public sealed class AssetsModule : IModule
{
    public string Code => "assets";

    public Assembly? PersistenceAssembly => typeof(AssetsInfrastructure).Assembly;

    public void AddServices(IServiceCollection services, IConfiguration configuration) => services.AddAssetsInfrastructure();

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapAssetsEndpoints();
}
