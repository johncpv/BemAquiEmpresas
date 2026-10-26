using System.Collections.Concurrent;
using System.Security.Cryptography;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Webhooks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Platform.Infrastructure.Catalog;

/// <summary>
/// <c>tenantKey</c> dos webhooks no catálogo (FND-16 CA 2): só a rota <c>/webhooks/*</c> usa. A chave vira o id do tenant (cache de
/// 5 min, negativo de 30 s e limitado), e o tenant em si vem do <see cref="ITenantResolver"/>, cuja suspensão vale em segundos
/// (NOTIFY do catálogo). Chave fora do formato (22 caracteres base64url) não chega ao banco.
/// </summary>
public sealed class CatalogWebhookTenantKeys(IServiceScopeFactory scopes, ITenantResolver resolver, TimeProvider clock) : IWebhookTenantKeys
{
    public const int KeyBytes = 16;
    public const int KeyLength = 22;
    public static readonly TimeSpan PositiveTtl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan NegativeTtl = TimeSpan.FromSeconds(30);
    public const int MaxEntries = 1000;

    private readonly ConcurrentDictionary<string, (Guid? TenantId, DateTimeOffset Expires)> _cache = new(StringComparer.Ordinal);

    public static string NewKey() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(KeyBytes));

    public static bool IsWellFormed(string? key) =>
        key is { Length: KeyLength } && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public async Task<ResolvedTenant?> ResolveAsync(string tenantKey, CancellationToken cancellationToken = default)
    {
        if (!IsWellFormed(tenantKey))
        {
            return null;
        }

        var now = clock.GetUtcNow();
        Guid? tenantId;
        if (_cache.TryGetValue(tenantKey, out var cached) && cached.Expires > now)
        {
            tenantId = cached.TenantId;
        }
        else
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            tenantId = await db.Tenants.AsNoTracking().Where(t => t.WebhookKey == tenantKey).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(cancellationToken);
            if (_cache.Count >= MaxEntries)
            {
                _cache.Clear(); // chaves aleatórias não enchem a memória
            }

            _cache[tenantKey] = (tenantId, now + (tenantId is null ? NegativeTtl : PositiveTtl));
        }

        return tenantId is { } id ? await resolver.FindByIdAsync(id, cancellationToken) : null;
    }

    public async Task<string> EnsureAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant.WebhookKey is { } existing)
        {
            return existing;
        }

        tenant.AssignWebhookKey(NewKey());
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Duas rotações ao mesmo tempo: vale a chave de quem gravou primeiro.
            db.ChangeTracker.Clear();
            return (await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.WebhookKey).SingleAsync(cancellationToken))
                ?? throw new InvalidOperationException("Chave do webhook não gravada.");
        }

        return tenant.WebhookKey!;
    }
}
