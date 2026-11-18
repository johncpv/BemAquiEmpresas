using BemAqui.Pos.Contracts;
using BemAqui.Pos.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Pos.Application;

/// <summary>
/// <see cref="IPosRegisters"/> (PDV-01 CA 6): o Identity troca o aparelho do caixa por aqui, na transação de quem chama (só rastreia; quem chama
/// grava). A época do caixa sobe a cada aparelho novo.
/// </summary>
public sealed class PosRegistersContract(IPosDb db) : IPosRegisters
{
    public async Task<PosRegisterRef?> FindAsync(Guid registerId, CancellationToken cancellationToken = default) =>
        (await FindManyAsync([registerId], cancellationToken)).GetValueOrDefault(registerId);

    public async Task<IReadOnlyDictionary<Guid, PosRegisterRef>> FindManyAsync(IReadOnlyCollection<Guid> registerIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registerIds);
        if (registerIds.Count == 0)
        {
            return new Dictionary<Guid, PosRegisterRef>();
        }

        var rows = await (from r in db.Registers.AsNoTracking()
                          join s in db.Stores.AsNoTracking() on r.StoreId equals s.Id
                          where registerIds.Contains(r.Id)
                          select new { Register = r, Store = s }).ToListAsync(cancellationToken);
        return rows.ToDictionary(x => x.Register.Id, x => ToRef(x.Store, x.Register));
    }

    public async Task<PosRegisterAssignment?> AssignDeviceAsync(Guid registerId, Guid deviceId, CancellationToken cancellationToken = default)
    {
        var register = await db.Registers.FirstOrDefaultAsync(r => r.Id == registerId, cancellationToken);
        var store = register is null ? null : await db.Stores.AsNoTracking().FirstOrDefaultAsync(s => s.Id == register.StoreId, cancellationToken);
        if (register is null || store is null || !register.IsActive || !store.IsActive)
        {
            return null;
        }

        var previous = register.AssignDevice(deviceId);
        return new PosRegisterAssignment(ToRef(store, register), register.DeviceEpoch, previous);
    }

    public async Task ReleaseDeviceAsync(Guid registerId, Guid deviceId, CancellationToken cancellationToken = default)
    {
        var register = await db.Registers.FirstOrDefaultAsync(r => r.Id == registerId, cancellationToken);
        register?.ReleaseDevice(deviceId);
    }

    public async Task<IReadOnlyList<PosStoreRef>> ListStoresAsync(CancellationToken cancellationToken = default) =>
        await db.Stores.AsNoTracking().OrderBy(s => s.Name).Select(s => new PosStoreRef(s.Id, s.Code, s.Name, s.IsActive)).ToListAsync(cancellationToken);

    private static PosRegisterRef ToRef(Store store, Register register) =>
        new(store.Id, store.Code, store.Name, register.Id, register.Number, register.ActiveDeviceId, register.DeviceEpoch, register.IsActive && store.IsActive);
}
