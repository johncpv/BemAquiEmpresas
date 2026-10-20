using System.Reflection;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.Commercial.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Commercial.Endpoints;

public sealed class CommercialModule : IModule
{
    public string Code => "commercial";

    public Assembly? PersistenceAssembly => typeof(CommercialInfrastructure).Assembly;

    public void AddServices(IServiceCollection services, IConfiguration configuration) => services.AddCommercialInfrastructure();

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapCommercialEndpoints();
}
