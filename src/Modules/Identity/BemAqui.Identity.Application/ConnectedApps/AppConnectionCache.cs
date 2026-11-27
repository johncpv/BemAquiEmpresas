using System.Collections.Concurrent;
using BemAqui.BuildingBlocks.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace BemAqui.Identity.Application.ConnectedApps;

/// <summary>
/// Cache de 30 s do token de acesso pelo hash (ADR 0053 §11), invalidado por conexão na revogação ou na suspensão. Com uma instância (hoje), a
/// invalidação é imediata; com várias, o atraso máximo é o tempo do cache.
/// </summary>
public sealed class AppConnectionCache(IMemoryCache cache)
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _byConnection = new();

    private static string Key(Guid tenantId, string tokenHash) => "apptok:" + tenantId + ":" + tokenHash;

    public bool TryGet(Guid tenantId, string tokenHash, out AppTokenResult? result) => cache.TryGetValue(Key(tenantId, tokenHash), out result);

    public void Set(Guid tenantId, string tokenHash, Guid connectionId, AppTokenResult result)
    {
        var source = _byConnection.GetOrAdd(connectionId, _ => new CancellationTokenSource());
        using var entry = cache.CreateEntry(Key(tenantId, tokenHash));
        entry.Value = result;
        entry.AbsoluteExpirationRelativeToNow = Ttl;
        entry.AddExpirationToken(new CancellationChangeToken(source.Token));
    }

    public void Invalidate(Guid connectionId)
    {
        if (_byConnection.TryRemove(connectionId, out var source))
        {
            source.Cancel();
            source.Dispose();
        }
    }
}
