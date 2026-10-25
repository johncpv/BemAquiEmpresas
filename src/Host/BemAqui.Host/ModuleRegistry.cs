using BemAqui.Assets.Endpoints;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.Commercial.Endpoints;
using BemAqui.Dashboard.Endpoints;
using BemAqui.Engineering.Endpoints;
using BemAqui.Finance.Endpoints;
using BemAqui.Fiscal.Endpoints;
using BemAqui.HR.Endpoints;
using BemAqui.Identity.Endpoints;
using BemAqui.Inventory.Endpoints;
using BemAqui.Migrations.Endpoints;
using BemAqui.Parties.Endpoints;
using BemAqui.Platform.Endpoints;
using BemAqui.Portal.Endpoints;
using BemAqui.Production.Endpoints;
using BemAqui.Purchasing.Endpoints;

namespace BemAqui.Host;

/// <summary>Módulos compostos no Host. Lista explícita: módulo novo entra aqui.</summary>
public static class ModuleRegistry
{
    public static IReadOnlyList<IModule> All { get; } =
    [
        new PlatformModule(),
        new IdentityModule(),
        new PartiesModule(),
        new HRModule(),
        new CommercialModule(),
        new EngineeringModule(),
        new ProductionModule(),
        new InventoryModule(),
        new PurchasingModule(),
        new FinanceModule(),
        new FiscalModule(),
        new AssetsModule(),
        new PortalModule(),
        new MigrationModule(),
        // DSH-01a (ADR 0028 §1): módulo de composição, só Application e Endpoints, sem tabela nem seeder.
        new DashboardModule(),
    ];
}
