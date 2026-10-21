using System.Reflection;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.Engineering.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Engineering.Endpoints;

public sealed class EngineeringModule : IModule
{
    public string Code => "engineering";

    public Assembly? PersistenceAssembly => typeof(EngineeringInfrastructure).Assembly;

    public void AddServices(IServiceCollection services, IConfiguration configuration) => services.AddEngineeringInfrastructure();

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapEngineeringEndpoints();
}