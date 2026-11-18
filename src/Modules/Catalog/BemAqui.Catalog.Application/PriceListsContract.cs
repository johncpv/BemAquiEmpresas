using BemAqui.Catalog.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Catalog.Application;

/// <summary><see cref="IPriceLists"/> (PDV-01 CA 2): as tabelas de preço para a loja do PDV, com a "Balcão" criada no primeiro uso.</summary>
public sealed class PriceListsContract(ICatalogDb db, CatalogDefaults defaults) : IPriceLists
{
    public async Task<IReadOnlyList<PriceListOption>> ActiveAsync(CancellationToken cancellationToken = default)
    {
        await defaults.DefaultPriceListAsync(cancellationToken);
        return await db.PriceLists.AsNoTracking().Where(l => l.IsActive).OrderByDescending(l => l.IsDefault).ThenBy(l => l.Name)
            .Select(l => new PriceListOption(l.Id, l.Name, l.IsActive, l.IsDefault)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PriceListOption>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return ids.Count == 0
            ? []
            : await db.PriceLists.AsNoTracking().Where(l => ids.Contains(l.Id)).Select(l => new PriceListOption(l.Id, l.Name, l.IsActive, l.IsDefault)).ToListAsync(cancellationToken);
    }

    public async Task<PriceListOption> DefaultAsync(CancellationToken cancellationToken = default)
    {
        var list = await defaults.DefaultPriceListAsync(cancellationToken);
        return new PriceListOption(list.Id, list.Name, list.IsActive, list.IsDefault);
    }
}
