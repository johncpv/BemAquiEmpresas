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
    PosDeviceCookieGate registers,
    BemAqui.HR.Contracts.IEmployees employees,
    TimeProvider clock) : IPinSessionValidator
{
    public static readonly TimeSpan EmployeeCacheTtl = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Sessão por PIN do caixa do PDV (PDV-01 CA 11): aparelho de caixa ativo pelo cookie <c>__Host-bq_pos</c>, sessão <c>PosPin</c> viva dele dentro das
    /// 12 h, usuário ativo, com <c>pos.create</c>, sem ação reservada, da loja do aparelho (usuário de operação) e com o funcionário ligado ativo.
    /// </summary>
    public async Task<PinSessionCheck> CheckRegisterAsync(Guid userId, string? posDeviceSecret, CancellationToken cancellationToken = default)
    {
        var device = await registers.ResolveAsync(posDeviceSecret, cancellationToken);
        if (device is null)
        {
            return PinSessionCheck.DeviceInvalid;
        }

        var now = clock.GetUtcNow();
        var alive = await db.RefreshTokens.AsNoTracking().AnyAsync(
            t => t.UserId == userId && t.DeviceId == device.Id && t.SessionKind == SessionKind.PosPin && t.RevokedAt == null && t.AbsoluteExpiresAt > now,
            cancellationToken);
        var user = alive ? await db.Users.AsNoTracking().Where(u => u.Id == userId && u.Status == UserStatus.Active)
            .Select(u => new { u.IsOperationUser, u.OperationStoreIds, u.EmployeeId }).FirstOrDefaultAsync(cancellationToken) : null;
        if (user is null || (user.IsOperationUser && !user.OperationStoreIds.Contains(device.StoreId!.Value))
            || !PinProfile.CanUsePosPin(await permissions.GetAsync(userId, cancellationToken)))
        {
            return PinSessionCheck.SessionEnded;
        }

        if (user.EmployeeId is not { } employeeId)
        {
            return PinSessionCheck.Valid;
        }

        var key = $"pos-pin-employee:{tenant.Tenant?.Id:N}:{employeeId:N}";
        if (!cache.TryGetValue(key, out bool active))
        {
            var found = await employees.FindAsync([employeeId], cancellationToken);
            active = found.Count == 1 && found[0].IsActive && found[0].Status == BemAqui.HR.Contracts.EmployeeSituation.Active;
            cache.Set(key, active, EmployeeCacheTtl);
        }

        return active ? PinSessionCheck.Valid : PinSessionCheck.SessionEnded;
    }

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