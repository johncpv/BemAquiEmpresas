using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.BuildingBlocks.Webhooks;

public sealed record WebhookEventsPurgeArgs;

/// <summary>
/// Expurgo do corpo (FND-16 CA 14, ADR 0031 §5): eventos <c>Processed</c> ou <c>Ignored</c> com mais de 90 dias perdem o
/// <c>payload</c>; a linha fica (provedor, ids, hash, tipo, datas, situação) para a deduplicação continuar valendo. <c>Failed</c> e
/// <c>DeadLettered</c> não são expurgados. Idempotente.
/// </summary>
[Privacy.PersonalDataPurge(typeof(WebhookEvent), 90, "Corpo do aviso apagado em 90 dias (ADR 0031)")]
public sealed partial class WebhookEventsPurgeJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<WebhookEventsPurgeJob> logger)
    : TenantJob<WebhookEventsPurgeArgs>(directory, scopes, logger)
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, WebhookEventsPurgeArgs args, CancellationToken cancellationToken)
    {
        var cutoff = services.GetRequiredService<TimeProvider>().GetUtcNow() - Retention;
        var purged = await services.GetRequiredService<BemAquiDbContext>().WebhookEvents
            .Where(e => e.Payload != null && e.ReceivedAt < cutoff && (e.Status == WebhookEventStatus.Processed || e.Status == WebhookEventStatus.Ignored))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Payload, (byte[]?)null), cancellationToken);
        LogPurged(logger, purged, tenant.Key);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Expurgo de webhooks: corpo apagado de {Purged} eventos no tenant {Tenant}.")]
    private static partial void LogPurged(ILogger logger, int purged, string tenant);
}

/// <summary>Recorrente global diário (03:00 de São Paulo), um expurgo por tenant ativo, na fila de manutenção.</summary>
public sealed class WebhookEventsPurgeRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<WebhookEventsPurgeJob, WebhookEventsPurgeArgs>(directory, jobs)
{
    public const string Id = "webhook-events-purge";
    public const string Cron = "0 3 * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override WebhookEventsPurgeArgs ArgsFor(TenantInfo tenant) => new();
}

public sealed record WebhookStalledSweepArgs;

/// <summary>
/// Varredor de reserva (CA 11, ADR 0031 §3): evento <c>Received</c> há mais de 5 min sem processamento volta ao outbox (mensagem
/// nova; o job é idempotente). Depois de <see cref="StalledAfter"/> voltas, sai o alerta <c>webhook_stalled</c> (FND-12).
/// </summary>
public sealed partial class WebhookStalledSweepJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<WebhookStalledSweepJob> logger)
    : TenantJob<WebhookStalledSweepArgs>(directory, scopes, logger)
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);
    public const int StalledAfter = 3;
    public const int BatchSize = 200;

    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, WebhookStalledSweepArgs args, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<BemAquiDbContext>();
        var publisher = services.GetRequiredService<IEventPublisher>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var cutoff = now - StaleAfter;
        var stale = await db.WebhookEvents
            .Where(e => e.Status == WebhookEventStatus.Received && e.LastQueuedAt < cutoff)
            .OrderBy(e => e.ReceivedAt).Take(BatchSize).ToListAsync(cancellationToken);
        if (stale.Count == 0)
        {
            return;
        }

        foreach (var evt in stale)
        {
            evt.Requeues++;
            evt.LastQueuedAt = now;
            publisher.Publish(new WebhookReceived(evt.Id, evt.Provider));
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return; // processado no meio da varredura: a próxima passada confere de novo
        }

        LogRequeued(logger, stale.Count, tenant.Key);
        var stalled = stale.Where(e => e.Requeues >= StalledAfter).ToList();
        if (stalled.Count > 0)
        {
            await services.GetRequiredService<IAlertSink>().RaiseAsync(
                new Alert(WebhookAlerts.Stalled, tenant.Key, stalled.Count + " avisos recebidos continuam parados", "Provedores: " + string.Join(", ", stalled.Select(e => e.Provider).Distinct(StringComparer.Ordinal)), WebhookLinks.Events),
                cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhooks: {Count} eventos parados recolocados no outbox no tenant {Tenant}.")]
    private static partial void LogRequeued(ILogger logger, int count, string tenant);
}

/// <summary>Recorrente global a cada 5 minutos, um varredor por tenant ativo.</summary>
public sealed class WebhookStalledSweepRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<WebhookStalledSweepJob, WebhookStalledSweepArgs>(directory, jobs)
{
    public const string Id = "webhook-events-sweep";
    public const string Cron = "*/5 * * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override WebhookStalledSweepArgs ArgsFor(TenantInfo tenant) => new();
}
