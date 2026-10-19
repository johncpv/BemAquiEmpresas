using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>Implementação de <see cref="ILocations"/> (CA 18).</summary>
public sealed class LocationsContract(IInventoryDb db) : ILocations
{
    public async Task<IReadOnlyList<LocationOption>> ActiveAsync(CancellationToken cancellationToken = default) =>
        await db.Locations.AsNoTracking().Where(l => l.IsActive).OrderByDescending(l => l.IsDefault).ThenBy(l => l.Code)
            .Select(l => new LocationOption(l.Id, l.Code, l.Name, l.IsActive, l.IsDefault)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<LocationOption>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return await db.Locations.AsNoTracking().Where(l => ids.Contains(l.Id))
            .Select(l => new LocationOption(l.Id, l.Code, l.Name, l.IsActive, l.IsDefault)).ToListAsync(cancellationToken);
    }

    public async Task<LocationOption?> DefaultAsync(CancellationToken cancellationToken = default) =>
        await db.Locations.AsNoTracking().Where(l => l.IsDefault && l.IsActive)
            .Select(l => new LocationOption(l.Id, l.Code, l.Name, l.IsActive, l.IsDefault)).FirstOrDefaultAsync(cancellationToken);

    internal static LocationDetail ToDetail(Location l) => new(l.Id, l.Code, l.Name, l.IsActive, l.IsDefault, l.SeedKey, l.CreatedAt);
}
