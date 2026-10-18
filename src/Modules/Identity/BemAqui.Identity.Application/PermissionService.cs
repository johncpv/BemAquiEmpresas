using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BemAqui.Identity.Application;

/// <summary>
/// Permissão efetiva com cache curto por usuário (ADR 0008), invalidado na hora quando grupo, vínculo ou usuário
/// muda. O filtro de módulos liberados é aplicado a cada chamada, então bloquear um módulo vale no próximo request.
/// </summary>
public sealed class PermissionService(IIdentityDb db, TenantContextHolder tenant, IMemoryCache cache) : IPermissionService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private sealed record Grants(bool IsOwner, IReadOnlySet<string> Permissions);

    public async Task<EffectivePermissions> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var resolved = tenant.Resolved;
        if (resolved is null)
        {
            return EffectivePermissions.None;
        }

        var grants = await cache.GetOrCreateAsync(Key(resolved.Tenant.Id, userId), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            return LoadAsync(userId, cancellationToken);
        }) ?? new Grants(false, new HashSet<string>());

        var released = grants.IsOwner
            ? ModuleCatalog.All.Where(m => resolved.IsReleased(m.Code)).SelectMany(m => m.Actions.Select(a => m.Code + "." + a))
            : grants.Permissions.Where(p => resolved.IsReleased(p[..p.IndexOf('.', StringComparison.Ordinal)]));
        return new EffectivePermissions(released.ToHashSet(StringComparer.Ordinal), grants.IsOwner);
    }

    public void Invalidate(Guid userId)
    {
        if (tenant.Resolved is { } resolved)
        {
            cache.Remove(Key(resolved.Tenant.Id, userId));
        }
    }

    private async Task<Grants> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId && u.Status == UserStatus.Active, cancellationToken))
        {
            return new Grants(false, new HashSet<string>());
        }

        var groups = await db.UserGroups.Where(ug => ug.UserId == userId)
            .Join(db.Groups.Where(g => g.IsActive), ug => ug.GroupId, g => g.Id, (_, g) => new { g.Id, g.IsSystem, g.SeedKey })
            .ToListAsync(cancellationToken);
        if (groups.Any(g => g.IsSystem && g.SeedKey == Group.OwnerSeedKey))
        {
            return new Grants(true, new HashSet<string>());
        }

        var ids = groups.Select(g => g.Id).ToList();
        var enabled = (await db.GroupModules.Where(m => ids.Contains(m.GroupId) && m.Enabled).Select(m => new { m.GroupId, m.ModuleCode }).ToListAsync(cancellationToken))
            .Select(m => (m.GroupId, m.ModuleCode)).ToHashSet();
        var permissions = await db.GroupPermissions.Where(p => ids.Contains(p.GroupId)).Select(p => new { p.GroupId, p.ModuleCode, p.Action }).ToListAsync(cancellationToken);
        return new Grants(false, permissions
            .Where(p => enabled.Contains((p.GroupId, p.ModuleCode)) && ModuleCatalog.IsKnownPermission(p.ModuleCode + "." + p.Action))
            .Select(p => p.ModuleCode + "." + p.Action)
            .ToHashSet(StringComparer.Ordinal));
    }

    private static string Key(Guid tenantId, Guid userId) => "perm:" + tenantId + ":" + userId;
}
