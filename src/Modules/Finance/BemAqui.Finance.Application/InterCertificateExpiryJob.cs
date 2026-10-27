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

public sealed record InterCertificateExpiryArgs;

/// <summary>
/// Aviso do vencimento do certificado do Inter (BNK-01a CA 15): aos membros do Proprietário, em 30, 15, 7 e 1 dia, uma vez por
/// limiar (<c>inter-cert:{contaId}:{limiar}</c>), com o link direto para "Trocar credenciais". Vencido, sai o alerta
/// <c>bank_integration</c> (FND-12) e a emissão responde 409 <c>bank_integration_expired</c>.
/// </summary>
public sealed class InterCertificateExpiryNotifier(
    IFinanceDb db, INotificationService notifications, IUserDirectory users, IAlertSink alerts, ITenantContext tenant, TimeProvider clock)
{
    public static readonly int[] Thresholds = [30, 15, 7, 1];
    public const string NotificationType = "finance.bank_certificate_expiry";

    /// <summary>Limiar do aviso para os dias que faltam: o menor que ainda cobre (29 dias → 30; 6 → 7); nulo acima de 30.</summary>
    public static int? ThresholdFor(int daysLeft) => Thresholds.Where(t => daysLeft <= t).Order().Cast<int?>().FirstOrDefault();

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var today = BusinessTime.Today(clock);
        var integrations = await db.BankIntegrations.AsNoTracking()
            .Where(i => i.Status == BankIntegrationStatus.Active && i.CertificateNotAfter != null)
            .Join(db.BankAccounts.AsNoTracking(), i => i.BankAccountId, a => a.Id, (i, a) => new { i.BankAccountId, i.CertificateNotAfter, a.Name })
            .ToListAsync(cancellationToken);
        var owners = integrations.Count == 0 ? [] : await users.OwnerIdsAsync(cancellationToken);
        var sent = 0;
        foreach (var item in integrations)
        {
            var notAfter = item.CertificateNotAfter!.Value;
            var link = BankIntegrationService.Link(item.BankAccountId) + "?acao=trocar";
            if (notAfter <= now)
            {
                foreach (var owner in owners)
                {
                    await notifications.NotifyOnceAsync(owner, NotificationType, $"O certificado do Inter da conta {item.Name} venceu. A emissão de boletos está parada até trocar as credenciais.", link,
                        $"inter-cert:{item.BankAccountId}:vencido", cancellationToken);
                }

                await alerts.RaiseAsync(new Alert(AlertKinds.BankIntegration, tenant.Tenant?.Key, "Certificado do Inter vencido", "Conta " + item.BankAccountId, link), cancellationToken);
                sent++;
                continue;
            }

            var daysLeft = BusinessTime.ToBusiness(notAfter).Date.Subtract(today.ToDateTime(TimeOnly.MinValue)).Days;
            if (ThresholdFor(daysLeft) is not { } threshold)
            {
                continue;
            }

            var days = Math.Max(daysLeft, 0);
            var text = days == 1 ? "vence em 1 dia" : days == 0 ? "vence hoje" : $"vence em {days} dias";
            foreach (var owner in owners)
            {
                await notifications.NotifyOnceAsync(owner, NotificationType, $"O certificado do Inter da conta {item.Name} {text}.", link, $"inter-cert:{item.BankAccountId}:{threshold}", cancellationToken);
            }

            sent++;
        }

        return sent;
    }
}

/// <summary>Execução por tenant (argumentos só de ids, ADR 0006).</summary>
public sealed class InterCertificateExpiryJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<InterCertificateExpiryJob> logger)
    : TenantJob<InterCertificateExpiryArgs>(directory, scopes, logger)
{
    protected override Task RunAsync(IServiceProvider services, TenantInfo tenant, InterCertificateExpiryArgs args, CancellationToken cancellationToken) =>
        services.GetRequiredService<InterCertificateExpiryNotifier>().RunAsync(cancellationToken);
}

/// <summary>Recorrente global <c>inter-certificate-expiry</c>: diário às 08:00 de São Paulo, um job por tenant ativo.</summary>
public sealed class InterCertificateExpiryRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<InterCertificateExpiryJob, InterCertificateExpiryArgs>(directory, jobs)
{
    public const string Id = "inter-certificate-expiry";
    public const string Cron = "0 8 * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override InterCertificateExpiryArgs ArgsFor(TenantInfo tenant) => new();
}
