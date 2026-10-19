using System.Reflection;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.HR.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.HR.Endpoints;

public sealed class HRModule : IModule
{
    public string Code => "hr";

    public Assembly? PersistenceAssembly => typeof(HrInfrastructure).Assembly;

    public void AddServices(IServiceCollection services, IConfiguration configuration) => services.AddHrInfrastructure();

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapHrEndpoints();
}
