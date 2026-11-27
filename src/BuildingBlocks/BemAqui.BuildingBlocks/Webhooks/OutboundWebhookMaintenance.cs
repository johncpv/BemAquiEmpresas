using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.BuildingBlocks.Webhooks;

public sealed record OutboundWebhookSweepArgs;

/// <summary>
/// Varredor <c>outbound-webhooks-sweep</c> (CA 7): entrega <c>Pending</c> parada há mais de 5 min, ou <c>Retrying</c> com a hora da tentativa
/// passada há mais de 5 min (job perdido), volta à fila. O job é idempotente pela entrega.
/// </summary>
public sealed partial class OutboundWebhookSweepJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<OutboundWebhookSweepJob> logger)
    : TenantJob<OutboundWebhookSweepArgs>(directory, scopes, logger)
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);
    public const int BatchSize = 200;

    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, OutboundWebhookSweepArgs args, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<BemAquiDbContext>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var cutoff = now - StaleAfter;
        var stale = await db.OutboundDeliveries
            .Where(d => (d.Status == OutboundDeliveryStatus.Pending && d.LastQueuedAt < cutoff && d.NextAttemptAt < cutoff)
                || (d.Status == OutboundDeliveryStatus.Retrying && d.NextAttemptAt < cutoff))
            .OrderBy(d => d.CreatedAt).Take(BatchSize).ToListAsync(cancellationToken);
        if (stale.Count == 0)
        {
            return;
        }

        foreach (var delivery in stale)
        {
            delivery.LastQueuedAt = now;
            delivery.NextAttemptAt = now;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return; // enviada no meio da varredura: a próxima passada confere de novo
        }

        var scheduler = services.GetRequiredService<OutboundDeliveryScheduler>();
        foreach (var delivery in stale)
        {
            scheduler.Enqueue(tenant.Id, delivery.Id, null);
        }

        LogRequeued(logger, stale.Count, tenant.Key);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Avisos de saída: {Count} entregas paradas recolocadas na fila no tenant {Tenant}.")]
    private static partial void LogRequeued(ILogger logger, int count, string tenant);
}

/// <summary>Recorrente global a cada 5 minutos, um varredor por tenant ativo.</summary>
public sealed class OutboundWebhookSweepRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<OutboundWebhookSweepJob, OutboundWebhookSweepArgs>(directory, jobs)
{
    public const string Id = "outbound-webhooks-sweep";
    public const string Cron = "*/5 * * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override OutboundWebhookSweepArgs ArgsFor(TenantInfo tenant) => new();
}

public sealed record OutboundWebhookPurgeArgs;

/// <summary>
/// Expurgo diário (CA 14, ADR 0031 §5): aviso com mais de 90 dias e todas as entregas finais perde o <c>data</c>; a linha fica (tipo, ids, datas,
/// situação, código), como na entrada. Idempotente.
/// </summary>
public sealed partial class OutboundWebhookPurgeJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<OutboundWebhookPurgeJob> logger)
    : TenantJob<OutboundWebhookPurgeArgs>(directory, scopes, logger)
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, OutboundWebhookPurgeArgs args, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<BemAquiDbContext>();
        var cutoff = services.GetRequiredService<TimeProvider>().GetUtcNow() - Retention;
        var purged = await db.OutboundEvents
            .Where(e => e.Data != null && e.CreatedAt < cutoff
                && !db.OutboundDeliveries.Any(d => d.EventId == e.Id
                    && (d.Status == OutboundDeliveryStatus.Pending || d.Status == OutboundDeliveryStatus.Retrying || d.Status == OutboundDeliveryStatus.Held)))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Data, (string?)null), cancellationToken);
        LogPurged(logger, purged, tenant.Key);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Expurgo dos avisos de saída: conteúdo apagado de {Purged} avisos no tenant {Tenant}.")]
    private static partial void LogPurged(ILogger logger, int purged, string tenant);
}

/// <summary>Recorrente global diário (03:10 de São Paulo), um expurgo por tenant ativo.</summary>
public sealed class OutboundWebhookPurgeRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<OutboundWebhookPurgeJob, OutboundWebhookPurgeArgs>(directory, jobs)
{
    public const string Id = "outbound-webhooks-purge";
    public const string Cron = "10 3 * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override OutboundWebhookPurgeArgs ArgsFor(TenantInfo tenant) => new();
}
