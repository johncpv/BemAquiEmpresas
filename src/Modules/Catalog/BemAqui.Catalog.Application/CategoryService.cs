using BemAqui.Catalog.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Catalog.Application;

/// <summary>
/// Categorias de produto (CA 14): nome, pai opcional, ordem e margem ou markup padrão (um dos dois). A "Geral" é a padrão e não sai; categoria
/// com produto não é excluída, só desativada. Sem ciclo na árvore.
/// </summary>
public sealed class CategoryService(ICatalogDb db, CatalogDefaults defaults, TimeProvider clock)
{
    public const int NameMax = 80;

    public async Task<IReadOnlyList<ProductCategoryDetail>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        await defaults.GeneralCategoryAsync(cancellationToken);
        return await db.ProductCategories.AsNoTracking().Where(c => includeInactive || c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new ProductCategoryDetail(c.Id, c.Name, c.ParentId, c.SortOrder, c.DefaultMarginPercent, c.DefaultMarkupPercent, c.IsActive,
                c.SeedKey == ProductCategory.GeneralSeedKey, db.Products.Count(p => p.CategoryId == c.Id)))
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<ProductCategoryDetail>> CreateAsync(ProductCategoryInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await ValidateAsync(input, null, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var category = new ProductCategory { CreatedAt = clock.GetUtcNow() };
        Apply(category, input);
        db.ProductCategories.Add(category);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(category.Id, cancellationToken);
    }

    public async Task<Result<ProductCategoryDetail>> UpdateAsync(Guid id, ProductCategoryInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var category = await db.ProductCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        if (await ValidateAsync(input, category, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        Apply(category, input);
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Desativar some do formulário do produto; os produtos dela continuam. A "Geral" não se desativa (é a padrão).</summary>
    public async Task<Result<ProductCategoryDetail>> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var category = await db.ProductCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        if (!active && category.SeedKey == ProductCategory.GeneralSeedKey)
        {
            return Error.Conflict("category_default", "A categoria Geral é a padrão dos produtos e não pode ser desativada.");
        }

        category.IsActive = active;
        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Exclusão lógica só sem produto e sem subcategoria (CA 14); com produto, 409 e a tela oferece "Desativar".</summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var category = await db.ProductCategories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        if (category.SeedKey == ProductCategory.GeneralSeedKey)
        {
            return Error.Conflict("category_default", "A categoria Geral é a padrão dos produtos e não pode ser excluída.");
        }

        if (await db.Products.IgnoreQueryFilters().AnyAsync(p => p.CategoryId == id, cancellationToken))
        {
            return Error.Conflict("category_in_use", "A categoria tem produtos. Desative-a.");
        }

        if (await db.ProductCategories.AnyAsync(c => c.ParentId == id, cancellationToken))
        {
            return Error.Conflict("category_has_children", "A categoria tem subcategorias. Mova ou exclua as subcategorias antes.");
        }

        category.MarkDeleted(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Result<ProductCategoryDetail>> GetAsync(Guid id, CancellationToken ct)
    {
        var detail = await db.ProductCategories.AsNoTracking().Where(c => c.Id == id)
            .Select(c => new ProductCategoryDetail(c.Id, c.Name, c.ParentId, c.SortOrder, c.DefaultMarginPercent, c.DefaultMarkupPercent, c.IsActive,
                c.SeedKey == ProductCategory.GeneralSeedKey, db.Products.Count(p => p.CategoryId == c.Id)))
            .FirstOrDefaultAsync(ct);
        return detail is null ? NotFound() : detail;
    }

    private static void Apply(ProductCategory category, ProductCategoryInput input)
    {
        category.Name = input.Name.Trim();
        category.ParentId = input.ParentId;
        category.SortOrder = input.SortOrder;
        category.DefaultMarginPercent = input.DefaultMarginPercent;
        category.DefaultMarkupPercent = input.DefaultMarkupPercent;
    }

    private async Task<Error?> ValidateAsync(ProductCategoryInput input, ProductCategory? current, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > NameMax)
        {
            return Invalid("name", $"Informe o nome da categoria, com até {NameMax} caracteres.");
        }

        if (input.DefaultMarginPercent is not null && input.DefaultMarkupPercent is not null)
        {
            return Invalid("defaultMarginPercent", "Escolha margem ou markup padrão, não os dois.");
        }

        if (input.DefaultMarginPercent is { } margin && (margin <= 0m || margin >= 100m || decimal.Round(margin, 4) != margin))
        {
            return Invalid("defaultMarginPercent", "A margem padrão fica entre 0 e 100%, sem incluir os extremos.");
        }

        if (input.DefaultMarkupPercent is { } markup && (markup <= 0m || markup > ProductRules.MaxPercent || decimal.Round(markup, 4) != markup))
        {
            return Invalid("defaultMarkupPercent", "O markup padrão é maior que zero.");
        }

        if (input.SortOrder is < 0 or > 9999)
        {
            return Invalid("sortOrder", "A ordem vai de 0 a 9999.");
        }

        var name = input.Name.Trim();
        if (await db.ProductCategories.AnyAsync(c => c.Name == name && c.ParentId == input.ParentId && (current == null || c.Id != current.Id), ct))
        {
            return Error.Conflict("category_name_taken", $"Já existe a categoria {name} neste nível.");
        }

        if (input.ParentId is { } parent)
        {
            if (current is not null && parent == current.Id)
            {
                return Invalid("parentId", "A categoria não pode ser pai dela mesma.");
            }

            var all = await db.ProductCategories.AsNoTracking().Select(c => new { c.Id, c.ParentId, c.IsActive }).ToListAsync(ct);
            var byId = all.ToDictionary(c => c.Id);
            if (!byId.TryGetValue(parent, out var p) || (!p.IsActive && current?.ParentId != parent))
            {
                return Invalid("parentId", "Escolha uma categoria pai ativa da lista.");
            }

            // Sem ciclo: subindo a partir do pai escolhido, não se pode chegar à própria categoria.
            for (Guid? at = parent; at is { } step && current is not null; at = byId.GetValueOrDefault(step)?.ParentId)
            {
                if (step == current.Id)
                {
                    return Invalid("parentId", "Essa escolha faria a categoria ficar dentro dela mesma. Escolha outro pai.");
                }
            }
        }

        return null;
    }

    private static Error Invalid(string field, string message) =>
        Error.Validation("validation", message, new Dictionary<string, string[]> { [field] = [message] });

    private static Error NotFound() => Error.NotFound("category_not_found", "Categoria não encontrada.");
}
