using System.Threading.RateLimiting;
using BemAqui.Fiscal.Application;
using Microsoft.Extensions.Options;

namespace BemAqui.Fiscal.Infrastructure.Emissor;

/// <summary>
/// Vez de chamar o Emissor (ADR 0027, adendo de 27/10/2026): uma por processo, somando todos os tenants, a tela, o envio, o webhook e a
/// conferência de pendentes. O Emissor aceita 10 por segundo por IP (e o limite da chave); aqui sai no máximo
/// <see cref="FiscalIntegrationOptions.MaxRequestsPerSecond"/>, espaçadas (balde de uma ficha, sem rajada), e quem passa da vez espera na
/// fila. Depois de um 429, ninguém chama até passar o <c>Retry-After</c> (pausa geral, com teto).
/// </summary>
internal sealed class EmissorFiscalRateGate : IDisposable
{
    private readonly TokenBucketRateLimiter _limiter;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _maxPause;
    private long _pausedUntilTicks;

    public EmissorFiscalRateGate(IOptions<FiscalIntegrationOptions> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        var value = options.Value;
        var perSecond = Math.Clamp(value.MaxRequestsPerSecond, 1, 1000);
        _limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 1,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / perSecond),
            QueueLimit = Math.Max(0, value.MaxQueuedRequests),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });
        _clock = clock;
        _maxPause = value.MaxRateLimitPause;
    }

    /// <summary>Espera a vez (a pausa do 429 e depois a ficha); falso se a fila estiver cheia: a chamada desiste sem ir ao Emissor.</summary>
    public async Task<bool> WaitAsync(CancellationToken cancellationToken)
    {
        var pause = PausedFor();
        if (pause > TimeSpan.Zero)
        {
            await Task.Delay(pause, _clock, cancellationToken);
        }

        using var lease = await _limiter.AcquireAsync(1, cancellationToken);
        return lease.IsAcquired;
    }

    /// <summary>O Emissor respondeu 429: pausa geral pelo <c>Retry-After</c> (1 s sem ele), limitada ao teto.</summary>
    public void Pause(TimeSpan? retryAfter)
    {
        var wait = retryAfter is { } r && r > TimeSpan.Zero ? r : TimeSpan.FromSeconds(1);
        if (wait > _maxPause)
        {
            wait = _maxPause;
        }

        var until = _clock.GetUtcNow().Add(wait).UtcTicks;
        long current;
        do
        {
            current = Interlocked.Read(ref _pausedUntilTicks);
            if (current >= until)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _pausedUntilTicks, until, current) != current);
    }

    /// <summary>Quanto falta da pausa do último 429.</summary>
    public TimeSpan PausedFor()
    {
        var left = Interlocked.Read(ref _pausedUntilTicks) - _clock.GetUtcNow().UtcTicks;
        return left > 0 ? TimeSpan.FromTicks(left) : TimeSpan.Zero;
    }

    public void Dispose() => _limiter.Dispose();
}
