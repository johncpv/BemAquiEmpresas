using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BemAqui.Identity.Infrastructure;

/// <summary>
/// Confere o sstamp do token contra o usuário, com cache curto por tenant e usuário (ADR 0008). Desativar,
/// redefinir senha e mexer no 2FA chamam <see cref="Invalidate"/>, então o efeito é no próximo request.
/// </summary>
public sealed class CachedSecurityStampValidator(IIdentityDb db, ITenantContext tenant, IMemoryCache cache, IAfterCommit afterCommit, IRealtimeConnections connections)
    : ISecurityStampValidator, ISecurityStampCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    public async Task<bool> IsCurrentAsync(Guid userId, string stamp, CancellationToken cancellationToken = default)
    {
        var key = Key(tenant.Tenant?.Id ?? Guid.Empty, userId);
        if (!cache.TryGetValue(key, out string? current))
        {
            current = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId && u.Status == UserStatus.Active)
                .Select(u => u.SecurityStamp)
                .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
            cache.Set(key, current, Ttl);
        }

        return !string.IsNullOrEmpty(current) && string.Equals(current, stamp, StringComparison.Ordinal);
    }

    /// <summary>O stamp mudou: limpa o cache e fecha as conexões do hub do usuário depois do commit (PRD-02 CA 17).</summary>
    public void Invalidate(Guid tenantId, Guid userId)
    {
        cache.Remove(Key(tenantId, userId));
        _ = afterCommit.RunAsync(_ =>
        {
            cache.Remove(Key(tenantId, userId));
            connections.CloseFor(tenantId, [userId]);
            return Task.CompletedTask;
        });
    }

    private static string Key(Guid tenantId, Guid userId) => $"sstamp:{tenantId:N}:{userId:N}";
}
