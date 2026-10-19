using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace BemAqui.BuildingBlocks.Lookups;

/// <summary>
/// Proteção das consultas externas (CAD-01b CA 5 e 12): teto global por instância e circuit breaker por integração. Com o
/// circuito aberto, a consulta nem sai: responde indisponível na hora, e o cadastro segue manual.
/// </summary>
public sealed class LookupGuard : IDisposable
{
    private readonly LookupOptions _options;
    private readonly TimeProvider _clock;
    private readonly FixedWindowRateLimiter _global;
    private readonly ConcurrentDictionary<string, (int Failures, DateTimeOffset? OpenUntil)> _circuits = new(StringComparer.Ordinal);

    public LookupGuard(IOptions<LookupOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _clock = clock;
        _global = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, _options.GlobalPerMinute), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
    }

    /// <summary>Pode sair? Falso com o circuito aberto ou o teto global atingido.</summary>
    public bool TryEnter(string integration)
    {
        if (_circuits.TryGetValue(integration, out var circuit) && circuit.OpenUntil > _clock.GetUtcNow())
        {
            return false;
        }

        using var lease = _global.AttemptAcquire();
        return lease.IsAcquired;
    }

    public void Success(string integration) => _circuits.TryRemove(integration, out _);

    public void Failure(string integration) => _circuits.AddOrUpdate(integration, _ => Next(0),
        (_, current) => Next(current.Failures));

    private (int, DateTimeOffset?) Next(int failures)
    {
        var count = failures + 1;
        return (count, count >= _options.CircuitFailures ? _clock.GetUtcNow().AddSeconds(_options.CircuitOpenSeconds) : null);
    }

    public void Dispose() => _global.Dispose();
}
