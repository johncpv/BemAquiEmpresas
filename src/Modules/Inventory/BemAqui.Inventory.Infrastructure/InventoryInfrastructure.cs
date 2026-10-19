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
    /// <summary>Locais (CAD-03) e itens (CAD-02a): serviços, contratos <see cref="ILocations"/> e <see cref="IItems"/>, seeds e histórico.</summary>
    public static IServiceCollection AddInventoryInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<LocationService>();
        services.TryAddScoped<ILocations, LocationsContract>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantSeeder, LocationSeeder>());
        // Itens, unidades, categorias e garantia padrão (CAD-02a).
        services.TryAddScoped<ItemService>();
        services.TryAddScoped<UnitService>();
        services.TryAddScoped<CategoryService>();
        services.TryAddScoped<WarrantyDefaultService>();
        services.TryAddScoped<IItems, ItemsContract>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantSeeder, UnitSeeder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantSeeder, WarrantyDefaultSeeder>());
        services.AddAuditableEntity<Location>("inventory");
        services.AddAuditableEntity<MeasureUnit>("inventory");
        services.AddAuditableEntity<ItemCategory>("inventory");
        services.AddAuditableEntity<WarrantyDefault>("inventory");
        services.AddAuditableEntity<Item>("inventory");
        return services;
    }
}
