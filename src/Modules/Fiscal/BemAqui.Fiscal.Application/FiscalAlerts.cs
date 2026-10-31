using System.Globalization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;

namespace BemAqui.Fiscal.Application;

/// <summary>Telas que os avisos fiscais abrem (todo alerta leva à ação, R-PRP-5).</summary>
public static class FiscalLinks
{
    public const string Integration = "/app/configuracoes/integracoes/emissor-fiscal";

    /// <summary>Configurações → Fiscal → Certificado digital (FIS-02a).</summary>
    public const string Certificate = "/app/configuracoes/fiscal/certificado";

    /// <summary>A nota (a tela nasce na FIS-03a; até lá o link leva à integração, com as pendentes).</summary>
    public static string Invoice(Guid invoiceId) => Integration + "?nota=" + invoiceId.ToString("D");
}

/// <summary>Por que a integração fiscal alertou (CA 12).</summary>
public enum FiscalAlertReason
{
    KeyRejected,
    Unreachable,
    SefazDown,
    ContingencyTooLong,
    ForceConsultMissing,

    /// <summary>Certificado A1 vencido (FIS-02a CA 14): nenhuma nota sai até o novo.</summary>
    CertificateExpired,
}

/// <summary>
/// Alerta <c>fiscal_integration</c> (FIS-01 CA 12, FND-12): aos super admins pelo <see cref="IAlertSink"/> (um por tipo e tenant a cada
/// janela) e notificação a quem tem <c>fiscal.edit</c>, uma vez por motivo e janela de 30 min, com o link para a integração ou para a
/// nota. Falhar ao alertar nunca derruba quem chamou. Nada de chave, payload ou XML na mensagem.
/// </summary>
public sealed partial class FiscalAlerts(IAlertSink alerts, IUserDirectory users, INotificationService notifications, ITenantContext tenant, TimeProvider clock, ILogger<FiscalAlerts> logger)
{
    public const string Kind = "fiscal_integration";
    public const string NotificationType = "fiscal.integration";
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(30);

    public async Task RaiseAsync(FiscalAlertReason reason, Guid? invoiceId = null, CancellationToken cancellationToken = default)
    {
        var (summary, title) = Texts(reason);
        var link = reason == FiscalAlertReason.CertificateExpired ? FiscalLinks.Certificate : invoiceId is { } id ? FiscalLinks.Invoice(id) : FiscalLinks.Integration;
        try
        {
            await alerts.RaiseAsync(new Alert(Kind, tenant.Tenant?.Key, summary, reason.ToString(), link), cancellationToken);
            var bucket = (clock.GetUtcNow().ToUnixTimeSeconds() / (long)Window.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            var dedup = "fiscal-integration:" + reason + ":" + (invoiceId?.ToString("N") ?? "-") + ":" + bucket;
            foreach (var user in await users.UsersWithPermissionAsync("fiscal.edit", cancellationToken))
            {
                await notifications.NotifyOnceAsync(user, NotificationType, title, link, dedup, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(logger, ex, reason);
        }
    }

    private static (string Summary, string Title) Texts(FiscalAlertReason reason) => reason switch
    {
        FiscalAlertReason.KeyRejected => ("Emissor Fiscal recusou a chave de API do tenant", "O Emissor Fiscal recusou a chave desta empresa. Confira a integração fiscal."),
        FiscalAlertReason.Unreachable => ("Emissor Fiscal fora de alcance", "O Emissor Fiscal não respondeu. As notas esperam a volta dele."),
        FiscalAlertReason.SefazDown => ("SEFAZ autorizadora fora do ar", "A SEFAZ está fora do ar. As notas esperam a volta dela."),
        FiscalAlertReason.CertificateExpired => ("Certificado digital A1 do tenant vencido", "O certificado digital da empresa venceu. Nenhuma nota é emitida até enviar o novo."),
        FiscalAlertReason.ContingencyTooLong => ("Nota em contingência há mais de 2 horas", "Uma nota está em contingência há mais de 2 horas. Não emita de novo; confira a situação."),
        _ => ("Consulta forçada indisponível no Emissor Fiscal", "Uma nota está demorando e o Emissor Fiscal ainda não tem a consulta forçada. Ela não será emitida de novo."),
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Alerta fiscal {Reason} não enviado.")]
    private static partial void LogFailed(ILogger logger, Exception exception, FiscalAlertReason reason);
}
