using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.Inventory.Infrastructure;

public static class InventoryInfrastructure
{
    /// <summary>Locais de estoque (CAD-03): serviço, contrato <see cref="ILocations"/>, seed do ALM e histórico.</summary>
    public static IServiceCollection AddInventoryInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<LocationService>();
        services.TryAddScoped<ILocations, LocationsContract>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantSeeder, LocationSeeder>());
        services.AddAuditableEntity<Location>("inventory");
        return services;
    }
}
