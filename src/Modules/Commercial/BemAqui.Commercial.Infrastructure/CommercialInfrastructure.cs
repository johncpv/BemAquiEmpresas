using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Commercial.Application;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.Commercial.Infrastructure;

public static class CommercialInfrastructure
{
    /// <summary>Orçamentos (COM-01a): serviços, parâmetros, uso dos cadastros e histórico.</summary>
    public static IServiceCollection AddCommercialInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<QuoteService>();
        services.TryAddScoped<QuoteLookups>();
        services.TryAddScoped<CommercialParametersService>();
        services.TryAddScoped<ICommercialParameters>(sp => sp.GetRequiredService<CommercialParametersService>());
        services.TryAddScoped<CommercialUsage>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPartyUsageProvider, CommercialUsage>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IItemUsageProvider, CommercialUsage>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkCenterUsageProvider, CommercialUsage>());
        services.AddAuditableEntity<Quote>("quotes");
        services.AddAuditableEntity<QuoteItem>("quotes");
        services.AddAuditableEntity<QuoteItemCost>("quotes", "view_cost");
        services.AddAuditableEntity<QuoteMilestone>("quotes");
        services.AddAuditableEntity<CommercialParameter>("settings");
        return services;
    }
}
