using BemAqui.Migrations.Application;
using Hangfire;

namespace BemAqui.Host;

/// <summary>
/// Recorrentes globais do Hangfire, registrados na subida do processo que roda o servidor de jobs (ADR 0006: um recorrente
/// por rotina, que enfileira um job por tenant). Hoje: o prazo da staging da importação (MIG-01a CA 22), a expiração de orçamentos, as despesas recorrentes, o aviso de vencidos, a "OS atrasada" (PRD-02 CA 11), a recomposição do custo dos apontamentos (PRD-04 CA 12) os eventos de afastamento (CAD-04c1) e o recálculo do plano de produção de hora em hora (PLN-01a).
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
        // Vencimento do certificado do Inter (BNK-01a CA 15): 08:00 de São Paulo, um job por tenant.
        recurring.AddOrUpdate<BemAqui.Finance.Application.InterCertificateExpiryRecurringJob>(BemAqui.Finance.Application.InterCertificateExpiryRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            BemAqui.Finance.Application.InterCertificateExpiryRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        // Conferência dos boletos com o Inter (BNK-02 CA 11): 07:00 e 13:00 de São Paulo, um job por tenant com o finance liberado.
        recurring.AddOrUpdate<BemAqui.Finance.Application.InterBillingReconcileRecurringJob>(BemAqui.Finance.Application.InterBillingReconcileRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            BemAqui.Finance.Application.InterBillingReconcileRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        // Extrato do banco pela API do Inter (BNK-03a CA 1): 06:30 de São Paulo, um job por tenant com o finance liberado.
        recurring.AddOrUpdate<BemAqui.Finance.Application.Statements.InterStatementImportRecurringJob>(BemAqui.Finance.Application.Statements.InterStatementImportRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            BemAqui.Finance.Application.Statements.InterStatementImportRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        // Régua de cobrança (FIN-09 CA 4): a cada 15 min das 07:00 às 20:45 de São Paulo; cada tenant envia a partir do horário da régua.
        recurring.AddOrUpdate<BemAqui.Finance.Application.CollectionRemindersRecurringJob>(BemAqui.Finance.Application.CollectionRemindersRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            BemAqui.Finance.Application.CollectionRemindersRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
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
        // Webhooks (FND-16 CA 11 e 14): expurgo do corpo com mais de 90 dias às 03:00 de São Paulo e varredor de eventos parados a cada 5 min.
        recurring.AddOrUpdate<BemAqui.BuildingBlocks.Webhooks.WebhookEventsPurgeRecurringJob>(BemAqui.BuildingBlocks.Webhooks.WebhookEventsPurgeRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            BemAqui.BuildingBlocks.Webhooks.WebhookEventsPurgeRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        recurring.AddOrUpdate<BemAqui.BuildingBlocks.Webhooks.WebhookStalledSweepRecurringJob>(BemAqui.BuildingBlocks.Webhooks.WebhookStalledSweepRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            BemAqui.BuildingBlocks.Webhooks.WebhookStalledSweepRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        // Conferência de notas fiscais pendentes (FIS-01 CA 14): a cada 10 min, um job por tenant com o módulo fiscal liberado.
        recurring.AddOrUpdate<BemAqui.Fiscal.Application.Invoices.FiscalPendingCheckRecurringJob>(BemAqui.Fiscal.Application.Invoices.FiscalPendingCheckRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            BemAqui.Fiscal.Application.Invoices.FiscalPendingCheckRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        // FIS-02a CA 14: aviso do vencimento do certificado A1, diário às 08:00 de São Paulo.
        recurring.AddOrUpdate<BemAqui.Fiscal.Application.Certificates.FiscalCertificateExpiryRecurringJob>(BemAqui.Fiscal.Application.Certificates.FiscalCertificateExpiryRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            BemAqui.Fiscal.Application.Certificates.FiscalCertificateExpiryRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        // 07:00 de São Paulo como valor inicial; o horário é parâmetro (Jobs:Cron:production-late-orders).
        recurring.AddOrUpdate<BemAqui.Production.Application.LateOrdersRecurringJob>(BemAqui.Production.Application.LateOrdersRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            configuration["Jobs:Cron:" + BemAqui.Production.Application.LateOrdersRecurringJob.Id] is { Length: > 0 } cron ? cron : BemAqui.Production.Application.LateOrdersRecurringJob.Cron,
            new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        // Recálculo do plano de produção de hora em hora (PLN-01a CA 13), fuso de São Paulo; só tenants com a Produção liberada.
        recurring.AddOrUpdate<BemAqui.Production.Application.PlanningRecalculateRecurringJob>(BemAqui.Production.Application.PlanningRecalculateRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            BemAqui.Production.Application.PlanningRecalculateRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        // Aparelhos da fábrica sem uso há 30 dias ou vencidos (PRD-03b CA 8): 00:30 de São Paulo.
        recurring.AddOrUpdate<BemAqui.Identity.Application.Shopfloor.ShopfloorDevicesRecurringJob>(BemAqui.Identity.Application.Shopfloor.ShopfloorDevicesRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            BemAqui.Identity.Application.Shopfloor.ShopfloorDevicesRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        // Resumo diário dos lançamentos manuais do estoque ao Proprietário (EST-01a CA 12, D7): 06:30 de São Paulo, valor inicial.
        recurring.AddOrUpdate<BemAqui.Inventory.Application.ManualDigestRecurringJob>(BemAqui.Inventory.Application.ManualDigestRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            configuration["Jobs:Cron:" + BemAqui.Inventory.Application.ManualDigestRecurringJob.Id] is { Length: > 0 } digest ? digest : BemAqui.Inventory.Application.ManualDigestRecurringJob.Cron,
            new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        // Solicitações de compra (CMP-01): a recomposição das faltas que já existiam (D3, CA 7) dispara na subida e repete às 03:50; o
        // job do estoque mínimo (CA 9) roda às 06:00 de São Paulo (valor inicial; Jobs:Cron:purchasing-min-stock muda) e antes recompõe
        // as faltas de novo (rede de segurança).
        recurring.AddOrUpdate<BemAqui.Purchasing.Application.ShortageBackfillRecurringJob>(BemAqui.Purchasing.Application.ShortageBackfillRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            BemAqui.Purchasing.Application.ShortageBackfillRecurringJob.Cron, new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        recurring.Trigger(BemAqui.Purchasing.Application.ShortageBackfillRecurringJob.Id);
        recurring.AddOrUpdate<BemAqui.Purchasing.Application.MinStockPurchaseRequestRecurringJob>(BemAqui.Purchasing.Application.MinStockPurchaseRequestRecurringJob.Id, j => j.ExecuteAsync(CancellationToken.None),
            configuration["Jobs:Cron:" + BemAqui.Purchasing.Application.MinStockPurchaseRequestRecurringJob.Id] is { Length: > 0 } minStock ? minStock : BemAqui.Purchasing.Application.MinStockPurchaseRequestRecurringJob.Cron,
            new RecurringJobOptions { TimeZone = BemAqui.BuildingBlocks.Time.BusinessTime.Zone });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
