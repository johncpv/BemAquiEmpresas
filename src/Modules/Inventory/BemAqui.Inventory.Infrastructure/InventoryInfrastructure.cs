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
        // Unidades usáveis por item para a BOM (ENG-02 CA 2).
        services.TryAddScoped<IItemUnits, ItemUnitsContract>();
        // Custo médio por item para o custo previsto da BOM (ENG-02, só com projects.view_cost no chamador).
        services.TryAddScoped<IItemCosts, ItemCostsContract>();
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
        // Motor de estoque (EST-01a, ADR 0023): um só StockLedger atrás de IStockMovements e IStockBalances.
        services.TryAddScoped<StockLedger>();
        services.TryAddScoped<IStockMovements>(sp => sp.GetRequiredService<StockLedger>());
        services.TryAddScoped<IStockBalances>(sp => sp.GetRequiredService<StockLedger>());
        services.TryAddScoped<ReversePolicy>();
        services.TryAddScoped<StockQueryService>();
        services.TryAddScoped<ManualPostingService>();
        services.TryAddScoped<ManualDigestNotifier>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IItemUsageProvider, StockUsage>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ILocationUsageProvider, StockUsage>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantSeeder, InventoryParametersSeeder>());
        services.AddAuditableEntity<Lot>("inventory");
        services.AddAuditableEntity<ManualPosting>("inventory");
        services.AddAuditableEntity<ManualPostingLine>("inventory");
        services.AddAuditableEntity<InventoryParameters>("inventory");
        return services;
    }
}
