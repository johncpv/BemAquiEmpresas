using BemAqui.BuildingBlocks.Identity;
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
public sealed class CachedSecurityStampValidator(IIdentityDb db, ITenantContext tenant, IMemoryCache cache) : ISecurityStampValidator, ISecurityStampCache
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

    public void Invalidate(Guid tenantId, Guid userId) => cache.Remove(Key(tenantId, userId));

    private static string Key(Guid tenantId, Guid userId) => $"sstamp:{tenantId:N}:{userId:N}";
}
