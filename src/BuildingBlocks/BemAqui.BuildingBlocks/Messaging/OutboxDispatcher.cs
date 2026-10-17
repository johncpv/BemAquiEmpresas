using BemAqui.BuildingBlocks.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BemAqui.BuildingBlocks.Messaging;

/// <summary>
/// Dispatcher único. Percorre os tenants ativos e esvazia o outbox de cada um; tenant suspenso não roda
/// (FND-07 CA 16). Acorda pelo OutboxSignal ou pelo intervalo de polling.
/// </summary>
public sealed partial class OutboxDispatcher(
    ITenantDirectory directory,
    ITenantScopeFactory scopes,
    OutboxSignal signal,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await DispatchOnceAsync(stoppingToken);
            try
            {
                await signal.WaitAsync(options.Value.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Uma passada por todos os tenants ativos.</summary>
    public async Task DispatchOnceAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<TenantInfo> tenants;
        try
        {
            tenants = await directory.GetActiveAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDirectoryFailed(logger, ex);
            return;
        }

        foreach (var tenant in tenants)
        {
            if (tenant.Status != TenantStatus.Active)
            {
                LogTenantSkipped(logger, tenant.Key, tenant.Status);
                continue;
            }

            try
            {
                int processed;
                do
                {
                    await using var scope = await scopes.CreateScopeAsync(tenant, cancellationToken);
                    processed = await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(tenant, cancellationToken);
                }
                while (processed >= options.Value.BatchSize && !cancellationToken.IsCancellationRequested);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogTenantFailed(logger, ex, tenant.Key);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox: não foi possível listar os tenants ativos.")]
    private static partial void LogDirectoryFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox: tenant {Tenant} ignorado ({Status}).")]
    private static partial void LogTenantSkipped(ILogger logger, string tenant, TenantStatus status);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox do tenant {Tenant} falhou nesta passada; tenta de novo na próxima.")]
    private static partial void LogTenantFailed(ILogger logger, Exception exception, string tenant);
}
