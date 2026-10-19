using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

public sealed partial class ItemService
{
    /// <summary>Inativar (CA 16): item com saldo não inativa. Reativar exige o NCM do tipo (CA 7).</summary>
    public async Task<Result<ItemDetail>> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        if (active && item.Ncm is null && Item.RequiresNcm(item.Type))
        {
            return ItemRules.Invalid("ncm", ItemRules.NcmRequired);
        }

        if (!active && await AnyAsync((u, ct) => u.HasBalanceAsync(id, ct), cancellationToken))
        {
            return Error.Conflict("item_has_balance", "O item tem saldo. Zere o saldo antes de inativar.");
        }

        item.IsActive = active;
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(item, cancellationToken);
    }

    /// <summary>Exclusão lógica só sem uso (CA 17): movimento, saldo, BOM, código do fornecedor ou documento.</summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        if (await AnyAsync((u, ct) => u.HasMovementAsync(id, ct), cancellationToken)
            || await AnyAsync((u, ct) => u.HasBalanceAsync(id, ct), cancellationToken)
            || await AnyAsync(async (u, ct) => await u.UsageAsync(id, ct) is not null, cancellationToken))
        {
            return Error.Conflict("item_in_use", "O item está em uso. Inative-o.");
        }

        db.Items.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static IQueryable<Item> Filter(IQueryable<Item> items, string? q)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return items;
        }

        var text = SearchText.Normalize(q);
        return items.Where(i => i.SearchText.Contains(text));
    }

    /// <summary>Unidade e categoria precisam existir e estar ativas, a não ser que o item já as use (inativadas depois).</summary>
    private async Task<Error?> CheckReferencesAsync(ItemInput input, Item? current, CancellationToken cancellationToken)
    {
        if (current?.UnitId != input.UnitId && !await db.Units.AnyAsync(u => u.Id == input.UnitId && u.IsActive, cancellationToken))
        {
            return ItemRules.Invalid("unitId", "Escolha uma unidade ativa da lista.");
        }

        if (input.CategoryId is { } category && current?.CategoryId != category && !await db.ItemCategories.AnyAsync(c => c.Id == category && c.IsActive, cancellationToken))
        {
            return ItemRules.Invalid("categoryId", "Escolha uma categoria ativa da lista.");
        }

        return null;
    }

    /// <summary>Com movimento, a unidade não troca e lote e série não desligam (CA 14).</summary>
    private async Task<Error?> LockedAsync(Item item, ItemInput input, CancellationToken cancellationToken)
    {
        var unit = item.UnitId != input.UnitId;
        var lot = item.TracksLot && !input.TracksLot;
        var serial = item.TracksSerial && !input.TracksSerial;
        if (!(unit || lot || serial) || !await AnyAsync((u, ct) => u.HasMovementAsync(item.Id, ct), cancellationToken))
        {
            return null;
        }

        return unit ? Error.Conflict("item_unit_locked", "Item com movimentação não pode trocar a unidade de estoque.")
            : lot ? Error.Conflict("item_lot_locked", "Item com movimentação não pode deixar de controlar lote.")
            : Error.Conflict("item_serial_locked", "Item com movimentação não pode deixar de controlar número de série.");
    }

    private async Task<bool> AnyAsync(Func<Contracts.IItemUsageProvider, CancellationToken, Task<bool>> check, CancellationToken cancellationToken)
    {
        foreach (var usage in usages)
        {
            if (await check(usage, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static void Apply(Item item, ItemInput input)
    {
        item.Type = input.Type;
        item.Code = input.Code;
        item.Name = input.Name;
        item.UnitId = input.UnitId;
        item.CategoryId = input.CategoryId;
        item.Ncm = input.Ncm;
        item.Cest = input.Cest;
        item.Origin = input.Origin;
        item.MinStock = input.MinStock;
        item.TracksLot = input.TracksLot;
        item.TracksSerial = input.TracksSerial;
        item.WarrantyMonths = input.WarrantyMonths;
    }

    private async Task<Result<ItemDetail>> SaveAsync(Item item, CancellationToken cancellationToken)
    {
        if (await db.Items.AnyAsync(i => i.Code == item.Code && i.Id != item.Id, cancellationToken))
        {
            return Duplicate(item.Code);
        }

        if (db.Entry(item).State == EntityState.Detached)
        {
            db.Items.Add(item);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, CodeIndex))
        {
            db.Entry(item).State = EntityState.Detached;
            return Duplicate(item.Code);
        }

        return await DetailAsync(item, cancellationToken);
    }

    private async Task<ItemDetail> DetailAsync(Item i, CancellationToken cancellationToken)
    {
        var unit = await db.Units.IgnoreQueryFilters().Where(u => u.Id == i.UnitId).Select(u => u.Code).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        var category = i.CategoryId is { } c ? await db.ItemCategories.IgnoreQueryFilters().Where(x => x.Id == c).Select(x => x.Name).FirstOrDefaultAsync(cancellationToken) : null;
        return new ItemDetail(i.Id, i.Type, i.Code, i.Name, i.UnitId, unit, i.CategoryId, category, i.Ncm, i.Cest, i.Origin, i.MinStock, i.TracksLot, i.TracksSerial,
            i.WarrantyMonths, i.AverageCost, i.IsActive, i.ImportSource, i.CreatedAt);
    }

    private static Error Duplicate(string code) => Error.Conflict("item_code_taken", $"Já existe o item {code}.");

    private static Error NotFound() => Error.NotFound("item_not_found", "Item não encontrado.");
}
