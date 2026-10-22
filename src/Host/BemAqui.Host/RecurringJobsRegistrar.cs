using BemAqui.Migrations.Application;
using Hangfire;

namespace BemAqui.Host;

/// <summary>
/// Recorrentes globais do Hangfire, registrados na subida do processo que roda o servidor de jobs (ADR 0006: um recorrente
/// por rotina, que enfileira um job por tenant). Hoje: o prazo da staging da importação (MIG-01a CA 22), a expiração de orçamentos, as despesas recorrentes, o aviso de vencidos e os eventos de afastamento (CAD-04c1).
/// </summary>
internal sealed class RecurringJobsRegistrar(IRecurringJobManager recurring, IConfiguration configuration) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        recurring.AddOrUpdate<MigrationStagingRecurringJob>(MigrationStagingRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            MigrationStagingRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        recurring.AddOrUpdate<BemAqui.Parties.Application.CnpjLookupPurgeRecurringJob>(BemAqui.Parties.Application.CnpjLookupPurgeRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            BemAqui.Parties.Application.CnpjLookupPurgeRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        recurring.AddOrUpdate<BemAqui.Finance.Application.RecurringPayablesRecurringJob>(BemAqui.Finance.Application.RecurringPayablesRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            BemAqui.Finance.Application.RecurringPayablesRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        recurring.AddOrUpdate<BemAqui.Finance.Application.OverdueReceivablesRecurringJob>(BemAqui.Finance.Application.OverdueReceivablesRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            BemAqui.Finance.Application.OverdueReceivablesRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        recurring.AddOrUpdate<BemAqui.Commercial.Application.ExpireQuotesRecurringJob>(BemAqui.Commercial.Application.ExpireQuotesRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            BemAqui.Commercial.Application.ExpireQuotesRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        // Afastamentos com data futura (CAD-04c1 CA 7): 00:15 de São Paulo, ou o horário do parâmetro.
        recurring.AddOrUpdate<BemAqui.HR.Application.EmployeeLeaveEventsRecurringJob>(BemAqui.HR.Application.EmployeeLeaveEventsRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            configuration[BemAqui.HR.Application.EmployeeLeaveEventsRecurringJob.CronSetting] ?? BemAqui.HR.Application.EmployeeLeaveEventsRecurringJob.Cron,
            new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        recurring.AddOrUpdate<BemAqui.BuildingBlocks.Persistence.IdempotencyPurgeRecurringJob>(BemAqui.BuildingBlocks.Persistence.IdempotencyPurgeRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            BemAqui.BuildingBlocks.Persistence.IdempotencyPurgeRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
