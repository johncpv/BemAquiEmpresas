using BemAqui.Inventory.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Inventory.Domain;

/// <summary>Unidade de medida (<c>inventory.units</c>, CAD-02a CA 1 e 2): código até 6 caracteres, maiúsculas, único.</summary>
public sealed class MeasureUnit : Entity, IAuditable, ISoftDeletable
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public string? SeedKey { get; init; }

    /// <summary>Origem da importação (ex.: <c>dolibarr:llx_c_units:3</c>), gravada só pela MIG-01c.</summary>
    public string? ImportSource { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }
}

/// <summary>Categoria de item em árvore (<c>inventory.item_categories</c>, CA 3): nome único entre as irmãs, sem ciclo.</summary>
public sealed class ItemCategory : Entity, IAuditable, ISoftDeletable
{
    public string Name { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Origem da importação (ex.: <c>dolibarr:llx_categorie:3</c>), gravada só pela MIG-01c.</summary>
    public string? ImportSource { get; set; }


    public DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }
}

/// <summary>Garantia padrão por tipo (<c>inventory.warranty_defaults</c>, CA 4 e 5, ADR 0016); null = sem garantia.</summary>
public sealed class WarrantyDefault : Entity, IAuditable
{
    public ItemType ItemType { get; init; }

    public int? Months { get; set; }
}
