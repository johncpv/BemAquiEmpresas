using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>
/// <see cref="IItemImport"/> (MIG-01c): unidades, categorias, itens e códigos do fornecedor com as regras da tela (CAD-02a e
/// CAD-02b). O item nunca recebe custo médio nem saldo (ADR 0017).
/// </summary>
public sealed partial class ItemImport(IInventoryDb db, ItemSupplierCodeService supplierCodes, TimeProvider clock) : IItemImport
{
    public async Task<Guid?> FindUnitAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        return await db.Units.Where(u => u.Code == normalized).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Result<InventoryImportWrite>> CreateUnitAsync(string code, string name, string importSource, CancellationToken cancellationToken = default)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized.Length is 0 or > 6 || !normalized.All(char.IsAsciiLetterOrDigit))
        {
            return ItemRules.Invalid("code", $"O código de unidade {code} não vale no BemAqui (até 6 letras ou números).");
        }

        var unit = new MeasureUnit { Code = normalized, Name = Trim(string.IsNullOrWhiteSpace(name) ? normalized : name, 60), ImportSource = importSource, CreatedAt = clock.GetUtcNow() };
        db.Units.Add(unit);
        await db.SaveChangesAsync(cancellationToken);
        return await WriteAsync("units", unit.Id, cancellationToken);
    }

    public async Task<Result<InventoryImportWrite>> LinkUnitAsync(Guid unitId, string importSource, CancellationToken cancellationToken = default)
    {
        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == unitId, cancellationToken);
        if (unit is null)
        {
            return Error.NotFound("unit_not_found", "Unidade não encontrada.");
        }

        unit.ImportSource ??= importSource;
        await db.SaveChangesAsync(cancellationToken);
        return await WriteAsync("units", unit.Id, cancellationToken);
    }

    public Task<long?> UnitVersionAsync(Guid unitId, CancellationToken cancellationToken = default) => VersionAsync("units", unitId, cancellationToken);

    public async Task<Result<InventoryImportWrite>> CreateCategoryAsync(string name, Guid? parentId, string importSource, CancellationToken cancellationToken = default)
    {
        var category = new ItemCategory { Name = Trim(name, 80), ParentId = parentId, ImportSource = importSource, CreatedAt = clock.GetUtcNow() };
        if (category.Name.Length == 0)
        {
            return ItemRules.Invalid("name", "Categoria sem nome.");
        }

        db.ItemCategories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        return await WriteAsync("item_categories", category.Id, cancellationToken);
    }

    public async Task<Result<InventoryImportWrite>> UpdateCategoryAsync(Guid categoryId, string name, Guid? parentId, CancellationToken cancellationToken = default)
    {
        var category = await db.ItemCategories.FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken);
        if (category is null)
        {
            return Error.NotFound("category_not_found", "Categoria não encontrada.");
        }

        category.Name = Trim(name, 80);
        category.ParentId = parentId == categoryId ? category.ParentId : parentId;
        await db.SaveChangesAsync(cancellationToken);
        return await WriteAsync("item_categories", category.Id, cancellationToken);
    }

    public async Task<string?> CategoryNameAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        await db.ItemCategories.Where(c => c.Id == categoryId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken);

    public Task<long?> CategoryVersionAsync(Guid categoryId, CancellationToken cancellationToken = default) => VersionAsync("item_categories", categoryId, cancellationToken);

    public async Task<int?> DefaultWarrantyAsync(ItemType type, CancellationToken cancellationToken = default) =>
        await db.WarrantyDefaults.Where(w => w.ItemType == type).Select(w => w.Months).FirstOrDefaultAsync(cancellationToken);

    public async Task<Guid?> FindItemAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = (code ?? string.Empty).Trim();
        return await db.Items.Where(i => i.Code == normalized).Select(i => (Guid?)i.Id).FirstOrDefaultAsync(cancellationToken);
    }

    private static string Trim(string? value, int max)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length > max ? text[..max] : text;
    }

    private async Task<Result<InventoryImportWrite>> WriteAsync(string table, Guid id, CancellationToken cancellationToken) =>
        new InventoryImportWrite(id, await VersionAsync(table, id, cancellationToken) ?? 0);

    /// <summary><c>xmin</c> da linha, para a idempotência da MIG-01a. A tabela vem de uma lista fixa deste arquivo, nunca da origem.</summary>
    private Task<long?> VersionAsync(string table, Guid id, CancellationToken cancellationToken) => table switch
    {
        "units" => db.Database.SqlQuery<long?>($"SELECT xmin::text::bigint AS \"Value\" FROM inventory.units WHERE id = {id} AND NOT is_deleted").SingleOrDefaultAsync(cancellationToken),
        "item_categories" => db.Database.SqlQuery<long?>($"SELECT xmin::text::bigint AS \"Value\" FROM inventory.item_categories WHERE id = {id} AND NOT is_deleted").SingleOrDefaultAsync(cancellationToken),
        "items" => db.Database.SqlQuery<long?>($"SELECT xmin::text::bigint AS \"Value\" FROM inventory.items WHERE id = {id} AND NOT is_deleted").SingleOrDefaultAsync(cancellationToken),
        _ => db.Database.SqlQuery<long?>($"SELECT xmin::text::bigint AS \"Value\" FROM inventory.item_supplier_codes WHERE id = {id} AND NOT is_deleted").SingleOrDefaultAsync(cancellationToken),
    };
}
