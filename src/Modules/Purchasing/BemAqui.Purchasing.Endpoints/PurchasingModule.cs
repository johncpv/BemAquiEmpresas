using System.Reflection;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.Purchasing.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Purchasing.Endpoints;

public sealed class PurchasingModule : IModule
{
    public string Code => "purchasing";

    public Assembly? PersistenceAssembly => typeof(PurchasingInfrastructure).Assembly;

    public void AddServices(IServiceCollection services, IConfiguration configuration) => services.AddPurchasingInfrastructure();

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapPurchasingEndpoints();
}
