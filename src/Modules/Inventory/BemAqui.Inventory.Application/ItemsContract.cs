using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>Implementação de <see cref="IItems"/> (CA 21).</summary>
public sealed class ItemsContract(IInventoryDb db) : IItems
{
    public async Task<IReadOnlyList<ItemSummary>> SearchAsync(ItemType? type, string? term, int limit = 20, CancellationToken cancellationToken = default)
    {
        var items = db.Items.AsNoTracking().Where(i => i.IsActive);
        if (type is { } t)
        {
            items = items.Where(i => i.Type == t);
        }

        if (!string.IsNullOrWhiteSpace(term))
        {
            var text = SearchText.Normalize(term);
            items = items.Where(i => i.SearchText.Contains(text));
        }

        return await Project(items.OrderBy(i => i.Code).Take(Math.Clamp(limit, 1, 100))).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ItemSummary>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return await Project(db.Items.IgnoreQueryFilters().AsNoTracking().Where(i => ids.Contains(i.Id))).ToListAsync(cancellationToken);
    }

    private IQueryable<ItemSummary> Project(IQueryable<Item> items) => items.Select(i => new ItemSummary(
        i.Id, i.Code, i.Name, i.Type, i.UnitId,
        db.Units.IgnoreQueryFilters().Where(u => u.Id == i.UnitId).Select(u => u.Code).FirstOrDefault() ?? string.Empty,
        i.Ncm, i.TracksLot, i.TracksSerial, i.WarrantyMonths, i.IsActive, i.IsDeleted));
}
