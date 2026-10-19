using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>
/// <see cref="ILocationImport"/> (MIG-01c CA 11 e 12): cria e atualiza locais com as regras da CAD-03, sem nunca trocar o local
/// padrão (o relatório sugere, o almoxarife decide).
/// </summary>
public sealed class LocationImport(IInventoryDb db, LocationService locations) : ILocationImport
{
    public async Task<Guid?> FindAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        return await db.Locations.Where(l => l.Code == normalized).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Guid?> DefaultAsync(CancellationToken cancellationToken = default) =>
        await db.Locations.Where(l => l.IsDefault).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(cancellationToken);

    public async Task<Result<InventoryImportWrite>> CreateAsync(LocationImportData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var created = await locations.CreateAsync(new LocationInput(data.Code, data.Name), cancellationToken);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        var location = await db.Locations.FirstAsync(l => l.Id == created.Value.Id, cancellationToken);
        location.ImportSource = data.ImportSource;
        // Inativo no Dolibarr entra inativo, a não ser que tenha virado o padrão (primeiro local do tenant).
        location.IsActive = data.IsActive || location.IsDefault;
        await db.SaveChangesAsync(cancellationToken);
        return new InventoryImportWrite(location.Id, await VersionAsync(location.Id, cancellationToken) ?? 0);
    }

    public async Task<Result<InventoryImportWrite>> UpdateAsync(Guid locationId, LocationImportData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var location = await db.Locations.FirstOrDefaultAsync(l => l.Id == locationId, cancellationToken);
        if (location is null)
        {
            return Error.NotFound("location_not_found", "Local não encontrado.");
        }

        var updated = await locations.UpdateAsync(locationId, new LocationInput(data.Code, data.Name), cancellationToken);
        if (updated.IsFailure)
        {
            return updated.Error!;
        }

        location = await db.Locations.FirstAsync(l => l.Id == locationId, cancellationToken);
        location.IsActive = data.IsActive || location.IsDefault;
        location.ImportSource ??= data.ImportSource;
        await db.SaveChangesAsync(cancellationToken);
        return new InventoryImportWrite(location.Id, await VersionAsync(location.Id, cancellationToken) ?? 0);
    }

    public async Task<Result<InventoryImportWrite>> LinkAsync(Guid locationId, string importSource, CancellationToken cancellationToken = default)
    {
        var location = await db.Locations.FirstOrDefaultAsync(l => l.Id == locationId, cancellationToken);
        if (location is null)
        {
            return Error.NotFound("location_not_found", "Local não encontrado.");
        }

        location.ImportSource ??= importSource;
        await db.SaveChangesAsync(cancellationToken);
        return new InventoryImportWrite(location.Id, await VersionAsync(location.Id, cancellationToken) ?? 0);
    }

    public async Task<LocationImportData?> CurrentAsync(Guid locationId, CancellationToken cancellationToken = default)
    {
        var l = await db.Locations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == locationId, cancellationToken);
        return l is null ? null : new LocationImportData(l.Code, l.Name, l.IsActive, l.ImportSource ?? string.Empty);
    }

    public Task<long?> VersionAsync(Guid locationId, CancellationToken cancellationToken = default) =>
        db.Database.SqlQuery<long?>($"SELECT xmin::text::bigint AS \"Value\" FROM inventory.locations WHERE id = {locationId} AND NOT is_deleted").SingleOrDefaultAsync(cancellationToken);
}
