using BemAqui.Inventory.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary><see cref="IItemUnits"/> (ENG-02 CA 2): a unidade de estoque e as unidades com conversão cadastrada de cada item.</summary>
public sealed class ItemUnitsContract(IInventoryDb db) : IItemUnits
{
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ItemUnitOption>>> ForItemsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        if (itemIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<ItemUnitOption>>();
        }

        var items = await db.Items.IgnoreQueryFilters().AsNoTracking().Where(i => itemIds.Contains(i.Id)).Select(i => new { i.Id, i.UnitId }).ToListAsync(cancellationToken);
        var conversions = await db.ItemUnitConversions.AsNoTracking().Where(c => itemIds.Contains(c.ItemId))
            .Select(c => new { c.ItemId, c.FromUnitId, c.ToUnitId }).ToListAsync(cancellationToken);
        var codes = await CodesAsync(items.Select(i => i.UnitId).Concat(conversions.SelectMany(c => new[] { c.FromUnitId, c.ToUnitId })).Distinct().ToList(), cancellationToken);
        var byItem = conversions.ToLookup(c => c.ItemId);
        return items.ToDictionary(i => i.Id, i =>
        {
            var others = byItem[i.Id].Select(c => c.FromUnitId == i.UnitId ? c.ToUnitId : c.FromUnitId).Where(u => u != i.UnitId).Distinct()
                .Select(u => new ItemUnitOption(u, codes.GetValueOrDefault(u, "?"), false)).OrderBy(o => o.Code, StringComparer.Ordinal);
            return (IReadOnlyList<ItemUnitOption>)[new ItemUnitOption(i.UnitId, codes.GetValueOrDefault(i.UnitId, "?"), true), .. others];
        });
    }

    public async Task<IReadOnlyDictionary<Guid, string>> CodesAsync(IReadOnlyCollection<Guid> unitIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unitIds);
        return unitIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Units.IgnoreQueryFilters().AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Code, cancellationToken);
    }
}
