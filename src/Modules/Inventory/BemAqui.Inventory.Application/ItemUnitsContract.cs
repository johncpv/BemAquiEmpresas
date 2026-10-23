using BemAqui.Inventory.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>
/// <see cref="IItemUnits"/> (ENG-02 CA 2): a unidade de estoque e as unidades com conversão cadastrada de cada item, com quanto 1 de cada
/// vale na unidade de estoque (a conversão grava "1 {de} = {fator} {para}", com uma das duas sempre a de estoque).
/// </summary>
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
            .Select(c => new { c.ItemId, c.FromUnitId, c.ToUnitId, c.Factor }).ToListAsync(cancellationToken);
        var codes = await CodesAsync(items.Select(i => i.UnitId).Concat(conversions.SelectMany(c => new[] { c.FromUnitId, c.ToUnitId })).Distinct().ToList(), cancellationToken);
        var byItem = conversions.ToLookup(c => c.ItemId);
        return items.ToDictionary(i => i.Id, i =>
        {
            var others = byItem[i.Id].Where(c => c.Factor > 0 && (c.FromUnitId == i.UnitId) != (c.ToUnitId == i.UnitId))
                .Select(c => c.ToUnitId == i.UnitId ? (Unit: c.FromUnitId, ToStock: c.Factor) : (Unit: c.ToUnitId, ToStock: 1m / c.Factor))
                .GroupBy(o => o.Unit).Select(g => g.First())
                .Select(o => new ItemUnitOption(o.Unit, codes.GetValueOrDefault(o.Unit, "?"), false, o.ToStock)).OrderBy(o => o.Code, StringComparer.Ordinal);
            return (IReadOnlyList<ItemUnitOption>)[new ItemUnitOption(i.UnitId, codes.GetValueOrDefault(i.UnitId, "?"), true, 1m), .. others];
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
