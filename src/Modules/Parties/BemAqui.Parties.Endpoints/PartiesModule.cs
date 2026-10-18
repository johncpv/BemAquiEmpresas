using System.Reflection;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.Parties.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Parties.Endpoints;

public sealed class PartiesModule : IModule
{
    public string Code => "parties";

    public Assembly? PersistenceAssembly => typeof(PartiesInfrastructure).Assembly;

    public void AddServices(IServiceCollection services, IConfiguration configuration) => services.AddPartiesInfrastructure();

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapPartyEndpoints();
}
