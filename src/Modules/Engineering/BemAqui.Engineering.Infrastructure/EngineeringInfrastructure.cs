using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Engineering.Application;
using BemAqui.Engineering.Contracts;
using BemAqui.Engineering.Domain;
using BemAqui.Inventory.Contracts;
using BemAqui.Production.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.Engineering.Infrastructure;

public static class EngineeringInfrastructure
{
    /// <summary>
    /// Roteiros (ENG-01): serviço, contrato para a Produção e o Comercial, uso de centro, seed e histórico. BOM da OS (ENG-02): serviço,
    /// <see cref="IBoms"/> para a Produção e o Estoque, uso de item e o saldo do estoque pelo IStockBalances (EST-01a).
    /// </summary>
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

        services.TryAddScoped<BomService>();
        services.TryAddScoped<IBoms, BomsContract>();
        // Saldo e custo médio pelo motor de estoque (EST-01a); "vai reservar" e o baixado por OS ligam na EST-02.
        services.TryAddScoped<IBomStock, StockBalancesBomStock>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IItemUsageProvider, BomItemUsage>());
        services.AddAuditableEntity<Bom>("engineering");
        services.AddAuditableEntity<BomLine>("engineering");
        return services;
    }
}
