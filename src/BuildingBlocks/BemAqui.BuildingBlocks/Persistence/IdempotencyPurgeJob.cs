using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Tenancy;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.BuildingBlocks.Persistence;

public sealed record IdempotencyPurgeArgs;

/// <summary>
/// Expurgo diário de <c>shared.idempotency_records</c> com mais de <see cref="Retention"/> (D19): a chave só protege
/// contra clique duplo e reenvio próximos; guardar a resposta para sempre só acumula dado.
/// </summary>
public sealed partial class IdempotencyPurgeJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<IdempotencyPurgeJob> logger)
    : TenantJob<IdempotencyPurgeArgs>(directory, scopes, logger)
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, IdempotencyPurgeArgs args, CancellationToken cancellationToken)
    {
        var cutoff = services.GetRequiredService<TimeProvider>().GetUtcNow() - Retention;
        var deleted = await services.GetRequiredService<BemAquiDbContext>().IdempotencyRecords
            .Where(r => r.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        LogPurged(logger, deleted, tenant.Key);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Expurgo de idempotência: {Deleted} registros apagados no tenant {Tenant}.")]
    private static partial void LogPurged(ILogger logger, int deleted, string tenant);
}

/// <summary>Recorrente global diário (03:15 de São Paulo) que enfileira o expurgo por tenant ativo.</summary>
public sealed class IdempotencyPurgeRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<IdempotencyPurgeJob, IdempotencyPurgeArgs>(directory, jobs)
{
    public const string Id = "idempotency-records-purge";
    public const string Cron = "15 3 * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override IdempotencyPurgeArgs ArgsFor(TenantInfo tenant) => new();
}
