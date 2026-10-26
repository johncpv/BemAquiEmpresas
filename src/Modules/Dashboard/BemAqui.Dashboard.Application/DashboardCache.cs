using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace BemAqui.Dashboard.Application;

/// <summary>Quem pede o card: tenant, usuário e a versão da permissão efetiva dele (ADR 0008).</summary>
public sealed record CardScope(Guid TenantId, Guid UserId, string PermissionVersion);

/// <summary>Resultado guardado e quando foi calculado (o "Atualizado às" do card).</summary>
public sealed record CachedCard(object Value, DateTimeOffset ComputedAt);

/// <summary>
/// Cache do painel (DSH-01a CA 12 e 13, ADR 0028 §3), em memória do processo (uma réplica hoje): cada card por 60 s com a
/// chave <c>{tenant}:{usuário}:{versão de permissão}:{geração}:{card}:{parâmetros}</c>. O contador de geração do tenant
/// (<c>dashboard:{tenant}:gen</c>) sobe com os eventos que mudam os números e invalida o painel do tenant sem varrer o cache; a
/// versão de permissão faz a perda de acesso valer na hora; "Atualizar" marca o usuário e o próximo pedido recalcula.
/// </summary>
public sealed class DashboardCache(TimeProvider clock) : IDisposable
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    /// <summary>Teto de entradas (cada card conta 1): protege a memória com muitos usuários.</summary>
    public const int MaxEntries = 20_000;

    private readonly MemoryCache _memory = new(new MemoryCacheOptions { SizeLimit = MaxEntries });
    private readonly ConcurrentDictionary<Guid, long> _generations = new();
    private readonly ConcurrentDictionary<(Guid Tenant, Guid User), DateTimeOffset> _refreshes = new();

    public static string GenerationKey(Guid tenantId) => $"dashboard:{tenantId}:gen";

    /// <summary>Chave do card (CA 12); o prefixo separa o painel de outros usos do cache.</summary>
    public static string Key(Guid tenantId, Guid userId, string permissionVersion, long generation, string card, string parameters) =>
        $"dashboard:{tenantId}:{userId}:{permissionVersion}:{generation}:{card}:{parameters}";

    public long Generation(Guid tenantId) => _generations.TryGetValue(tenantId, out var generation) ? generation : 0;

    /// <summary>Um evento mudou os números do tenant: o painel inteiro dele passa a ser recalculado.</summary>
    public long Bump(Guid tenantId) => _generations.AddOrUpdate(tenantId, 1, (_, current) => current + 1);

    /// <summary>"Atualizar" (CA 13): o que foi calculado antes de agora é ignorado para este usuário.</summary>
    public void MarkRefresh(Guid tenantId, Guid userId) => _refreshes[(tenantId, userId)] = clock.GetUtcNow();

    public async Task<CachedCard> GetOrComputeAsync(CardScope scope, string card, string parameters, Func<CancellationToken, Task<object>> compute, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(compute);
        var key = Key(scope.TenantId, scope.UserId, scope.PermissionVersion, Generation(scope.TenantId), card, parameters);
        var refreshedAt = _refreshes.TryGetValue((scope.TenantId, scope.UserId), out var at) ? at : DateTimeOffset.MinValue;
        var now = clock.GetUtcNow();
        if (_memory.TryGetValue(key, out CachedCard? cached) && cached is not null && cached.ComputedAt > refreshedAt && now - cached.ComputedAt < Ttl)
        {
            return cached;
        }

        var entry = new CachedCard(await compute(cancellationToken), now);
        _memory.Set(key, entry, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl, Size = 1 });
        return entry;
    }

    public void Dispose() => _memory.Dispose();
}
