using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Engineering.Application;
using BemAqui.Engineering.Contracts;
using BemAqui.Engineering.Domain;
using BemAqui.Production.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.Engineering.Infrastructure;

public static class EngineeringInfrastructure
{
    /// <summary>Roteiros (ENG-01): serviço, contrato para a Produção e o Comercial, uso de centro, seed e histórico.</summary>
    public static IServiceCollection AddEngineeringInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<RoutingTemplateService>();
        services.TryAddScoped<IRoutingTemplates, RoutingTemplatesContract>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkCenterUsageProvider, RoutingWorkCenterUsage>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantSeeder, RoutingTemplateSeeder>());
        services.AddAuditableEntity<RoutingTemplate>("engineering");
        services.AddAuditableEntity<RoutingTemplateStep>("engineering");
        services.AddAuditableEntity<RoutingTemplateItem>("engineering");
        return services;
    }
}