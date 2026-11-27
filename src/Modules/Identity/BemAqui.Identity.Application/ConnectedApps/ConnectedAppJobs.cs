using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Domain;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Identity.Application.ConnectedApps;

/// <summary>
/// Rotinas diárias da conexão (INT-01 CA 14 e regras): aviso 30 dias antes e suspensão aos 12 meses do consentimento; revogação depois de 30 dias
/// com o módulo <c>conecta</c> desligado; tokens, códigos e intenções vencidos ou revogados apagados depois de 30 dias (LGPD).
/// </summary>
public sealed class ConnectedAppMaintenance(IIdentityDb db, AppConnectionLifecycle lifecycle, INotificationService notifications,
    BemAqui.BuildingBlocks.Identity.IUserDirectory users, TenantContextHolder tenant, TimeProvider clock)
{
    public static readonly TimeSpan NoticeBefore = TimeSpan.FromDays(30);

    public async Task ReconsentAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var released = tenant.Resolved?.IsReleased(ConnectedAppModule);
        var open = await db.AppConnections.Where(c => c.Status != AppConnectionStatus.Revoked).ToListAsync(cancellationToken);
        var changed = new List<AppConnection>();
        foreach (var connection in open)
        {
            if (released is { } on)
            {
                connection.MarkModuleOff(!on, now);
                if (connection.ModuleOffSince is { } since && now - since > AppConnection.ModuleOffGrace
                    && await lifecycle.RevokeAsync(connection, AppConnectionReason.ModuleOff, AppConnectionLifecycle.ModuleOffText, null, notifyOwners: true, cancellationToken))
                {
                    changed.Add(connection);
                    continue;
                }
            }

            if (connection.Status != AppConnectionStatus.Active)
            {
                continue;
            }

            if (connection.ReconsentDueAt <= now)
            {
                if (await lifecycle.SuspendAsync(connection, AppConnectionReason.ReconsentDue, "Conecta: autorização vencida. Autorize de novo para continuar.", cancellationToken))
                {
                    changed.Add(connection);
                }
            }
            else if (connection.ReconsentDueAt - now <= NoticeBefore)
            {
                foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
                {
                    await notifications.NotifyOnceAsync(owner, AppConnectionLifecycle.NotificationType,
                        "Conecta: a autorização vence em " + AppConnectionLifecycle.Day(connection.ReconsentDueAt) + ". Autorize de novo para não parar.",
                        AppConnectionLifecycle.PageLink, "conecta-reconsent:" + connection.Id, cancellationToken);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        changed.ForEach(lifecycle.AfterSave);
    }

    public const string ConnectedAppModule = "conecta";

    public async Task PurgeAsync(CancellationToken cancellationToken = default)
    {
        var limit = clock.GetUtcNow() - AppToken.PurgeAfter;
        await db.AppTokens.Where(t => t.ExpiresAt < limit || t.RevokedAt < limit || t.ReplacedAt < limit).ExecuteDeleteAsync(cancellationToken);
        await db.AppAuthorizationCodes.Where(c => c.ExpiresAt < limit).ExecuteDeleteAsync(cancellationToken);
        await db.AppConnectIntents.Where(i => i.ExpiresAt < limit).ExecuteDeleteAsync(cancellationToken);
    }
}

public sealed record ConnectedAppDailyArgs;

/// <summary>Job <c>connected-apps-daily</c> de um tenant: nova autorização (<c>AppReconsentJob</c>) e limpeza (<c>AppTokensPurgeJob</c>).</summary>
public sealed class ConnectedAppDailyJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<ConnectedAppDailyJob> logger)
    : TenantJob<ConnectedAppDailyArgs>(directory, scopes, logger)
{
    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, ConnectedAppDailyArgs args, CancellationToken cancellationToken)
    {
        var maintenance = services.GetRequiredService<ConnectedAppMaintenance>();
        await maintenance.ReconsentAsync(cancellationToken);
        await maintenance.PurgeAsync(cancellationToken);
    }
}

/// <summary>Recorrente global diário que enfileira a rotina por tenant ativo.</summary>
public sealed class ConnectedAppDailyRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<ConnectedAppDailyJob, ConnectedAppDailyArgs>(directory, jobs)
{
    public const string Id = "connected-apps-daily";
    public const string Cron = "15 4 * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override ConnectedAppDailyArgs ArgsFor(TenantInfo tenant) => new();
}
