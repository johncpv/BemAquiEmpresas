using BemAqui.Migrations.Application;
using Hangfire;

namespace BemAqui.Host;

/// <summary>
/// Recorrentes globais do Hangfire, registrados na subida do processo que roda o servidor de jobs (ADR 0006: um recorrente
/// por rotina, que enfileira um job por tenant). Hoje: o prazo da staging da importação (MIG-01a CA 22).
/// </summary>
internal sealed class RecurringJobsRegistrar(IRecurringJobManager recurring) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurring.AddOrUpdate<MigrationStagingRecurringJob>(MigrationStagingRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            MigrationStagingRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        recurring.AddOrUpdate<BemAqui.Parties.Application.CnpjLookupPurgeRecurringJob>(BemAqui.Parties.Application.CnpjLookupPurgeRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            BemAqui.Parties.Application.CnpjLookupPurgeRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        recurring.AddOrUpdate<BemAqui.Finance.Application.RecurringPayablesRecurringJob>(BemAqui.Finance.Application.RecurringPayablesRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            BemAqui.Finance.Application.RecurringPayablesRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
