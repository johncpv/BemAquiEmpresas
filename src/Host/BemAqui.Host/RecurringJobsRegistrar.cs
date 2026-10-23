using BemAqui.Migrations.Application;
using Hangfire;

namespace BemAqui.Host;

/// <summary>
/// Recorrentes globais do Hangfire, registrados na subida do processo que roda o servidor de jobs (ADR 0006: um recorrente
/// por rotina, que enfileira um job por tenant). Hoje: o prazo da staging da importação (MIG-01a CA 22), a expiração de orçamentos, as despesas recorrentes, o aviso de vencidos, a "OS atrasada" (PRD-02 CA 11), a recomposição do custo dos apontamentos (PRD-04 CA 12) e os eventos de afastamento (CAD-04c1).
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
        // Recomposição do custo dos apontamentos fechados antes da PRD-04 (D3, CA 12): dispara já na subida (depois do deploy) e
        // repete de madrugada; idempotente, termina sem fazer nada quando não há pendente.
        recurring.AddOrUpdate<BemAqui.Production.Application.LaborCostBackfillRecurringJob>(BemAqui.Production.Application.LaborCostBackfillRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            BemAqui.Production.Application.LaborCostBackfillRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        recurring.Trigger(BemAqui.Production.Application.LaborCostBackfillRecurringJob.Id);
        // Recomposição das reservas das BOMs já liberadas (EST-02 CA 12, D3): dispara na subida e repete de madrugada; depois da
        // primeira execução completa no tenant, não faz nada.
        recurring.AddOrUpdate<BemAqui.Inventory.Application.ReservationBackfillRecurringJob>(BemAqui.Inventory.Application.ReservationBackfillRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            BemAqui.Inventory.Application.ReservationBackfillRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        recurring.Trigger(BemAqui.Inventory.Application.ReservationBackfillRecurringJob.Id);
        // Recomposição do custo de material das saídas com projeto publicadas antes do consumidor (EST-03 CA 11, D3): dispara na subida e
        // repete de madrugada; sem nada faltando na razão, não grava.
        recurring.AddOrUpdate<BemAqui.Commercial.Application.MaterialCostBackfillRecurringJob>(BemAqui.Commercial.Application.MaterialCostBackfillRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            BemAqui.Commercial.Application.MaterialCostBackfillRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        recurring.Trigger(BemAqui.Commercial.Application.MaterialCostBackfillRecurringJob.Id);
        // Afastamentos com data futura (CAD-04c1 CA 7): 00:15 de São Paulo, ou o horário do parâmetro.
        recurring.AddOrUpdate<BemAqui.HR.Application.EmployeeLeaveEventsRecurringJob>(BemAqui.HR.Application.EmployeeLeaveEventsRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            configuration[BemAqui.HR.Application.EmployeeLeaveEventsRecurringJob.CronSetting] ?? BemAqui.HR.Application.EmployeeLeaveEventsRecurringJob.Cron,
            new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        recurring.AddOrUpdate<BemAqui.BuildingBlocks.Persistence.IdempotencyPurgeRecurringJob>(BemAqui.BuildingBlocks.Persistence.IdempotencyPurgeRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            BemAqui.BuildingBlocks.Persistence.IdempotencyPurgeRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        // 07:00 de São Paulo como valor inicial; o horário é parâmetro (Jobs:Cron:production-late-orders).
        recurring.AddOrUpdate<BemAqui.Production.Application.LateOrdersRecurringJob>(BemAqui.Production.Application.LateOrdersRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            configuration["Jobs:Cron:" + BemAqui.Production.Application.LateOrdersRecurringJob.Id] is { Length: > 0 } cron ? cron : BemAqui.Production.Application.LateOrdersRecurringJob.Cron,
            new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        // Aparelhos da fábrica sem uso há 30 dias ou vencidos (PRD-03b CA 8): 00:30 de São Paulo.
        recurring.AddOrUpdate<BemAqui.Identity.Application.Shopfloor.ShopfloorDevicesRecurringJob>(BemAqui.Identity.Application.Shopfloor.ShopfloorDevicesRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            BemAqui.Identity.Application.Shopfloor.ShopfloorDevicesRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        // Resumo diário dos lançamentos manuais do estoque ao Proprietário (EST-01a CA 12, D7): 06:30 de São Paulo, valor inicial.
        recurring.AddOrUpdate<BemAqui.Inventory.Application.ManualDigestRecurringJob>(BemAqui.Inventory.Application.ManualDigestRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            configuration["Jobs:Cron:" + BemAqui.Inventory.Application.ManualDigestRecurringJob.Id] is { Length: > 0 } digest ? digest : BemAqui.Inventory.Application.ManualDigestRecurringJob.Cron,
            new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
