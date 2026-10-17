using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.BuildingBlocks.Attachments;

public sealed record RescanArgs(int Limit = 50);

/// <summary>Varre de novo os anexos que ficaram pendentes (ClamAV fora do ar no upload). Infectado é apagado.</summary>
public sealed class RescanPendingAttachmentsJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<RescanPendingAttachmentsJob> logger)
    : TenantJob<RescanArgs>(directory, scopes, logger)
{
    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, RescanArgs args, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<BemAquiDbContext>();
        var storage = services.GetRequiredService<IFileStorage>();
        var scanner = services.GetRequiredService<IVirusScanner>();
        var clock = services.GetRequiredService<TimeProvider>();

        var pending = await db.Attachments
            .Where(a => a.ScanStatus == ScanStatus.Pending || a.ScanStatus == ScanStatus.Failed)
            .OrderBy(a => a.CreatedAt)
            .Take(args.Limit)
            .ToListAsync(cancellationToken);

        foreach (var attachment in pending)
        {
            ScanResult result;
            await using (var content = await storage.OpenReadAsync(attachment.StoragePath, cancellationToken))
            {
                result = await scanner.ScanAsync(content, cancellationToken);
            }

            switch (result.Verdict)
            {
                case ScanVerdict.Clean:
                    attachment.ScanStatus = ScanStatus.Clean;
                    attachment.ScannedAt = clock.GetUtcNow();
                    break;
                case ScanVerdict.Infected:
                    await storage.DeleteAsync(attachment.StoragePath, cancellationToken);
                    attachment.ScanStatus = ScanStatus.Infected;
                    attachment.ScannedAt = clock.GetUtcNow();
                    break;
                default:
                    attachment.ScanStatus = ScanStatus.Pending;
                    break;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>A cada 10 minutos, um rescan por tenant ativo na fila de manutenção.</summary>
public sealed class RescanPendingAttachmentsRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<RescanPendingAttachmentsJob, RescanArgs>(directory, jobs)
{
    public const string Id = "attachments-rescan";
    public const string Cron = "*/10 * * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override RescanArgs ArgsFor(TenantInfo tenant) => new();
}
