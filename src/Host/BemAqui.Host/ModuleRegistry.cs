using BemAqui.Assets.Endpoints;
using BemAqui.Catalog.Endpoints;
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
using BemAqui.Pos.Endpoints;
using BemAqui.Sales.Endpoints;

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
        new SalesModule(),
        // PDV-01: lojas, caixas e o aperto de mão do aparelho de caixa (oculto fora do perfil Comércio, ADR 0046).
        new PosModule(),
        new FiscalModule(),
        new AssetsModule(),
        // CAT-01: catálogo de produtos do PDV (oculto fora dos perfis de comércio, ADR 0046).
        new CatalogModule(),
        new PortalModule(),
        new MigrationModule(),
        // DSH-01a (ADR 0028 §1): módulo de composição, só Application e Endpoints, sem tabela nem seeder.
        new DashboardModule(),
    ];
}
