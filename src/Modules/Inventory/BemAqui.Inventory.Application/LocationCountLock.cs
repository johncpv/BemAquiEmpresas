using BemAqui.SharedKernel;

namespace BemAqui.Inventory.Application;

/// <summary>Inventário em contagem num local (EST-05): o local, o inventário e o número dele, para a mensagem e o link.</summary>
public sealed record LocationCountBlock(Guid LocationId, string LocationName, Guid CountId, string CountNumber);

/// <summary>
/// Ponto de bloqueio do local por inventário (EST-01b CA 18, ponto 91), interno do Inventory: o ajuste manual (lançar, aprovar e
/// estornar) e o estorno de movimento manual (EST-01a CA 20) consultam antes de gravar. Nesta story a implementação nunca bloqueia
/// (<see cref="NoLocationCountLock"/>); a EST-05 registra a real (EST-05 CA 6). O ajuste de origem <c>InventoryCount</c> não passa por aqui
/// ao nascer.
/// </summary>
public interface ILocationCountLock
{
    /// <summary>O primeiro inventário em contagem num dos locais, ou null quando nenhum está bloqueado.</summary>
    Task<LocationCountBlock?> FindAsync(IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken = default);
}

/// <summary>Até a EST-05: nenhum local fica bloqueado.</summary>
public sealed class NoLocationCountLock : ILocationCountLock
{
    public Task<LocationCountBlock?> FindAsync(IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken = default) => Task.FromResult<LocationCountBlock?>(null);
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
