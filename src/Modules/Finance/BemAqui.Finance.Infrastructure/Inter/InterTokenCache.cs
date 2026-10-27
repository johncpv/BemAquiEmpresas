using System.Collections.Concurrent;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Infrastructure.Inter;

/// <summary>Token OAuth do Inter: o valor fica num <see cref="Secret"/> (nunca aparece em log ou <c>ToString</c>).</summary>
public sealed record InterAccessToken(Secret Value, DateTimeOffset ExpiresAt, IReadOnlyList<string> Scopes);

/// <summary>
/// Token em cache por tenant, conta e certificado (BNK-01a CA 10, ADR 0032 §2): vale até 60 s antes de vencer, só na memória do
/// processo (nunca no banco nem no log); a renovação é uma por vez por chave (lock), e quem esperava usa o token novo.
/// </summary>
public sealed class InterTokenCache(TimeProvider clock)
{
    public static readonly TimeSpan Margin = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, InterAccessToken> _tokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public bool TryGet(string key, out InterAccessToken token)
    {
        if (_tokens.TryGetValue(key, out var cached) && cached.ExpiresAt - Margin > clock.GetUtcNow())
        {
            token = cached;
            return true;
        }

        token = null!;
        return false;
    }

    /// <summary>
    /// Token válido da chave ou um novo pedido por <paramref name="fetch"/>, sob lock. <paramref name="stale"/> é o token recusado
    /// (401) que deve ser trocado: se outro já renovou, usa o novo sem pedir de novo.
    /// </summary>
    public async Task<(InterAccessToken? Token, TFailure? Failure)> GetOrRenewAsync<TFailure>(
        string key, Func<CancellationToken, Task<(InterAccessToken? Token, TFailure? Failure)>> fetch, InterAccessToken? stale, CancellationToken cancellationToken)
        where TFailure : class
    {
        ArgumentNullException.ThrowIfNull(fetch);
        if (stale is null && TryGet(key, out var fresh))
        {
            return (fresh, null);
        }

        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (TryGet(key, out var current) && !ReferenceEquals(current, stale))
            {
                return (current, null);
            }

            var (token, failure) = await fetch(cancellationToken);
            if (token is not null)
            {
                _tokens[key] = token;
            }
            else
            {
                _tokens.TryRemove(key, out _);
            }

            return (token, failure);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Esquece os tokens da integração (qualquer certificado): troca ou desligamento (CA 14 e 16).</summary>
    public void Forget(string prefix)
    {
        foreach (var key in _tokens.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            _tokens.TryRemove(key, out _);
        }
    }

    public int Count => _tokens.Count;
}
