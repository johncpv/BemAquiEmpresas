using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using BemAqui.BuildingBlocks.Observability;

namespace BemAqui.BuildingBlocks.Webhooks;

/// <summary>
/// Recusas de webhook (FND-16 CA 3): o contador <c>webhook_rejected{provider}</c> sobe a cada recusa e, a partir de
/// <see cref="Threshold"/> recusas em <see cref="Window"/> do mesmo provedor e tenant, sai o alerta <c>webhook_rejections</c> uma
/// vez por janela. Provedor ou tenant desconhecido contam como "-" (o código da URL não vira etiqueta: não enche a memória).
/// </summary>
public sealed class WebhookRejectionMonitor(IAlertSink alerts, TimeProvider clock) : IDisposable
{
    public const int Threshold = 20;
    public const int MaxKeys = 1000;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
    public const string MeterName = "BemAqui.Webhooks";

    private readonly Meter _meter = new(MeterName);
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);
    private Counter<long>? _counter;

    private sealed class Bucket
    {
        public DateTimeOffset Start;
        public int Count;
        public bool Alerted;
    }

    private Counter<long> Counter => _counter ??= _meter.CreateCounter<long>("webhook_rejected", description: "Webhooks recusados por provedor");

    /// <summary>Registra a recusa; devolve true quando esta recusa disparou o alerta da janela.</summary>
    public async Task<bool> RecordAsync(string? knownProvider, Guid? tenantId, string? tenantKey, CancellationToken cancellationToken = default)
    {
        var provider = knownProvider ?? "-";
        Counter.Add(1, new KeyValuePair<string, object?>("provider", provider));
        var key = provider + "|" + (tenantId?.ToString("N") ?? "-");
        if (!_buckets.ContainsKey(key) && _buckets.Count >= MaxKeys)
        {
            Prune();
            if (_buckets.Count >= MaxKeys)
            {
                key = provider + "|*";
            }
        }

        var now = clock.GetUtcNow();
        var bucket = _buckets.GetOrAdd(key, _ => new Bucket { Start = now });
        bool alert;
        lock (bucket)
        {
            if (now - bucket.Start >= Window)
            {
                bucket.Start = now;
                bucket.Count = 0;
                bucket.Alerted = false;
            }

            bucket.Count++;
            alert = bucket.Count >= Threshold && !bucket.Alerted;
            if (alert)
            {
                bucket.Alerted = true;
            }
        }

        if (alert)
        {
            await alerts.RaiseAsync(new Alert(WebhookAlerts.Rejections, tenantKey, $"{Threshold} avisos recusados em 5 minutos do provedor {provider}",
                "Pode ser segredo trocado no provedor ou tentativa de aviso falso."), cancellationToken);
        }

        return alert;
    }

    private void Prune()
    {
        var now = clock.GetUtcNow();
        foreach (var (key, bucket) in _buckets)
        {
            if (now - bucket.Start >= Window)
            {
                _buckets.TryRemove(key, out _);
            }
        }
    }

    public void Dispose() => _meter.Dispose();
}
