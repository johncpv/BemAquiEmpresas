using BemAqui.BuildingBlocks.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BemAqui.Platform.Infrastructure.Catalog;

/// <summary>LISTEN no canal <c>tenant_changed</c> do catálogo: qualquer mudança em tenants ou domínios limpa o cache.</summary>
public sealed partial class CatalogChangeListener(IConfiguration configuration, ITenantResolver resolver, ILogger<CatalogChangeListener> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = configuration.GetConnectionString("Catalog");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(stoppingToken);
                connection.Notification += (_, _) => resolver.Invalidate();
                await using (var listen = new NpgsqlCommand("LISTEN " + CatalogDbContext.ChangeChannel, connection))
                {
                    await listen.ExecuteNonQueryAsync(stoppingToken);
                }

                resolver.Invalidate(); // pode ter perdido avisos enquanto estava desconectado
                while (!stoppingToken.IsCancellationRequested)
                {
                    await connection.WaitAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
            {
                LogListenFailed(logger, ex);
                resolver.Invalidate();
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Catálogo: LISTEN tenant_changed caiu; reconectando em 5 s (cache invalidado).")]
    private static partial void LogListenFailed(ILogger logger, Exception exception);
}
