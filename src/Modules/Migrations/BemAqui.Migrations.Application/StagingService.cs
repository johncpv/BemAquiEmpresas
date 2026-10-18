using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Migrations.Domain;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BemAqui.Migrations.Application;

/// <summary>Seção <c>Migration</c>: onde ficam os arquivos do relatório (volume da API, dono 1654) e o prazo da staging.</summary>
public sealed class MigrationOptions
{
    public const string Section = "Migration";

    public string ReportDirectory { get; set; } = "/data/files/migration-reports";

    /// <summary>Prazo máximo da staging depois da carga (ADR 0018).</summary>
    public int StagingDays { get; set; } = 30;
}

public sealed record PurgeResult(int Runs, int Items, int Files);

/// <summary>
/// Expurgo da staging (CA 22, ADR 0018): apaga <c>import_items</c> e os arquivos do relatório e mantém <c>import_runs</c>
/// (só os totais) e <c>external_refs</c>, que a reexecução precisa. Com <c>accepted</c>, registra o aceite do PO.
/// </summary>
public sealed class StagingService(IMigrationDb db, IOptions<MigrationOptions> options, TimeProvider clock)
{
    public async Task<PurgeResult> PurgeAsync(bool accepted, CancellationToken cancellationToken = default)
    {
        var runs = await db.ImportRuns.Where(r => r.PurgedAt == null && r.Status != ImportRunStatus.Running).ToListAsync(cancellationToken);
        var ids = runs.Select(r => r.Id).ToList();
        var items = await db.ImportItems.Where(i => ids.Contains(i.RunId)).ExecuteDeleteAsync(cancellationToken);
        var files = 0;
        var now = clock.GetUtcNow();
        foreach (var run in runs)
        {
            foreach (var extension in new[] { "json", "csv" })
            {
                var path = Path.Combine(options.Value.ReportDirectory, ImportReportWriter.FileName(run.Id, extension));
                if (File.Exists(path))
                {
                    File.Delete(path);
                    files++;
                }
            }

            if (accepted)
            {
                run.Accept(now);
            }

            run.MarkPurged(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new PurgeResult(runs.Count, items, files);
    }

    /// <summary>Execuções com staging guardada além do prazo (alerta <c>migration_staging_expired</c>).</summary>
    public async Task<IReadOnlyList<ImportRun>> ExpiredAsync(CancellationToken cancellationToken = default)
    {
        var limit = clock.GetUtcNow().AddDays(-options.Value.StagingDays);
        return await db.ImportRuns.AsNoTracking()
            .Where(r => r.PurgedAt == null && r.StartedAt <= limit && db.ImportItems.Any(i => i.RunId == r.Id))
            .OrderBy(r => r.StartedAt)
            .ToListAsync(cancellationToken);
    }
}

public sealed record StagingCheckArgs;

/// <summary>Uma vez por dia, por tenant: staging com mais de 30 dias sem expurgo dispara o alerta aos super admins (CA 22, FND-12).</summary>
public sealed class MigrationStagingCheckJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<MigrationStagingCheckJob> logger)
    : TenantJob<StagingCheckArgs>(directory, scopes, logger)
{
    public const string AlertKind = "migration_staging_expired";

    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, StagingCheckArgs args, CancellationToken cancellationToken)
    {
        var expired = await services.GetRequiredService<StagingService>().ExpiredAsync(cancellationToken);
        if (expired.Count == 0)
        {
            return;
        }

        await services.GetRequiredService<IAlertSink>().RaiseAsync(new Alert(AlertKind, tenant.Key,
            "Staging da importação do Dolibarr passou do prazo de 30 dias",
            expired.Count + " execução(ões) com staging guardada. Rode tenant import-dolibarr --purge-staging --tenant " + tenant.Key + " (ADR 0018)."), cancellationToken);
    }
}

/// <summary>Recorrente global diário (08:00 de São Paulo) que enfileira a verificação por tenant ativo.</summary>
public sealed class MigrationStagingRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<MigrationStagingCheckJob, StagingCheckArgs>(directory, jobs)
{
    public const string Id = "migration-staging-check";
    public const string Cron = "0 8 * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override StagingCheckArgs ArgsFor(TenantInfo tenant) => new();
}
