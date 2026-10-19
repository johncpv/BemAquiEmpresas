using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

public sealed partial class ItemImport
{
    public async Task<Result<InventoryImportWrite>> CreateItemAsync(ItemImportData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (Validate(data, out var input) is { } invalid)
        {
            return invalid;
        }

        if (await db.Items.AnyAsync(i => i.Code == input.Code, cancellationToken))
        {
            return Error.Conflict("item_code_taken", $"Já existe o item {input.Code}.");
        }

        var item = new Item { CreatedAt = clock.GetUtcNow(), ImportSource = data.ImportSource, IsActive = data.IsActive };
        Apply(item, input);
        db.Items.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return await WriteAsync("items", item.Id, cancellationToken);
    }

    public async Task<Result<InventoryImportWrite>> UpdateItemAsync(Guid itemId, ItemImportData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);
        if (item is null)
        {
            return Error.NotFound("item_not_found", "Item não encontrado.");
        }

        if (Validate(data, out var input) is { } invalid)
        {
            return invalid;
        }

        Apply(item, input);
        item.IsActive = data.IsActive;
        item.ImportSource ??= data.ImportSource;
        await db.SaveChangesAsync(cancellationToken);
        return await WriteAsync("items", item.Id, cancellationToken);
    }

    public async Task<Result<InventoryImportWrite>> LinkItemAsync(Guid itemId, string importSource, CancellationToken cancellationToken = default)
    {
        var item = await db.Items.FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);
        if (item is null)
        {
            return Error.NotFound("item_not_found", "Item não encontrado.");
        }

        item.ImportSource ??= importSource;
        await db.SaveChangesAsync(cancellationToken);
        return await WriteAsync("items", item.Id, cancellationToken);
    }

    public async Task<ItemImportData?> CurrentItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var i = await db.Items.AsNoTracking().FirstOrDefaultAsync(x => x.Id == itemId, cancellationToken);
        return i is null ? null : new ItemImportData(i.Type, i.Code, i.Name, i.UnitId, i.CategoryId, i.Ncm, i.MinStock, i.TracksLot, i.TracksSerial, i.WarrantyMonths, i.IsActive, i.ImportSource ?? string.Empty);
    }

    public Task<long?> ItemVersionAsync(Guid itemId, CancellationToken cancellationToken = default) => VersionAsync("items", itemId, cancellationToken);

    public async Task<Result<InventoryImportWrite>> CreateSupplierCodeAsync(SupplierCodeImportData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var created = await supplierCodes.CreateAsync(data.ItemId, Input(data), cancellationToken);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        var row = await db.ItemSupplierCodes.FirstAsync(c => c.Id == created.Value.Id, cancellationToken);
        row.ImportSource = data.ImportSource;
        await db.SaveChangesAsync(cancellationToken);
        return await WriteAsync("item_supplier_codes", row.Id, cancellationToken);
    }

    public async Task<Result<InventoryImportWrite>> UpdateSupplierCodeAsync(Guid codeId, SupplierCodeImportData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var updated = await supplierCodes.UpdateAsync(data.ItemId, codeId, Input(data), cancellationToken);
        return updated.IsFailure ? updated.Error! : await WriteAsync("item_supplier_codes", codeId, cancellationToken);
    }

    public async Task<SupplierCodeImportData?> CurrentSupplierCodeAsync(Guid codeId, CancellationToken cancellationToken = default)
    {
        var c = await db.ItemSupplierCodes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == codeId, cancellationToken);
        return c is null ? null : new SupplierCodeImportData(c.ItemId, c.PartyId, c.SupplierCode, c.SupplierUnit, c.ConversionFactor, c.ImportSource ?? string.Empty);
    }

    public Task<long?> SupplierCodeVersionAsync(Guid codeId, CancellationToken cancellationToken = default) => VersionAsync("item_supplier_codes", codeId, cancellationToken);

    private static SupplierCodeInput Input(SupplierCodeImportData d) => new(d.PartyId, d.SupplierCode, d.SupplierUnit, d.ConversionFactor);

    /// <summary>As regras da tela, com a situação que a carga decidiu (o item sem NCM vem inativo, MIG-01c CA 7).</summary>
    private static Error? Validate(ItemImportData d, out ItemInput input) =>
        ItemRules.Validate(new ItemInput(d.Type, d.Code, d.Name, d.UnitId, d.CategoryId, d.Ncm, null, 0, d.MinStock, d.TracksLot, d.TracksSerial, d.WarrantyMonths), d.IsActive, out input);

    private static void Apply(Item item, ItemInput input)
    {
        item.Type = input.Type;
        item.Code = input.Code;
        item.Name = input.Name;
        item.UnitId = input.UnitId;
        item.CategoryId = input.CategoryId;
        item.Ncm = input.Ncm;
        item.MinStock = input.MinStock;
        item.TracksLot = input.TracksLot;
        item.TracksSerial = input.TracksSerial;
        item.WarrantyMonths = input.WarrantyMonths;
    }
}
