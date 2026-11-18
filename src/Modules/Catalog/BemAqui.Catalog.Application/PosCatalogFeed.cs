using BemAqui.Catalog.Contracts;
using BemAqui.Inventory.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Catalog.Application;

/// <summary>
/// <see cref="IPosCatalogFeed"/> (OFF-01): o produto com códigos de barras, preços das tabelas pedidas e a unidade do item (pelo
/// <see cref="IItems"/>). Custo e margem não entram (CA 12). Inclui desativados e excluídos para o <c>Pos</c> marcar como removidos.
/// </summary>
public sealed class PosCatalogFeed(ICatalogDb db, IItems items) : IPosCatalogFeed
{
    public async Task<IReadOnlyList<PosFeedProduct>> FindAsync(IReadOnlyCollection<Guid> productIds, IReadOnlyCollection<Guid> priceListIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        ArgumentNullException.ThrowIfNull(priceListIds);
        if (productIds.Count == 0)
        {
            return [];
        }

        var ids = productIds.Distinct().ToList();
        var products = await db.Products.IgnoreQueryFilters().AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
        if (products.Count == 0)
        {
            return [];
        }

        var found = products.Select(p => p.Id).ToList();
        var barcodes = (await db.ProductBarcodes.AsNoTracking().Where(b => found.Contains(b.ProductId)).OrderBy(b => b.Gtin).ToListAsync(cancellationToken))
            .ToLookup(b => b.ProductId);
        var lists = priceListIds.Distinct().ToList();
        var versions = lists.Count == 0
            ? new Dictionary<Guid, long>()
            : await db.PriceLists.AsNoTracking().Where(l => lists.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Version, cancellationToken);
        var prices = lists.Count == 0
            ? []
            : await db.PriceListItems.AsNoTracking().Where(i => lists.Contains(i.PriceListId) && found.Contains(i.ProductId) && i.MinQuantity == 1m).ToListAsync(cancellationToken);
        var byProduct = prices.ToLookup(i => i.ProductId);
        var units = (await items.FindAsync(products.Select(p => p.ItemId).Distinct().ToList(), cancellationToken)).ToDictionary(i => i.Id, i => i.UnitCode);
        return products.Select(p => new PosFeedProduct(
            p.Id,
            p.VariantOfProductId,
            p.SaleName,
            units.GetValueOrDefault(p.ItemId, string.Empty),
            p.ItemId,
            p.IsWeighable,
            p.Plu,
            p.IsActive && !p.IsDeleted && p.SellablePos && !p.HasVariants,
            barcodes[p.Id].Select(b => new PosFeedBarcode(b.Gtin, b.PackQuantity)).ToList(),
            byProduct[p.Id].Where(i => versions.ContainsKey(i.PriceListId)).OrderBy(i => i.PriceListId)
                .Select(i => new PosFeedPrice(i.PriceListId, i.Price, versions[i.PriceListId])).ToList())).ToList();
    }

    public async Task<IReadOnlyList<Guid>> SellableIdsAsync(Guid? after, int take, CancellationToken cancellationToken = default)
    {
        var query = db.Products.AsNoTracking().Where(p => p.IsActive && p.SellablePos && !p.HasVariants);
        if (after is { } last)
        {
            query = query.Where(p => p.Id > last);
        }

        return await query.OrderBy(p => p.Id).Select(p => p.Id).Take(Math.Clamp(take, 1, 5000)).ToListAsync(cancellationToken);
    }
}
