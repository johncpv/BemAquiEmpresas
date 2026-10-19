using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Itens (CAD-02a CA 6 a 20). Código único (índice parcial), regras por tipo em <see cref="ItemRules"/>, unidade e indicadores
/// de lote e série travados com movimento, inativar e excluir conforme os <see cref="IItemUsageProvider"/> (D10).
/// </summary>
public sealed partial class ItemService(IInventoryDb db, IEnumerable<IItemUsageProvider> usages, TimeProvider clock)
{
    public const string CodeIndex = "ix_items_code";

    public async Task<PagedList<ItemListItem>> ListAsync(ItemListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);
        var items = Filter(db.Items.AsNoTracking(), query.Q);
        if (!query.IncludeInactive)
        {
            items = items.Where(i => i.IsActive);
        }

        if (query.Type is { } type)
        {
            items = items.Where(i => i.Type == type);
        }

        if (query.CategoryId is { } category)
        {
            items = items.Where(i => i.CategoryId == category);
        }

        items = page.ParseSort() switch
        {
            ("name", true) => items.OrderByDescending(i => i.Name),
            ("name", false) => items.OrderBy(i => i.Name),
            ("code", true) => items.OrderByDescending(i => i.Code),
            _ => items.OrderBy(i => i.Code),
        };

        var paged = await items.Select(i => new ItemListItem(
                i.Id, i.Code, i.Name, i.Type,
                db.ItemCategories.IgnoreQueryFilters().Where(c => c.Id == i.CategoryId).Select(c => c.Name).FirstOrDefault(),
                db.Units.IgnoreQueryFilters().Where(u => u.Id == i.UnitId).Select(u => u.Code).FirstOrDefault() ?? string.Empty,
                i.Ncm, i.TracksLot, i.TracksSerial, i.MinStock, i.IsActive))
            .ToPagedListAsync(page, cancellationToken);
        return paged;
    }

    /// <summary>Busca da topbar (CA 20): a partir de 2 caracteres, até 5 ativos.</summary>
    public async Task<IReadOnlyList<ItemSearchHit>> SearchHitsAsync(string? q, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return [];
        }

        return await Filter(db.Items.AsNoTracking().Where(i => i.IsActive), q).OrderBy(i => i.Code).Take(5)
            .Select(i => new ItemSearchHit(i.Id, i.Code, i.Name, i.Type)).ToListAsync(cancellationToken);
    }

    public async Task<Result<ItemDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        return item is null ? NotFound() : await DetailAsync(item, cancellationToken);
    }

    public async Task<Result<ItemDetail>> CreateAsync(ItemInput input, CancellationToken cancellationToken = default)
    {
        if (ItemRules.Validate(input, true, out var valid) is { } invalid)
        {
            return invalid;
        }

        if (await CheckReferencesAsync(valid, null, cancellationToken) is { } badRef)
        {
            return badRef;
        }

        var item = new Item { CreatedAt = clock.GetUtcNow() };
        Apply(item, valid);
        return await SaveAsync(item, cancellationToken);
    }

    public async Task<Result<ItemDetail>> UpdateAsync(Guid id, ItemInput input, CancellationToken cancellationToken = default)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        if (ItemRules.Validate(input, item.IsActive, out var valid) is { } invalid)
        {
            return invalid;
        }

        if (await CheckReferencesAsync(valid, item, cancellationToken) is { } badRef)
        {
            return badRef;
        }

        if (await LockedAsync(item, valid, cancellationToken) is { } locked)
        {
            return locked;
        }

        Apply(item, valid);
        return await SaveAsync(item, cancellationToken);
    }
}
