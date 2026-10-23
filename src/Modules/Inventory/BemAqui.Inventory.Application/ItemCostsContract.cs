using BemAqui.Inventory.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary><see cref="IItemCosts"/> (ENG-02): o custo médio gravado no item pelo motor de estoque (EST-01a).</summary>
public sealed class ItemCostsContract(IInventoryDb db) : IItemCosts
{
    public async Task<IReadOnlyDictionary<Guid, decimal>> AverageCostsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        return itemIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await db.Items.IgnoreQueryFilters().AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.AverageCost, cancellationToken);
    }
}
