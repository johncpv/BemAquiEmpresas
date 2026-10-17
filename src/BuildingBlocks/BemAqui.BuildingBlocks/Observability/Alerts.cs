namespace BemAqui.BuildingBlocks.Observability;

/// <summary>Tipos de alerta operacional (FND-12 CA 6 a 8).</summary>
public static class AlertKinds
{
    public const string JobFailed = "job_failed";
    public const string OutboxDeadLetter = "outbox_dead_letter";
    public const string EmailFailed = "email_failed";
    public const string MigrationFailed = "migration_failed";
    public const string IntegrationDown = "integration_down";
}

public sealed record Alert(string Kind, string? Tenant, string Summary, string? Details = null, string? Link = null);

/// <summary>
/// Alerta aos super admins (e-mail no MVP), com no máximo um alerta por tipo e tenant a cada 30 min.
/// Nunca lança: falhar ao alertar não pode derrubar o que estava falhando.
/// </summary>
public interface IAlertSink
{
    Task RaiseAsync(Alert alert, CancellationToken cancellationToken = default);
}

/// <summary>Sem destino configurado: alerta só no log (a implementação real vive no módulo Platform).</summary>
public sealed class LogOnlyAlertSink(Microsoft.Extensions.Logging.ILogger<LogOnlyAlertSink> logger) : IAlertSink
{
    public Task RaiseAsync(Alert alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);
        AlertLog.Raised(logger, alert.Kind, alert.Tenant, alert.Summary);
        return Task.CompletedTask;
    }
}

public static partial class AlertLog
{
    [Microsoft.Extensions.Logging.LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "ALERTA {Kind} (tenant {Tenant}): {Summary}")]
    public static partial void Raised(Microsoft.Extensions.Logging.ILogger logger, string kind, string? tenant, string summary);
}
