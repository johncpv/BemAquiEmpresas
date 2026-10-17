using System.Collections.Concurrent;

namespace BemAqui.BuildingBlocks.Observability;

/// <summary>Seção <c>Alerts</c>.</summary>
public sealed class AlertOptions
{
    public const string Section = "Alerts";

    /// <summary>E-mails dos super admins (até a FND-13 trazer o cadastro deles).</summary>
    public string[] Recipients { get; set; } = [];

    public TimeSpan DedupWindow { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Falhas seguidas de uma integração externa até alertar (FND-12 CA 8).</summary>
    public int IntegrationFailureThreshold { get; set; } = 5;

    /// <summary>Base do link para o painel do Hangfire no host da plataforma.</summary>
    public string? PlatformBaseUrl { get; set; }
}

/// <summary>Conta falhas seguidas por integração (SMTP, Inter, EmissorFiscal, IA) e alerta ao atingir o limite.</summary>
public sealed class IntegrationHealthTracker(IAlertSink alerts, Microsoft.Extensions.Options.IOptions<AlertOptions> options)
{
    private readonly ConcurrentDictionary<string, int> _failures = new(StringComparer.Ordinal);

    public void RecordSuccess(string integration) => _failures.TryRemove(integration, out _);

    public async Task RecordFailureAsync(string integration, string? tenant, string error, CancellationToken cancellationToken = default)
    {
        var count = _failures.AddOrUpdate(integration, 1, (_, c) => c + 1);
        if (count >= options.Value.IntegrationFailureThreshold)
        {
            await alerts.RaiseAsync(new Alert(AlertKinds.IntegrationDown, tenant, $"Integração {integration} falhou {count} vezes seguidas", error), cancellationToken);
        }
    }
}
