using System.Net.Sockets;
using BemAqui.BuildingBlocks.Attachments;
using Hangfire;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace BemAqui.Host.Health;

/// <summary>Seção <c>Health</c>: quais dependências entram no /health/ready.</summary>
public sealed class HealthOptions
{
    public const string Section = "Health";

    public bool ClamAv { get; set; } = true;

    public bool Storage { get; set; } = true;

    public bool Hangfire { get; set; } = true;
}

/// <summary>clamd responde ao PING (o upload depende dele: falha fechada).</summary>
public sealed class ClamAvHealthCheck(IOptions<ClamAvOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var client = new TcpClient();
            await client.ConnectAsync(options.Value.Host, options.Value.Port, timeout.Token);
            await using var stream = client.GetStream();
            await stream.WriteAsync("zPING\0"u8.ToArray(), timeout.Token);
            var buffer = new byte[16];
            var read = await stream.ReadAsync(buffer, timeout.Token);
            return System.Text.Encoding.ASCII.GetString(buffer, 0, read).StartsWith("PONG", StringComparison.Ordinal)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("ClamAV respondeu de forma inesperada.");
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("ClamAV indisponível.", ex);
        }
    }
}

/// <summary>A raiz do armazenamento de arquivos aceita escrita.</summary>
public sealed class StorageHealthCheck(IOptions<FileStorageOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var root = Path.GetFullPath(options.Value.Root);
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, ".health-" + Guid.CreateVersion7().ToString("N"));
            await File.WriteAllTextAsync(probe, "ok", cancellationToken);
            File.Delete(probe);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return HealthCheckResult.Unhealthy("Armazenamento de arquivos sem escrita.", ex);
        }
    }
}

/// <summary>Há ao menos um servidor do Hangfire ativo (jobs e e-mails dependem dele).</summary>
public sealed class HangfireHealthCheck(JobStorage storage) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(storage.GetMonitoringApi().Servers().Count > 0
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Nenhum servidor de jobs ativo."));
}
