using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Identity.Domain;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Identity.Application.Privacy;

/// <summary>Argumentos da exportação (CA 8, §4.3 da revisão): só ids, nunca dado do titular.</summary>
public sealed record PrivacyExportArgs(Guid PrivacyRequestId, Guid RequestedBy);

/// <summary>"Exportar dados do titular" (CA 8): monta o ZIP no escopo do tenant e avisa quem pediu no sino.</summary>
public sealed class PrivacyExportJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<PrivacyExportJob> logger)
    : TenantJob<PrivacyExportArgs>(directory, scopes, logger)
{
    protected override Task RunAsync(IServiceProvider services, TenantInfo tenant, PrivacyExportArgs args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(args);
        return services.GetRequiredService<PrivacyExportService>().RunAsync(args.PrivacyRequestId, args.RequestedBy, cancellationToken);
    }
}

/// <summary>
/// Aviso do prazo (CA 3): pedido aberto a 3 dias ou menos do prazo, ou vencido, avisa cada membro do Proprietário uma vez por dia
/// (<c>privacy-due:{id}:{data}</c>). Idempotente: rodar de novo no mesmo dia não duplica.
/// </summary>
public sealed class PrivacyDueNotices(IIdentityDb db, IUserDirectory users, INotificationService notifications, TimeProvider clock)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var today = BusinessTime.Today(clock);
        var limit = today.AddDays(PrivacyRequestRules.WarningDays);
        var due = await db.PrivacyRequests.AsNoTracking().Where(r => r.Status == PrivacyRequestStatus.Open && r.DueOn <= limit)
            .Select(r => new { r.Id, r.Number, r.DueOn }).ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        var owners = await users.OwnerIdsAsync(cancellationToken);
        var sent = 0;
        foreach (var request in due)
        {
            var title = $"Pedido {request.Number} do titular vence em {request.DueOn:dd/MM/yyyy}.";
            foreach (var owner in owners)
            {
                await notifications.NotifyOnceAsync(owner, PrivacyRequestService.NotificationType, title, PrivacyRequestService.Link(request.Id),
                    $"privacy-due:{request.Id:D}:{today:yyyy-MM-dd}", cancellationToken);
                sent++;
            }
        }

        return sent;
    }
}

public sealed record PrivacyDueArgs;

/// <summary>Execução por tenant do aviso do prazo; pula o tenant com o módulo <c>settings</c> não liberado (CA 3).</summary>
public sealed partial class PrivacyDueJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<PrivacyDueJob> logger)
    : TenantJob<PrivacyDueArgs>(directory, scopes, logger)
{
    protected override Task RunAsync(IServiceProvider services, TenantInfo tenant, PrivacyDueArgs args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tenant);
        if (services.GetRequiredService<TenantContextHolder>().Resolved?.IsReleased("settings") != true)
        {
            var log = services.GetRequiredService<ILogger<PrivacyDueJob>>();
            LogNotReleased(log, tenant.Id);
            return Task.CompletedTask;
        }

        return services.GetRequiredService<PrivacyDueNotices>().RunAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Aviso de prazo do titular não executado para o tenant {TenantId}: módulo settings não liberado.")]
    private static partial void LogNotReleased(ILogger logger, Guid tenantId);
}

/// <summary>Recorrente global diário (08:00 de São Paulo): um aviso de prazo por tenant ativo.</summary>
public sealed class PrivacyDueRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<PrivacyDueJob, PrivacyDueArgs>(directory, jobs)
{
    public const string Id = "privacy-due-notices";
    public const string Cron = "0 8 * * *";

    protected override string Queue => JobQueues.Default;

    protected override PrivacyDueArgs ArgsFor(TenantInfo tenant) => new();
}

/// <summary>
/// Expurgo da exportação (CA 10): 30 dias depois de concluído ou recusado o pedido, o ZIP sai do storage e da tabela de anexos e o pedido fica
/// com a exportação "Expired". Idempotente.
/// </summary>
public sealed class PrivacyExportPurge(IIdentityDb db, AttachmentService attachments, TimeProvider clock)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = clock.GetUtcNow() - PrivacyRequestRules.ExportRetention;
        var expired = await db.PrivacyRequests.Where(r => r.Status != PrivacyRequestStatus.Open && r.ClosedAt < cutoff && r.ExportAttachmentId != null).ToListAsync(cancellationToken);
        foreach (var request in expired)
        {
            await attachments.DeleteSystemFileAsync(PrivacyRequest.OwnerType, request.Id, request.ExportAttachmentId!.Value, cancellationToken);
            request.ExportAttachmentId = null;
            request.ExportStatus = PrivacyExportStatus.Expired;
            request.DownloadTokenHash = null;
            request.DownloadTokenExpiresAt = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }
}

public sealed record PrivacyExportPurgeArgs;

/// <summary>Execução por tenant do expurgo das exportações (CA 10).</summary>
public sealed class PrivacyExportPurgeJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<PrivacyExportPurgeJob> logger)
    : TenantJob<PrivacyExportPurgeArgs>(directory, scopes, logger)
{
    protected override Task RunAsync(IServiceProvider services, TenantInfo tenant, PrivacyExportPurgeArgs args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetRequiredService<PrivacyExportPurge>().RunAsync(cancellationToken);
    }
}

/// <summary>Recorrente global diário (03:45 de São Paulo), na fila de manutenção.</summary>
public sealed class PrivacyExportPurgeRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<PrivacyExportPurgeJob, PrivacyExportPurgeArgs>(directory, jobs)
{
    public const string Id = "privacy-export-purge";
    public const string Cron = "45 3 * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override PrivacyExportPurgeArgs ArgsFor(TenantInfo tenant) => new();
}
