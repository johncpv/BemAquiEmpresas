using System.Net;
using System.Text.Json;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.BuildingBlocks.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BemAqui.BuildingBlocks.Lookups;

/// <summary>
/// GET mínimo para APIs públicas (CAD-01b CA 13): só caminho relativo já validado pelo chamador, URL base fixa, sem
/// redirect, tempo limite, resposta até 64 KB tratada como JSON de texto, log do HttpClient desligado. Toda falha conta
/// para o alerta da integração (FND-12) e para o circuit breaker.
/// </summary>
public sealed class PublicDataHttp(IHttpClientFactory factory, LookupGuard guard, IOptions<LookupOptions> options, IServiceProvider services)
{
    public const string ClientName = "public-data";

    public async Task<(LookupStatus Status, JsonDocument? Json)> GetAsync(string relativePath, string integration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        if (relativePath.Contains("://", StringComparison.Ordinal) || relativePath.StartsWith('/'))
        {
            throw new ArgumentException("Só caminho relativo à URL base fixa.", nameof(relativePath));
        }

        if (!guard.TryEnter(integration))
        {
            await FailedAsync(integration, "circuito aberto ou teto global atingido", cancellationToken);
            return (LookupStatus.Unavailable, null);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        try
        {
            var http = factory.CreateClient(ClientName);
            using var response = await http.GetAsync(new Uri(options.Value.BrasilApiBaseUrl, relativePath), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                guard.Success(integration);
                services.GetService<IntegrationHealthTracker>()?.RecordSuccess(integration);
                return (LookupStatus.NotFound, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                await FailedAsync(integration, "HTTP " + (int)response.StatusCode, cancellationToken);
                return (LookupStatus.Unavailable, null);
            }

            var body = await ReadLimitedAsync(response, options.Value.MaxResponseBytes, timeout.Token);
            if (body is null)
            {
                await FailedAsync(integration, "resposta acima do limite", cancellationToken);
                return (LookupStatus.Unavailable, null);
            }

            var json = JsonDocument.Parse(body);
            guard.Success(integration);
            services.GetService<IntegrationHealthTracker>()?.RecordSuccess(integration);
            return (LookupStatus.Found, json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException && !cancellationToken.IsCancellationRequested)
        {
            await FailedAsync(integration, ex.GetType().Name, cancellationToken);
            return (LookupStatus.Unavailable, null);
        }
    }

    private static async Task<byte[]?> ReadLimitedAsync(HttpResponseMessage response, int max, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > max)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > max)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private async Task FailedAsync(string integration, string reason, CancellationToken ct)
    {
        guard.Failure(integration);
        if (services.GetService<IntegrationHealthTracker>() is { } health)
        {
            await health.RecordFailureAsync(integration, services.GetService<ITenantContext>()?.Tenant?.Key, reason, ct);
        }
    }
}
