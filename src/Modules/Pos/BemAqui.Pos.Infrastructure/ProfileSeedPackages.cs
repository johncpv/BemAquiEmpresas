using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Catalog.Contracts;
using BemAqui.Finance.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Pos.Application;
using BemAqui.Pos.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Pos.Infrastructure;

/// <summary>
/// Pacote do Comércio no PDV (PDV-01 CA 2): a loja "Loja" (código <c>LOJA</c>) ligada ao local "Loja" (pacote do Estoque) e à tabela "Balcão"
/// (Catálogo), com o caixa 01 na conta "Caixa PDV 01" (pacote do Financeiro). Roda depois desses pacotes. Pela <c>seed_key</c>: não duplica, não
/// muda o que o tenant alterou e não cria a loja se o código já estiver em uso ou faltar o local.
/// </summary>
public sealed class PosRetailSeedPackage : IProfileSeedPackage
{
    public const string StoreCode = "LOJA";
    public const string StoreName = "Loja";
    public const string CashAccountSeedKey = "caixa-pdv-01";

    public string Key => BusinessProfileSeeds.PosRetail;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IPosDb>();
        if (await db.Stores.AnyAsync(s => s.SeedKey == Store.RetailSeedKey || s.Code == StoreCode, cancellationToken))
        {
            return;
        }

        var locations = await tenantScope.GetRequiredService<ILocations>().ActiveAsync(cancellationToken);
        var location = locations.FirstOrDefault(l => string.Equals(l.Code, StoreCode, StringComparison.OrdinalIgnoreCase));
        if (location is null)
        {
            return;
        }

        var priceList = await tenantScope.GetRequiredService<IPriceLists>().DefaultAsync(cancellationToken);
        var clock = tenantScope.GetRequiredService<TimeProvider>();
        var store = new Store
        {
            SeedKey = Store.RetailSeedKey, Code = StoreCode, Name = StoreName, StockLocationId = location.Id, DefaultPriceListId = priceList.Id, CreatedAt = clock.GetUtcNow(),
        };
        db.Stores.Add(store);
        var accounts = await tenantScope.GetRequiredService<ICashAccounts>().ActiveAsync(cancellationToken);
        var cash = accounts.FirstOrDefault(a => a.SeedKey == CashAccountSeedKey);
        if (cash is not null && !await db.Registers.AnyAsync(r => r.CashAccountId == cash.Id, cancellationToken))
        {
            db.Registers.Add(new Register { StoreId = store.Id, Number = 1, CashAccountId = cash.Id, UiMode = RegisterUiMode.Keyboard, CreatedAt = clock.GetUtcNow() });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
