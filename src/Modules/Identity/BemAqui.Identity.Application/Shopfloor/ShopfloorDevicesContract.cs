using BemAqui.Identity.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application.Shopfloor;

/// <summary>Implementação do <see cref="IShopfloorDevices"/> (RH-01a): pelo gate do cookie e pelas revogações do chão de fábrica.</summary>
public sealed class ShopfloorDevicesContract(IIdentityDb db, ShopfloorDeviceGate gate, ShopfloorRevocations revocations) : IShopfloorDevices
{
    public async Task<ShopfloorDeviceRef?> ResolveAsync(string? deviceSecret, CancellationToken cancellationToken = default) =>
        await gate.ResolveAsync(deviceSecret, cancellationToken) is { } device ? new ShopfloorDeviceRef(device.Id, device.Name) : null;

    public async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> deviceIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deviceIds);
        if (deviceIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        var ids = deviceIds.Distinct().ToList();
        return await db.ShopfloorDevices.AsNoTracking().Where(d => ids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);
    }

    public Task<int> EndPinSessionAsync(Guid userId, Guid deviceId, string reason, CancellationToken cancellationToken = default) =>
        revocations.EndPinSessionAsync(userId, deviceId, reason, cancellationToken);
}
