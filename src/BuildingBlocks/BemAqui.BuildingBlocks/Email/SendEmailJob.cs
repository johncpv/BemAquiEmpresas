using System.Text.Json;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using Hangfire;
using Hangfire.States;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.BuildingBlocks.Email;

public sealed record SendEmailArgs(Guid EmailLogId);

/// <summary>EmailQueued (já gravado) vira job na fila <c>email</c> do Hangfire, com o tenantId (FND-08 CA 1).</summary>
public sealed class EmailQueuedConsumer(IBackgroundJobClient jobs, ITenantContext tenant) : IIntegrationEventConsumer<EmailQueued>
{
    public Task HandleAsync(EmailQueued integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var tenantId = tenant.Tenant?.Id ?? throw new InvalidOperationException("EmailQueued fora de escopo de tenant.");
        var args = new SendEmailArgs(integrationEvent.EmailLogId);
        jobs.Create<SendEmailJob>(j => j.ExecuteAsync(tenantId, args, CancellationToken.None), new EnqueuedState(JobQueues.Email));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Envia um e-mail da fila: 3 tentativas com intervalo crescente (30 s, 2 min); depois da 3ª falha marca
/// Failed com o erro e registra para o alerta (FND-08 CA 2, 3; alerta na FND-12). Tenant suspenso: Skipped (CA 9).
/// </summary>
[AutomaticRetry(Attempts = MaxAttempts - 1, DelaysInSeconds = [30, 120], OnAttemptsExceeded = AttemptsExceededAction.Fail)]
public sealed partial class SendEmailJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<SendEmailJob> logger)
    : TenantJob<SendEmailArgs>(directory, scopes, logger)
{
    public const int MaxAttempts = 3;

    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, SendEmailArgs args, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<BemAquiDbContext>();
        var clock = services.GetRequiredService<TimeProvider>();
        var log = await db.EmailLogs.FindAsync([args.EmailLogId], cancellationToken);
        if (log is null || log.Status is EmailStatus.Sent or EmailStatus.Failed)
        {
            return; // já resolvido: reentrega do Hangfire não envia de novo
        }

        var (files, wait, refused) = await AttachmentsAsync(services, db, log, cancellationToken);
        if (wait)
        {
            log.ScanWaits++;
            await db.SaveChangesAsync(cancellationToken);
            ScheduleScanWait(services, tenant, args);
            return;
        }

        if (refused is not null)
        {
            log.Status = EmailStatus.Failed;
            log.Error = refused;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        log.Attempts++;
        try
        {
            var model = ToModel(log.Model);
            model["tenant_name"] ??= services.GetRequiredService<TenantContextHolder>().Resolved?.TradeName ?? services.GetRequiredService<TenantContextHolder>().Resolved?.LegalName;
            var content = services.GetRequiredService<EmailTemplateRenderer>().Render(log.Template, model);
            log.Subject = content.Subject;
            var outgoing = new OutgoingEmail(log.To, content)
            {
                Cc = log.Cc?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [], ReplyTo = log.ReplyTo, FromName = log.FromName, Attachments = files,
            };
            await services.GetRequiredService<IEmailTransport>().SendAsync(outgoing, cancellationToken);
            log.Status = EmailStatus.Sent;
            log.SentAt = clock.GetUtcNow();
            log.Error = null;
            await db.SaveChangesAsync(cancellationToken);
            services.GetService<IntegrationHealthTracker>()?.RecordSuccess("smtp");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Error = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
            if (services.GetService<IntegrationHealthTracker>() is { } integrations)
            {
                await integrations.RecordFailureAsync("smtp", tenant.Key, log.Error, cancellationToken);
            }

            if (log.Attempts >= MaxAttempts)
            {
                log.Status = EmailStatus.Failed;
                await db.SaveChangesAsync(cancellationToken);
                LogFailed(logger, ex, log.Id, tenant.Key, log.Attempts);
                await services.GetRequiredService<IAlertSink>().RaiseAsync(new Alert(AlertKinds.EmailFailed, tenant.Key, "E-mail " + log.Template + " não foi enviado após " + log.Attempts + " tentativas", log.Error), cancellationToken);
                return;
            }

            await db.SaveChangesAsync(cancellationToken);
            throw; // o Hangfire tenta de novo com o intervalo configurado
        }
    }

    protected override async Task OnSkippedAsync(IServiceProvider services, TenantInfo tenant, SendEmailArgs args, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<BemAquiDbContext>();
        var log = await db.EmailLogs.FindAsync([args.EmailLogId], cancellationToken);
        if (log is { Status: EmailStatus.Queued })
        {
            log.Status = EmailStatus.Skipped;
            log.Error = "Tenant não ativo (" + tenant.Status + "): e-mail não enviado.";
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static Dictionary<string, object?> ToModel(string json)
    {
        var model = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in JsonDocument.Parse(json).RootElement.EnumerateObject())
        {
            model[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => property.Value.GetRawText(),
            };
        }

        return model;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "E-mail {EmailLogId} do tenant {Tenant} falhou após {Attempts} tentativas (alerta).")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid emailLogId, string tenant, int attempts);
}
