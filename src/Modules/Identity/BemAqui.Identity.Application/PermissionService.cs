using System.Security.Cryptography;
using System.Text;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BemAqui.Identity.Application;

/// <summary>
/// Permissão efetiva com cache versionado (ADR 0008): a chave é <c>{tenant}:{user}:{perm_version}</c> e a versão é lida a
/// cada request, então revogar vale no próximo request mesmo com várias instâncias. O filtro de módulos liberados é
/// aplicado a cada chamada e também compõe a versão que vai para o front.
/// </summary>
public sealed class PermissionService(IIdentityDb db, TenantContextHolder tenant, IMemoryCache cache, IAfterCommit afterCommit, IRealtimeConnections connections) : IPermissionService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private sealed record Grants(bool IsOwner, IReadOnlySet<string> Permissions);

    public async Task<EffectivePermissions> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var resolved = tenant.Resolved;
        var user = resolved is null
            ? null
            : await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.Status, u.PermVersion }).FirstOrDefaultAsync(cancellationToken);
        if (resolved is null || user is null || user.Status != UserStatus.Active)
        {
            return EffectivePermissions.None;
        }

        var grants = await cache.GetOrCreateAsync($"perm:{resolved.Tenant.Id}:{userId}:{user.PermVersion}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            var loaded = await LoadAsync(userId, cancellationToken);
            await SnapshotAsync(userId, user.PermVersion, loaded, cancellationToken);
            return loaded;
        }) ?? new Grants(false, new HashSet<string>());

        var released = grants.IsOwner
            ? ModuleCatalog.All.Where(m => resolved.IsReleased(m.Code)).SelectMany(m => m.Actions.Select(a => m.Code + "." + a))
            : grants.Permissions.Where(p => resolved.IsReleased(p[..p.IndexOf('.', StringComparison.Ordinal)]));
        return new EffectivePermissions(released.ToHashSet(StringComparer.Ordinal), grants.IsOwner, user.PermVersion + "-" + ReleaseHash(resolved));
    }

    public async Task BumpAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count > 0)
        {
            var ids = userIds.ToList();
            await db.Users.Where(u => ids.Contains(u.Id)).ExecuteUpdateAsync(s => s.SetProperty(u => u.PermVersion, u => u.PermVersion + 1), cancellationToken);

            // PRD-02 CA 17: as conexões do hub caem depois do commit e a reconexão passa de novo pela autorização.
            if (tenant.Tenant?.Id is { } tenantId)
            {
                await afterCommit.RunAsync(_ =>
                {
                    connections.CloseFor(tenantId, ids);
                    return Task.CompletedTask;
                }, cancellationToken);
            }

            // OFF-02: o retrato da versão nova nasce logo depois do commit, para a conferência "tinha a permissão no instante do evento".
            await afterCommit.RunAsync(async ct =>
            {
                foreach (var id in ids)
                {
                    await GetAsync(id, ct);
                }
            }, cancellationToken);
        }
    }

    private async Task<Grants> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
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

    /// <summary>
    /// Retrato da versão (OFF-02, <see cref="PermissionSnapshot"/>): grava uma vez por usuário e versão (<c>ON CONFLICT DO NOTHING</c>, várias
    /// instâncias podem avaliar a mesma versão). Melhor esforço: falhar aqui não pode derrubar a autorização do request.
    /// </summary>
    private async Task SnapshotAsync(Guid userId, int version, Grants grants, CancellationToken cancellationToken)
    {
        try
        {
            var list = grants.Permissions.Order(StringComparer.Ordinal).ToArray();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO identity.permission_snapshots (user_id, perm_version, valid_from, is_owner, permissions) VALUES ({userId}, {version}, {DateTimeOffset.UtcNow}, {grants.IsOwner}, {list}) ON CONFLICT DO NOTHING",
                cancellationToken);
        }
        catch (System.Data.Common.DbException)
        {
            // Sem a tabela (banco ainda sem a migration) ou transação já abortada: a conferência usa o retrato seguinte.
        }
    }

    // Os ocultos (ADR 0046) entram no hash só quando o tenant tem perfil além da fábrica: trocar o perfil muda a versão e o front recarrega o
    // menu. Na fábrica sozinha (a Konebras), o oculto é só o módulo novo do PDV, que nunca aparece para ela: entra no catálogo de módulos sem
    // mudar a versão (CAT-01; regressão da PERF-01 CA 11). Liberar à mão um módulo oculto muda os liberados, e a versão muda por eles.
    // O vocabulário (PERF-03) entra só quando existe; sem termo, a versão fica idêntica à de antes dos perfis.
    private static string ReleaseHash(ResolvedTenant tenant)
    {
        var factoryOnly = tenant.Profiles.All(p => p == BusinessProfileCatalog.Factory);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(',', tenant.ReleasedModules.Order(StringComparer.Ordinal))
            + (factoryOnly || tenant.HiddenModules.Count == 0 ? string.Empty : "|" + string.Join(',', tenant.HiddenModules.Order(StringComparer.Ordinal)))
            + (tenant.Vocabulary.Count == 0 ? string.Empty : "|v:" + string.Join(',', tenant.Vocabulary.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => t.Key + "=" + t.Value)))
            // Adendo da PAG-01: perfil além da fábrica muda os recursos por perfil; a fábrica sozinha (a Konebras) mantém a versão de antes.
            + (factoryOnly ? string.Empty : "#" + string.Join(',', tenant.Profiles.Order(StringComparer.Ordinal))))))[..8];
    }
}
