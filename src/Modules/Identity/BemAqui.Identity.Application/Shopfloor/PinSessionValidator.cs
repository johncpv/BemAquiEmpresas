using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BemAqui.Identity.Application.Shopfloor;

/// <summary>
/// Sessão por PIN conferida a cada request (ADR 0026 §1 e §3, PRD-03b CA 10, 15, 17 e 20): o aparelho do cookie continua válido (não
/// revogado, não vencido, segredo atual), existe sessão por PIN viva do usuário nele dentro das 12 h, e o perfil ainda pode usar PIN
/// (<c>shopfloor.create</c>, sem ação reservada, funcionário ativo). A situação do funcionário fica 30 s em cache.
/// </summary>
public sealed class PinSessionValidator(
    IIdentityDb db,
    ShopfloorDeviceGate gate,
    ShopfloorDeviceService devices,
    IPermissionService permissions,
    ITenantContext tenant,
    IMemoryCache cache,
    TimeProvider clock) : IPinSessionValidator
{
    public static readonly TimeSpan EmployeeCacheTtl = TimeSpan.FromSeconds(30);

    public async Task<PinSessionCheck> CheckAsync(Guid userId, string? deviceSecret, CancellationToken cancellationToken = default)
    {
        var device = await gate.ResolveAsync(deviceSecret, cancellationToken);
        if (device is null)
        {
            return PinSessionCheck.DeviceInvalid;
        }

        var now = clock.GetUtcNow();
        var alive = await db.RefreshTokens.AsNoTracking().AnyAsync(
            t => t.UserId == userId && t.DeviceId == device.Id && t.SessionKind == SessionKind.Pin && t.RevokedAt == null && t.AbsoluteExpiresAt > now,
            cancellationToken);
        if (!alive)
        {
            return PinSessionCheck.SessionEnded;
        }

        if (!PinProfile.CanUsePin(await permissions.GetAsync(userId, cancellationToken)))
        {
            return PinSessionCheck.SessionEnded;
        }

        var key = $"pin-employee:{tenant.Tenant?.Id:N}:{userId:N}";
        if (!cache.TryGetValue(key, out bool active))
        {
            active = await devices.EmployeeActiveAsync(userId, cancellationToken);
            cache.Set(key, active, EmployeeCacheTtl);
        }

        return active ? PinSessionCheck.Valid : PinSessionCheck.SessionEnded;
    }
}