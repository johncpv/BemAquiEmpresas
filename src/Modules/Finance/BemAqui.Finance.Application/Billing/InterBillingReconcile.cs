using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Domain;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Finance.Application;

/// <summary>
/// Conferência diária dos boletos (BNK-02 CA 11 e 12), para o caso de o aviso falhar: para cada conta com a integração ativa, consulta no
/// Inter as cobranças pagas, canceladas e expiradas desde a última execução (com 2 dias de sobreposição; na primeira, os últimos 30 dias)
/// e os boletos <c>Requested</c> há mais de 1 h; aplica as mesmas transições do aviso pelo <see cref="BankPaymentService"/>. Idempotente:
/// o boleto já pago termina sem efeito. O relatório (consultados, baixados, divergentes) fica na integração. Falha da consulta não muda
/// nenhum título; depois de 3 execuções seguidas sem sucesso, alerta <c>bank_integration</c> e aviso aos Proprietários e a quem tem
/// <c>finance.edit</c>.
/// </summary>
public sealed partial class InterBillingReconciler(
    IFinanceDb db,
    BankIntegrationService integrations,
    IBankBillingProvider bank,
    BankPaymentService payments,
    INotificationService notifications,
    IUserDirectory users,
    IAlertSink alerts,
    ITenantContext tenant,
    TimeProvider clock,
    ILogger<InterBillingReconciler> logger)
{
    public const int OverlapDays = 2;
    public const int FirstRunDays = 30;
    public const int FailuresToAlert = 3;
    public static readonly TimeSpan RequestedAfter = TimeSpan.FromHours(1);
    public const string FailureNotification = "finance.billing_reconcile_failed";

    public async Task<IReadOnlyList<ReconcileReport>> RunAsync(CancellationToken cancellationToken = default)
    {
        var ids = await db.BankIntegrations.AsNoTracking().Where(i => i.Status == BankIntegrationStatus.Active && i.Provider == BankProvider.Inter)
            .Select(i => i.Id).ToListAsync(cancellationToken);
        var reports = new List<ReconcileReport>();
        foreach (var id in ids)
        {
            if (await RunAsync(id, cancellationToken) is { } report)
            {
                reports.Add(report);
            }
        }

        return reports;
    }

    private async Task<ReconcileReport?> RunAsync(Guid integrationId, CancellationToken ct)
    {
        var integration = await db.BankIntegrations.AsNoTracking().FirstAsync(i => i.Id == integrationId, ct);
        var allowed = await integrations.EffectsAllowedAsync(integration.BankAccountId, ct);
        if (allowed.IsFailure)
        {
            await FailedAsync(integrationId, allowed.Error!.Message, ct);
            return null;
        }

        var today = BusinessTime.Today(clock);
        var from = (integration.LastReconcileAt is { } last ? DateOnly.FromDateTime(BusinessTime.ToBusiness(last).DateTime) : today.AddDays(-FirstRunDays)).AddDays(-OverlapDays);
        var listed = await bank.ListChangedAsync(allowed.Value, from, today, ct);
        if (!listed.Ok)
        {
            await FailedAsync(integrationId, listed.Failure!.Message, ct);
            return null;
        }

        var counts = new Counts();
        var account = integration.BankAccountId;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var remote in listed.Value!)
        {
            seen.Add(remote.RequestId);
            counts.Consulted++;
            var slipId = await db.BillingSlips.AsNoTracking()
                .Where(s => s.BankAccountId == account && (s.ProviderRequestId == remote.RequestId || (remote.OurReference != null && s.OurReference == remote.OurReference)))
                .OrderByDescending(s => s.RequestedAt).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
            if (slipId is null)
            {
                // Cobrança fora do sistema (CA 10): nada é baixado; a entrada aparece para a conciliação (BNK-03b).
                counts.Unknown += remote.Situation == BankSlipSituation.Paid ? 1 : 0;
                continue;
            }

            await ApplyAsync(slipId.Value, remote, counts, ct);
        }

        // Pedidos parados há mais de 1 h (a emissão assíncrona pode ter sido paga antes de o job confirmar).
        var stale = clock.GetUtcNow() - RequestedAfter;
        var requested = await db.BillingSlips.AsNoTracking()
            .Where(s => s.BankAccountId == account && s.Status == BillingSlipStatus.Requested && s.ProviderRequestId != null && s.RequestedAt < stale)
            .Select(s => new { s.Id, RequestId = s.ProviderRequestId! }).ToListAsync(ct);
        foreach (var item in requested.Where(r => !seen.Contains(r.RequestId)))
        {
            counts.Consulted++;
            var remote = await bank.GetAsync(allowed.Value, item.RequestId, ct);
            if (!remote.Ok)
            {
                counts.Failed++;
                continue;
            }

            if (remote.Value!.Situation is BankSlipSituation.Paid or BankSlipSituation.Cancelled or BankSlipSituation.Expired)
            {
                await ApplyAsync(item.Id, remote.Value, counts, ct);
            }
        }

        var report = new ReconcileReport(from, today, counts.Consulted, counts.Settled, counts.Divergent, counts.Unknown, counts.Cancelled, counts.Expired, counts.Failed);
        db.ChangeTracker.Clear();
        var tracked = await db.BankIntegrations.FirstAsync(i => i.Id == integrationId, ct);
        tracked.RecordReconcile(clock.GetUtcNow(), JsonSerializer.Serialize(report, InterWebhookService.Json));
        await db.SaveChangesAsync(ct);
        LogReport(logger, integrationId, report.Consulted, report.Settled, report.Divergent, report.Failed);
        return report;
    }

    private async Task ApplyAsync(Guid slipId, BankSlip remote, Counts counts, CancellationToken ct)
    {
        try
        {
            var result = await payments.ApplyAsync(slipId, remote, BankPaymentOrigin.Reconcile, ct);
            switch (result.Outcome)
            {
                case BankPaymentOutcome.Settled:
                    counts.Settled++;
                    break;
                case BankPaymentOutcome.PendingReview:
                    counts.Divergent++;
                    break;
                case BankPaymentOutcome.Cancelled:
                    counts.Cancelled++;
                    break;
                case BankPaymentOutcome.Expired:
                    counts.Expired++;
                    break;
            }
        }
        catch (DbUpdateException ex)
        {
            // O aviso gravou o mesmo boleto no meio (xmin): a próxima execução, ou o próprio aviso, resolve.
            db.ChangeTracker.Clear();
            counts.Failed++;
            LogApplyFailed(logger, ex, slipId);
        }
    }

    private async Task FailedAsync(Guid integrationId, string message, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var integration = await db.BankIntegrations.FirstAsync(i => i.Id == integrationId, ct);
        var failures = integration.RecordReconcileFailure(message);
        await db.SaveChangesAsync(ct);
        LogFailed(logger, integrationId, failures);
        if (failures < FailuresToAlert)
        {
            return;
        }

        var link = BankIntegrationService.Link(integration.BankAccountId);
        await alerts.RaiseAsync(new Alert(AlertKinds.BankIntegration, tenant.Tenant?.Key, $"Conferência dos boletos do Inter falhou {failures} vezes seguidas", message, link), ct);
        var recipients = (await users.OwnerIdsAsync(ct)).Concat(await users.UsersWithPermissionAsync("finance.edit", ct)).Distinct();
        var day = BusinessTime.Today(clock).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        foreach (var user in recipients)
        {
            await notifications.NotifyOnceAsync(user, FailureNotification, "A conferência dos boletos com o Inter não está funcionando. Confira a integração: " + message, link,
                $"inter-reconcile:{integrationId:N}:{day}", ct);
        }
    }

    private sealed class Counts
    {
        public int Consulted { get; set; }

        public int Settled { get; set; }

        public int Divergent { get; set; }

        public int Unknown { get; set; }

        public int Cancelled { get; set; }

        public int Expired { get; set; }

        public int Failed { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Conferência do Inter {IntegrationId}: {Consulted} consultados, {Settled} baixados, {Divergent} divergentes, {Failed} com falha.")]
    private static partial void LogReport(ILogger logger, Guid integrationId, int consulted, int settled, int divergent, int failed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Conferência do Inter {IntegrationId} falhou ({Failures} seguidas).")]
    private static partial void LogFailed(ILogger logger, Guid integrationId, int failures);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Conferência do Inter: boleto {SlipId} não foi aplicado nesta execução.")]
    private static partial void LogApplyFailed(ILogger logger, Exception exception, Guid slipId);
}

public sealed record InterBillingReconcileArgs;

/// <summary>Execução por tenant: só com o módulo <c>finance</c> liberado (conferido de novo aqui); sem integração ativa, não faz nada.</summary>
public sealed class InterBillingReconcileJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<InterBillingReconcileJob> logger)
    : TenantJob<InterBillingReconcileArgs>(directory, scopes, logger)
{
    public const string Module = "finance";

    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, InterBillingReconcileArgs args, CancellationToken cancellationToken)
    {
        if (services.GetRequiredService<TenantContextHolder>().Resolved?.IsReleased(Module) != true)
        {
            return;
        }

        await services.GetRequiredService<InterBillingReconciler>().RunAsync(cancellationToken);
    }
}

/// <summary>Recorrente global <c>inter-billing-reconcile</c>: 07:00 e 13:00 de São Paulo, um job por tenant com o <c>finance</c> liberado.</summary>
public sealed class InterBillingReconcileRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs, ITenantResolver resolver)
    : GlobalRecurringJob<InterBillingReconcileJob, InterBillingReconcileArgs>(directory, jobs)
{
    public const string Id = "inter-billing-reconcile";
    public const string Cron = "0 7,13 * * *";

    protected override string Queue => JobQueues.Default;

    protected override InterBillingReconcileArgs ArgsFor(TenantInfo tenant) => new();

    protected override async Task<bool> ShouldEnqueueAsync(TenantInfo tenant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        return (await resolver.FindByIdAsync(tenant.Id, cancellationToken))?.IsReleased(InterBillingReconcileJob.Module) == true;
    }
}
