using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Catalog.Application;

/// <summary>
/// Cadastros padrão do Catálogo (CA 8 e 14): a categoria "Geral" e a tabela de preço "Balcão". Nascem no primeiro uso do módulo, e não por
/// seed no provisionamento: tenant que não usa o Catálogo (a Konebras) não ganha nenhuma linha. Gravação simultânea: o índice único da
/// <c>seed_key</c> segura, e quem perdeu relê.
/// </summary>
public sealed class CatalogDefaults(ICatalogDb db, TimeProvider clock)
{
    public const string GeneralName = "Geral";
    public const string DefaultListName = "Balcão";

    public async Task<ProductCategory> GeneralCategoryAsync(CancellationToken cancellationToken = default)
    {
        var existing = await db.ProductCategories.FirstOrDefaultAsync(c => c.SeedKey == ProductCategory.GeneralSeedKey, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var general = new ProductCategory { Name = GeneralName, SeedKey = ProductCategory.GeneralSeedKey, CreatedAt = clock.GetUtcNow() };
        db.ProductCategories.Add(general);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return general;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            db.Entry(general).State = EntityState.Detached;
            return await db.ProductCategories.FirstAsync(c => c.SeedKey == ProductCategory.GeneralSeedKey, cancellationToken);
        }
    }

    public async Task<PriceList> DefaultPriceListAsync(CancellationToken cancellationToken = default)
    {
        var existing = await db.PriceLists.FirstOrDefaultAsync(l => l.SeedKey == PriceList.DefaultSeedKey, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var list = new PriceList { Name = DefaultListName, SeedKey = PriceList.DefaultSeedKey, IsDefault = true, Channel = PriceChannel.Pos, CreatedAt = clock.GetUtcNow() };
        db.PriceLists.Add(list);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return list;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            db.Entry(list).State = EntityState.Detached;
            return await db.PriceLists.FirstAsync(l => l.SeedKey == PriceList.DefaultSeedKey, cancellationToken);
        }
    }

    /// <summary>Os dois de uma vez, antes de abrir a transação de quem grava (a falha do índice único não pode abortar a transação).</summary>
    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        await GeneralCategoryAsync(cancellationToken);
        await DefaultPriceListAsync(cancellationToken);
    }
}
