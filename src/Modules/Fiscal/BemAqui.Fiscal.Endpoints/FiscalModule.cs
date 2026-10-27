using System.Reflection;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.Fiscal.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Fiscal.Endpoints;

public sealed class FiscalModule : IModule
{
    public string Code => "fiscal";

    public Assembly? PersistenceAssembly => typeof(FiscalInfrastructure).Assembly;

    public void AddServices(IServiceCollection services, IConfiguration configuration) => services.AddFiscalInfrastructure(configuration);

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapFiscalEndpoints();
}
