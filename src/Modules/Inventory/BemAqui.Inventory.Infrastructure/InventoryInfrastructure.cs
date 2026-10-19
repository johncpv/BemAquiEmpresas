using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.Parties.Contracts;
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
        // Escrita da importação do Dolibarr (MIG-01c): só o módulo Migrations usa.
        services.TryAddScoped<IItemImport, ItemImport>();
        services.TryAddScoped<ILocationImport, LocationImport>();
        // Conversões e códigos do fornecedor (CAD-02b).
        services.TryAddScoped<ItemConversionService>();
        services.TryAddScoped<IUnitConversion>(sp => sp.GetRequiredService<ItemConversionService>());
        services.TryAddScoped<ItemSupplierCodeService>();
        services.TryAddScoped<IItemSupplierCodes>(sp => sp.GetRequiredService<ItemSupplierCodeService>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPartyUsageProvider, ItemSupplierCodeService>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantSeeder, UnitSeeder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantSeeder, WarrantyDefaultSeeder>());
        services.AddAuditableEntity<Location>("inventory");
        services.AddAuditableEntity<MeasureUnit>("inventory");
        services.AddAuditableEntity<ItemCategory>("inventory");
        services.AddAuditableEntity<WarrantyDefault>("inventory");
        services.AddAuditableEntity<Item>("inventory");
        services.AddAuditableEntity<ItemUnitConversion>("inventory");
        services.AddAuditableEntity<ItemSupplierCode>("inventory");
        return services;
    }
}
