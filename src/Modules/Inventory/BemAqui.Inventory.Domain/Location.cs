using BemAqui.SharedKernel;

namespace BemAqui.Inventory.Domain;

/// <summary>
/// Local de estoque (<c>inventory.locations</c>, CAD-03, R-EST-2). Código único entre os não excluídos e um só padrão
/// (índices únicos parciais); o padrão não é inativado nem excluído.
/// </summary>
public sealed class Location : Entity, IAuditable, ISoftDeletable
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public bool IsDefault { get; set; }

    /// <summary>Chave do seed (<c>alm</c>); null para os criados pelo tenant.</summary>
    public string? SeedKey { get; init; }

    /// <summary>Origem da importação (ex.: <c>dolibarr:llx_entrepot:3</c>), gravada só pela MIG-01c.</summary>
    public string? ImportSource { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
        IsDefault = false;
    }
}
