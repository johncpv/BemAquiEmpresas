using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>Categorias em árvore (CAD-02a CA 3): nome único entre as irmãs, sem ciclo; com itens ou filhas, só inativa.</summary>
public sealed class CategoryService(IInventoryDb db, TimeProvider clock)
{
    public async Task<IReadOnlyList<CategoryDetail>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.ItemCategories.AsNoTracking().OrderBy(c => c.Name)
            .Select(c => new CategoryDetail(c.Id, c.Name, c.ParentId, c.IsActive, db.Items.Count(i => i.CategoryId == c.Id), db.ItemCategories.Count(k => k.ParentId == c.Id), c.ImportSource))
            .ToListAsync(cancellationToken);

    public async Task<Result<CategoryDetail>> CreateAsync(CategoryInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var category = new ItemCategory { CreatedAt = clock.GetUtcNow() };
        if (await ValidateAsync(category, input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        db.ItemCategories.Add(category);
        return await SaveAsync(category, input, cancellationToken);
    }

    public async Task<Result<CategoryDetail>> UpdateAsync(Guid id, CategoryInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var category = await db.ItemCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        return await ValidateAsync(category, input, cancellationToken) is { } invalid ? invalid : await SaveAsync(category, input, cancellationToken);
    }

    public async Task<Result<CategoryDetail>> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var category = await db.ItemCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        category.IsActive = active;
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(category, cancellationToken);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await db.ItemCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        if (await db.Items.IgnoreQueryFilters().AnyAsync(i => i.CategoryId == id, cancellationToken) || await db.ItemCategories.AnyAsync(c => c.ParentId == id, cancellationToken))
        {
            return Error.Conflict("category_in_use", "A categoria tem itens ou subcategorias. Inative-a.");
        }

        db.ItemCategories.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Error?> ValidateAsync(ItemCategory category, CategoryInput input, CancellationToken cancellationToken)
    {
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > 80)
        {
            return ItemRules.Invalid("name", "Informe o nome da categoria, com até 80 caracteres.");
        }

        if (input.ParentId is { } parentId)
        {
            if (!await db.ItemCategories.AnyAsync(c => c.Id == parentId, cancellationToken))
            {
                return ItemRules.Invalid("parentId", "Escolha a categoria pai da lista.");
            }

            // Sobe pelos pais: se chegar nela mesma, seria ciclo (CA 3).
            var parents = await db.ItemCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.ParentId, cancellationToken);
            for (Guid? current = parentId; current is { } at; current = parents.GetValueOrDefault(at))
            {
                if (at == category.Id)
                {
                    return ItemRules.Invalid("parentId", "A categoria não pode ficar dentro dela mesma nem de uma subcategoria dela.");
                }
            }
        }

        var siblings = await db.ItemCategories.AsNoTracking().Where(c => c.ParentId == input.ParentId && c.Id != category.Id).Select(c => c.Name).ToListAsync(cancellationToken);
        return siblings.Any(s => string.Equals(SearchText.Normalize(s), SearchText.Normalize(name), StringComparison.Ordinal))
            ? Error.Conflict("category_name_taken", $"Já existe a categoria {name} neste nível.")
            : null;
    }

    private async Task<Result<CategoryDetail>> SaveAsync(ItemCategory category, CategoryInput input, CancellationToken cancellationToken)
    {
        category.Name = input.Name.Trim();
        category.ParentId = input.ParentId;
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(category, cancellationToken);
    }

    private async Task<CategoryDetail> DetailAsync(ItemCategory c, CancellationToken cancellationToken) =>
        new(c.Id, c.Name, c.ParentId, c.IsActive, await db.Items.CountAsync(i => i.CategoryId == c.Id, cancellationToken), await db.ItemCategories.CountAsync(k => k.ParentId == c.Id, cancellationToken), c.ImportSource);

    private static Error NotFound() => Error.NotFound("category_not_found", "Categoria não encontrada.");
}
