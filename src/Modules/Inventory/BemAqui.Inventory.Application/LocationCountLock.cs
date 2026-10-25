using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>Inventário em contagem num local (EST-05): o local, o inventário e o número dele, para a mensagem e o link.</summary>
public sealed record LocationCountBlock(Guid LocationId, string LocationName, Guid CountId, string CountNumber);

/// <summary>
/// Ponto de bloqueio do local por inventário (EST-01b CA 18, EST-05 CA 6, ponto 91), interno do Inventory: o ajuste manual (lançar,
/// aprovar e estornar) e o estorno de movimento manual (EST-01a CA 20) consultam antes de gravar. A implementação é o
/// <see cref="InventoryCountLock"/>; o <see cref="NoLocationCountLock"/> fica para os testes que não têm inventário. O ajuste de
/// origem <c>InventoryCount</c> não passa por aqui ao nascer.
/// </summary>
public interface ILocationCountLock
{
    /// <summary>O primeiro inventário em contagem num dos locais, ou null quando nenhum está bloqueado.</summary>
    Task<LocationCountBlock?> FindAsync(IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken = default);
}

/// <summary>Nenhum local bloqueado (dublê dos testes; até a EST-05 era o registrado).</summary>
public sealed class NoLocationCountLock : ILocationCountLock
{
    public Task<LocationCountBlock?> FindAsync(IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken = default) => Task.FromResult<LocationCountBlock?>(null);
}

/// <summary>
/// Bloqueio real (EST-05 CA 6): o local tem um inventário <c>Counting</c> (no máximo um, pelo índice único parcial
/// <c>ux_inventory_counts_location_counting</c>). Entradas, saídas, requisições, recebimentos e romaneios não consultam (ponto 91).
/// </summary>
public sealed class InventoryCountLock(IInventoryDb db) : ILocationCountLock
{
    public async Task<LocationCountBlock?> FindAsync(IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locationIds);
        if (locationIds.Count == 0)
        {
            return null;
        }

        var ids = locationIds.Distinct().ToList();
        var found = await (from c in db.InventoryCounts.AsNoTracking()
                           join l in db.Locations.IgnoreQueryFilters() on c.LocationId equals l.Id
                           where c.Status == InventoryCountStatus.Counting && ids.Contains(c.LocationId)
                           orderby c.Number
                           select new { c.LocationId, l.Name, c.Id, c.Number }).FirstOrDefaultAsync(cancellationToken);
        return found is null ? null : new LocationCountBlock(found.LocationId, found.Name, found.Id, found.Number);
    }
}

public static class LocationCountLocks
{
    public const string Code = "location_counting";
    public const string Link = "/app/estoque/inventarios/";

    /// <summary>409 <c>location_counting</c> com o link para o inventário (CA 18).</summary>
    public static Error Blocked(LocationCountBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return Error.Conflict(Code, $"Há um inventário em contagem em {block.LocationName}: {block.CountNumber}. Ajustes e estornos manuais ficam bloqueados até fechar ou cancelar.") with
        {
            Data = new Dictionary<string, object?> { ["countId"] = block.CountId, ["countNumber"] = block.CountNumber, ["link"] = Link + block.CountId },
        };
    }

    /// <summary>Consulta e, se houver contagem aberta, devolve o 409; null quando pode seguir.</summary>
    public static async Task<Error?> CheckAsync(this ILocationCountLock locks, IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locks);
        return await locks.FindAsync(locationIds, cancellationToken) is { } block ? Blocked(block) : null;
    }
}
