using System.Globalization;
using BemAqui.BuildingBlocks.Agenda;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Fiscal.Domain;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Fiscal.Application.Certificates;

/// <summary>O que a conferência do vencimento fez num tenant.</summary>
public sealed record FiscalCertificateExpiryResult(int? DaysLeft, int? Threshold, int Notified, bool Expired);

/// <summary>
/// Aviso de vencimento do A1 (FIS-02a CA 14): em 30, 15 e 7 dias, os membros do Proprietário recebem "O certificado digital da empresa
/// vence em {n} dias. Sem ele, nenhuma nota é emitida." com o link da tela do certificado, uma vez por limiar e certificado
/// (<c>dedup_key = fiscal-cert:{limiar}:{thumbprint}</c>, a execução dupla não repete). Vencido, sai o alerta <c>fiscal_integration</c>.
/// </summary>
public sealed partial class FiscalCertificateExpiryChecker(
    IFiscalDb db,
    IUserDirectory users,
    INotificationService notifications,
    FiscalAlerts alerts,
    TenantContextHolder tenant,
    TimeProvider clock,
    ILogger<FiscalCertificateExpiryChecker> logger)
{
    public const string NotificationType = "fiscal.certificate_expiring";

    public async Task<FiscalCertificateExpiryResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (tenant.Resolved?.IsReleased(FiscalModuleInfo.Code) != true)
        {
            return new FiscalCertificateExpiryResult(null, null, 0, false);
        }

        var settings = await db.FiscalSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (settings?.CertificateNotAfter is null || settings.CertificateThumbprint is null)
        {
            return new FiscalCertificateExpiryResult(null, null, 0, false);
        }

        var days = settings.CertificateDaysLeft(BusinessTime.Today(clock))!.Value;
        if (settings.CertificateExpired(clock.GetUtcNow()))
        {
            await alerts.RaiseAsync(FiscalAlertReason.CertificateExpired, null, cancellationToken);
            return new FiscalCertificateExpiryResult(days, null, 0, true);
        }

        if (FiscalCertificateRules.ThresholdFor(days) is not { } threshold)
        {
            return new FiscalCertificateExpiryResult(days, null, 0, false);
        }

        var title = Title(days);
        var dedup = "fiscal-cert:" + threshold.ToString(CultureInfo.InvariantCulture) + ":" + settings.CertificateThumbprint;
        var notified = 0;
        foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
        {
            if ((await notifications.NotifyOnceAsync(owner, NotificationType, title, FiscalLinks.Certificate, dedup, cancellationToken)).IsSuccess)
            {
                notified++;
            }
        }

        LogChecked(logger, days, threshold);
        return new FiscalCertificateExpiryResult(days, threshold, notified, false);
    }

    public static string Title(int days) => days switch
    {
        0 => "O certificado digital da empresa vence hoje. Sem ele, nenhuma nota é emitida.",
        1 => "O certificado digital da empresa vence em 1 dia. Sem ele, nenhuma nota é emitida.",
        _ => $"O certificado digital da empresa vence em {days.ToString(CultureInfo.InvariantCulture)} dias. Sem ele, nenhuma nota é emitida.",
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Certificado digital: {Days} dias para vencer (limiar {Threshold}).")]
    private static partial void LogChecked(ILogger logger, int days, int threshold);
}

public sealed record FiscalCertificateExpiryArgs;

/// <summary>Execução por tenant (só com o <c>tenantId</c>).</summary>
public sealed class FiscalCertificateExpiryJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<FiscalCertificateExpiryJob> logger)
    : TenantJob<FiscalCertificateExpiryArgs>(directory, scopes, logger)
{
    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, FiscalCertificateExpiryArgs args, CancellationToken cancellationToken) =>
        await services.GetRequiredService<FiscalCertificateExpiryChecker>().CheckAsync(cancellationToken);
}

/// <summary>Recorrente global <c>fiscal-certificate-expiry</c> (diário, 08:00 de São Paulo): um job por tenant com o módulo <c>fiscal</c> liberado.</summary>
public sealed class FiscalCertificateExpiryRecurringJob(ITenantDirectory directory, ITenantResolver resolver, IBackgroundJobClient jobs)
    : GlobalRecurringJob<FiscalCertificateExpiryJob, FiscalCertificateExpiryArgs>(directory, jobs)
{
    public const string Id = "fiscal-certificate-expiry";
    public const string Cron = "0 8 * * *";

    protected override string Queue => JobQueues.Fiscal;

    protected override FiscalCertificateExpiryArgs ArgsFor(TenantInfo tenant) => new();

    protected override async Task<bool> ShouldEnqueueAsync(TenantInfo tenant, CancellationToken cancellationToken) =>
        (await resolver.FindByIdAsync(tenant.Id, cancellationToken))?.IsReleased(FiscalModuleInfo.Code) == true;
}

/// <summary>
/// Pauta do Proprietário (FIS-02a CA 14, DSH-04): "Certificado digital vencendo" quando faltam 30 dias ou menos, ou já venceu. A
/// permissão é <c>fiscal.approve</c>, reservada ao Proprietário (ADR 0008).
/// </summary>
public sealed class FiscalAgendaSource(IFiscalDb db) : IAgendaSource
{
    public const string CertificateExpiring = "fiscal.certificate_expiring";

    private static readonly AgendaCategory[] Declared =
    [
        new(CertificateExpiring, FiscalModuleInfo.Code, "fiscal.approve", AgendaTier.DueSoon, "o certificado digital"),
    ];

    public string Module => FiscalModuleInfo.Code;

    public IReadOnlyList<AgendaCategory> Categories => Declared;

    public async Task<IReadOnlyList<AgendaItem>> ItemsAsync(AgendaRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Wants(CertificateExpiring))
        {
            return [];
        }

        var settings = await db.FiscalSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (settings?.CertificateDaysLeft(request.Today) is not { } days || days > FiscalCertificateRules.WarnDays)
        {
            return [];
        }

        var due = DateOnly.FromDateTime(BusinessTime.ToBusiness(settings.CertificateNotAfter!.Value).DateTime);
        var why = days < 0 ? "Venceu: nenhuma nota é emitida até enviar o novo." : FiscalCertificateExpiryChecker.Title(days);
        return
        [
            new AgendaItem(CertificateExpiring, days < 0 ? "Certificado digital vencido" : "Certificado digital vencendo", why, FiscalLinks.Certificate, due, null,
                days < 0 ? AgendaTier.Overdue : AgendaTier.DueSoon, 1, "Enviar certificado",
                [new AgendaEntry(settings.CertificateSubject ?? "Certificado A1", "Válido até " + due.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), FiscalLinks.Certificate, due, null)]),
        ];
    }
}
