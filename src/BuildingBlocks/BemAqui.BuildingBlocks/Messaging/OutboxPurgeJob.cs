using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Privacy;
using BemAqui.BuildingBlocks.Tenancy;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.BuildingBlocks.Messaging;

public sealed record OutboxPurgeArgs;

/// <summary>
/// Expurgo diário do outbox e da inbox (ADR 0037 §2, LGPD-01a CA 6): mensagem entregue (<c>processed_at</c>) há mais de 30 dias sai; a que
/// foi para a fila de mortas fica (pode ser reprocessada). A entrega já feita a um handler (inbox) sai quando a mensagem dela não existe
/// mais, para o reprocessamento nunca entregar de novo a quem já recebeu. O evento leva só ids (teste de arquitetura), então o expurgo é
/// higiene: o que fica não guarda dado pessoal. Idempotente.
/// </summary>
[PersonalDataPurge(typeof(OutboxMessage), 30, "Mensagem entregue apagada em 30 dias; o evento só leva ids")]
[PersonalDataPurge(typeof(InboxMessage), 30, "Entrega apagada junto com a mensagem")]
public sealed partial class OutboxPurgeJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<OutboxPurgeJob> logger)
    : TenantJob<OutboxPurgeArgs>(directory, scopes, logger)
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, OutboxPurgeArgs args, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<BemAquiDbContext>();
        var cutoff = services.GetRequiredService<TimeProvider>().GetUtcNow() - Retention;
        var messages = await db.OutboxMessages.Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff && m.DeadLetteredAt == null).ExecuteDeleteAsync(cancellationToken);
        var deliveries = await db.InboxMessages.Where(i => i.ProcessedAt < cutoff && !db.OutboxMessages.Any(m => m.Id == i.MessageId)).ExecuteDeleteAsync(cancellationToken);
        LogPurged(logger, messages, deliveries, tenant.Key);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Expurgo do outbox: {Messages} mensagens e {Deliveries} entregas apagadas no tenant {Tenant}.")]
    private static partial void LogPurged(ILogger logger, int messages, int deliveries, string tenant);
}

/// <summary>Recorrente global diário (03:30 de São Paulo), um expurgo por tenant ativo, na fila de manutenção.</summary>
public sealed class OutboxPurgeRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<OutboxPurgeJob, OutboxPurgeArgs>(directory, jobs)
{
    public const string Id = "outbox-purge";
    public const string Cron = "30 3 * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override OutboxPurgeArgs ArgsFor(TenantInfo tenant) => new();
}
