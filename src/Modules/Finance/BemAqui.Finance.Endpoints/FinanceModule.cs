using System.Reflection;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.Finance.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Finance.Endpoints;

public sealed class FinanceModule : IModule
{
    public string Code => "finance";

    public Assembly? PersistenceAssembly => typeof(FinanceInfrastructure).Assembly;

    public void AddServices(IServiceCollection services, IConfiguration configuration) => services.AddFinanceInfrastructure(configuration);

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapFinanceEndpoints().MapBankIntegrationEndpoints().MapPaymentNoticeEndpoints().MapBillingSettingsEndpoints();
}
