using BemAqui.Catalog.Contracts;
using BemAqui.Catalog.Domain;
using BemAqui.Inventory.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Catalog.Application;

/// <summary>
/// <see cref="ISellableProducts"/> e <see cref="ICatalogLookup"/> (CAT-01 CA 12 e 13, contrato com a VND-01): o produto vendável com o preço
/// vigente da tabela pedida e a unidade do item (pelo <see cref="IItems"/>). O pai de grade fica fora: vende-se a variação. As consultas usam os
/// índices do GTIN (único), do PLU e do trigrama do nome.
/// </summary>
public sealed class CatalogReadContract(ICatalogDb db, CatalogDefaults defaults, IItems items) : ISellableProducts, ICatalogLookup
{
    public async Task<SellableProduct?> FindAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var found = await FindManyAsync([productId], cancellationToken);
        return found.Count == 0 ? null : found[0];
    }

    public async Task<IReadOnlyList<SellableProduct>> FindManyAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        if (productIds.Count == 0)
        {
            return [];
        }

        var products = await db.Products.IgnoreQueryFilters().AsNoTracking().Where(p => productIds.Contains(p.Id) && !p.HasVariants).ToListAsync(cancellationToken);
        return await ProjectAsync(products, null, cancellationToken);
    }

    public async Task<CatalogLookupHit?> ByGtinAsync(string gtin, Guid? priceListId = null, CancellationToken cancellationToken = default)
    {
        var code = GtinRules.Normalize(gtin);
        if (code.Length == 0)
        {
            return null;
        }

        var barcode = await db.ProductBarcodes.AsNoTracking().FirstOrDefaultAsync(b => b.Gtin == code, cancellationToken);
        if (barcode is null)
        {
            return null;
        }

        var product = await Sellable().FirstOrDefaultAsync(p => p.Id == barcode.ProductId, cancellationToken);
        return product is null ? null : new CatalogLookupHit((await ProjectAsync([product], priceListId, cancellationToken))[0], CatalogLookupMatch.Gtin, barcode.Gtin, barcode.PackQuantity);
    }

    public async Task<CatalogLookupHit?> ByPluAsync(string plu, Guid? priceListId = null, CancellationToken cancellationToken = default)
    {
        var code = (plu ?? string.Empty).Trim();
        if (!ProductRules.IsValidPlu(code))
        {
            return null;
        }

        var product = await Sellable().FirstOrDefaultAsync(p => p.Plu == code, cancellationToken);
        return product is null ? null : new CatalogLookupHit((await ProjectAsync([product], priceListId, cancellationToken))[0], CatalogLookupMatch.Plu, null, 1m);
    }

    public async Task<IReadOnlyList<CatalogLookupHit>> SearchAsync(string term, int limit = 20, Guid? priceListId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2)
        {
            return [];
        }

        var text = SearchText.Normalize(term);
        var products = await Sellable().Where(p => p.SearchText.Contains(text)).OrderBy(p => p.SaleName).Take(Math.Clamp(limit, 1, 50)).ToListAsync(cancellationToken);
        return (await ProjectAsync(products, priceListId, cancellationToken)).Select(p => new CatalogLookupHit(p, CatalogLookupMatch.Name, null, 1m)).ToList();
    }

    private IQueryable<Product> Sellable() => db.Products.AsNoTracking().Where(p => p.IsActive && !p.HasVariants);

    private async Task<IReadOnlyList<SellableProduct>> ProjectAsync(List<Product> products, Guid? priceListId, CancellationToken ct)
    {
        if (products.Count == 0)
        {
            return [];
        }

        var list = priceListId is { } wanted
            ? await db.PriceLists.AsNoTracking().FirstOrDefaultAsync(l => l.Id == wanted, ct) ?? await defaults.DefaultPriceListAsync(ct)
            : await defaults.DefaultPriceListAsync(ct);
        var ids = products.Select(p => p.Id).ToList();
        var prices = await db.PriceListItems.AsNoTracking().Where(i => i.PriceListId == list.Id && ids.Contains(i.ProductId) && i.MinQuantity == 1m)
            .ToDictionaryAsync(i => i.ProductId, i => i.Price, ct);
        var found = (await items.FindAsync(products.Select(p => p.ItemId).Distinct().ToList(), ct)).ToDictionary(i => i.Id);
        return products.Select(p =>
        {
            var item = found.GetValueOrDefault(p.ItemId);
            return new SellableProduct(p.Id, p.VariantOfProductId, p.SaleName, item?.UnitId ?? Guid.Empty, item?.UnitCode ?? string.Empty, p.ItemId,
                prices.TryGetValue(p.Id, out var price) ? price : null, list.Id, list.Version, p.IsActive && !p.IsDeleted, p.IsWeighable, p.Plu);
        }).ToList();
    }
}

/// <summary>Item com produto no Catálogo não é excluído no Estoque (CA 4: um produto por item; o produto aponta para ele).</summary>
public sealed class CatalogItemUsage(ICatalogDb db) : IItemUsageProvider
{
    public Task<bool> HasMovementAsync(Guid itemId, CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<bool> HasBalanceAsync(Guid itemId, CancellationToken cancellationToken = default) => Task.FromResult(false);

    public async Task<string?> UsageAsync(Guid itemId, CancellationToken cancellationToken = default) =>
        await db.Products.AnyAsync(p => p.ItemId == itemId, cancellationToken) ? "produto do catálogo" : null;
}
