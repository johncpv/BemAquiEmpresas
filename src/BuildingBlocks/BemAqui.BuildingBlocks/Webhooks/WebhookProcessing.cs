using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.SharedKernel;
using Hangfire;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.BuildingBlocks.Webhooks;

/// <summary>
/// Mensagem interna do outbox (FND-16 CA 9): o evento <paramref name="EventId"/> do <paramref name="Provider"/> foi gravado e
/// espera processamento. Leva só ids, nunca o corpo.
/// </summary>
public sealed record WebhookReceived(Guid EventId, string Provider) : IIntegrationEvent;

/// <summary>O que o handler vê do evento: tipo, ids e o corpo cru, que nunca é fonte de verdade (ADR 0031 §2).</summary>
public sealed record WebhookEventContext(Guid EventId, string Provider, string EventType, string ExternalId, Guid ScopeId, DateTimeOffset ReceivedAt, ReadOnlyMemory<byte> Payload);

/// <summary>Resultado do handler: efeito gravado (<c>Processed</c>) ou nada a fazer, com o motivo (<c>Ignored</c>).</summary>
public sealed record WebhookHandling(WebhookEventStatus Status, string? Reason)
{
    public static WebhookHandling Processed() => new(WebhookEventStatus.Processed, null);

    public static WebhookHandling Ignored(string reason) => new(WebhookEventStatus.Ignored, reason);
}

/// <summary>
/// Handler de um provedor, no módulo dono (FIS-01, BNK-02). Roda na transação que marca o evento: grava o efeito no mesmo
/// <c>SaveChanges</c>. Valida tipo, versão e ids pelo esquema e reconsulta a fonte antes de qualquer efeito; erro que vai para
/// o <c>last_error</c> sai por <see cref="WebhookHandlerException"/>, com mensagem sem dado pessoal.
/// </summary>
public interface IWebhookHandler
{
    Task<WebhookHandling> HandleAsync(WebhookEventContext context, CancellationToken cancellationToken);
}

/// <summary>Falha com mensagem segura para o <c>last_error</c> e para a tela (sem CPF, nome, valor do corpo).</summary>
public sealed class WebhookHandlerException(string safeMessage, Exception? inner = null) : Exception(safeMessage, inner);

/// <summary>
/// Consumidor da mensagem do outbox, com inbox (no máximo uma vez por mensagem): enfileira o <see cref="ProcessWebhookEventJob"/>
/// na fila do provedor, só com o <c>tenantId</c> e o id do evento.
/// </summary>
public sealed class WebhookReceivedConsumer(ITenantContext tenant, WebhookProviderRegistry providers, IBackgroundJobClient jobs) : IIntegrationEventConsumer<WebhookReceived>
{
    public Task HandleAsync(WebhookReceived integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var tenantId = tenant.Tenant?.Id ?? throw new InvalidOperationException("Consumidor de webhook fora do escopo de um tenant.");
        var queue = providers.TryGet(integrationEvent.Provider, out var provider) ? provider.Queue : JobQueues.Default;
        var args = new WebhookEventJobArgs(integrationEvent.EventId);
        jobs.Create<ProcessWebhookEventJob>(j => j.ExecuteAsync(tenantId, args, CancellationToken.None), new EnqueuedState(queue));
        return Task.CompletedTask;
    }
}

public sealed record WebhookEventJobArgs(Guid EventId);

/// <summary>
/// Processa um evento (CA 11 e 12): confere o módulo dono liberado, chama o handler numa transação própria com a linha do evento
/// travada (<c>FOR UPDATE SKIP LOCKED</c>; a segunda execução simultânea não faz nada) e marca <c>Processed</c> ou
/// <c>Ignored</c>. Exceção marca <c>Failed</c> e segue o retry do Hangfire; na 10ª tentativa, <c>DeadLettered</c>, com aviso a quem
/// edita o módulo dono e alerta aos super admins.
/// </summary>
[AutomaticRetry(Attempts = MaxAttempts, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
public sealed partial class ProcessWebhookEventJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<ProcessWebhookEventJob> logger)
    : TenantJob<WebhookEventJobArgs>(directory, scopes, logger)
{
    public const int MaxAttempts = 10;
    public const string ModuleNotReleased = "Módulo não liberado";
    public const string UnknownProvider = "Provedor não registrado";
    public const string DeadLetterNotification = "webhook_dead_lettered";

    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, WebhookEventJobArgs args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        var db = services.GetRequiredService<BemAquiDbContext>();
        var clock = services.GetRequiredService<TimeProvider>();
        var started = clock.GetTimestamp();
        var outcome = await TryProcessAsync(services, db, clock, args.EventId, cancellationToken);
        if (outcome.Error is null)
        {
            if (outcome.Status is { } done)
            {
                var ms1 = clock.GetElapsedTime(started).TotalMilliseconds;
                LogProcessed(logger, outcome.Provider ?? "-", tenant.Id, args.EventId, outcome.EventType ?? "-", done, ms1);
            }

            return;
        }

        db.ChangeTracker.Clear();
        var failed = await RecordFailureAsync(services, db, clock, tenant, args.EventId, outcome.Error, cancellationToken);
        var ms2 = clock.GetElapsedTime(started).TotalMilliseconds;
        LogFailed(logger, outcome.Provider ?? "-", tenant.Id, args.EventId, outcome.EventType ?? "-", failed, ms2);
        if (failed == WebhookEventStatus.Failed)
        {
            // Segue o backoff do Hangfire; a mensagem leva só o tipo do erro, sem o corpo.
            throw new WebhookProcessingFailedException(args.EventId);
        }
    }

    private sealed record Outcome(WebhookEventStatus? Status, string? Provider, string? EventType, string? Error);

    private static async Task<Outcome> TryProcessAsync(IServiceProvider services, BemAquiDbContext db, TimeProvider clock, Guid eventId, CancellationToken cancellationToken)
    {
        string? provider = null;
        string? eventType = null;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var evt = await db.WebhookEvents
                .FromSql($"SELECT e.*, e.xmin FROM shared.webhook_events e WHERE e.id = {eventId} FOR UPDATE SKIP LOCKED")
                .FirstOrDefaultAsync(cancellationToken);
            if (evt is null || evt.IsFinal)
            {
                // Outro job está com a linha, ou já terminou: nada a fazer (execução dupla, CA 12).
                return new Outcome(null, null, null, null);
            }

            provider = evt.Provider;
            eventType = evt.EventType;
            var registry = services.GetRequiredService<WebhookProviderRegistry>();
            var tenant = services.GetRequiredService<TenantContextHolder>().Resolved;
            WebhookHandling handling;
            if (!registry.TryGet(evt.Provider, out var definition))
            {
                handling = WebhookHandling.Ignored(UnknownProvider);
            }
            else if (tenant?.IsReleased(definition.OwnerModule) != true)
            {
                handling = WebhookHandling.Ignored(ModuleNotReleased);
            }
            else
            {
                var handler = (IWebhookHandler)services.GetRequiredService(definition.HandlerType);
                handling = await handler.HandleAsync(
                    new WebhookEventContext(evt.Id, evt.Provider, evt.EventType, evt.ExternalId, evt.ScopeId, evt.ReceivedAt, evt.Payload ?? ReadOnlyMemory<byte>.Empty),
                    cancellationToken);
            }

            evt.Attempts++;
            evt.Status = handling.Status == WebhookEventStatus.Ignored ? WebhookEventStatus.Ignored : WebhookEventStatus.Processed;
            evt.LastError = Truncate(handling.Reason);
            evt.ProcessedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new Outcome(evt.Status, provider, eventType, null);
        }
        catch (DbUpdateConcurrencyException)
        {
            // "Reprocessar" ou outra execução mudou a linha: quem chegou primeiro vale.
            await transaction.RollbackAsync(CancellationToken.None);
            return new Outcome(null, provider, eventType, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return new Outcome(null, provider, eventType, SafeError(ex));
        }
    }

    private static async Task<WebhookEventStatus> RecordFailureAsync(IServiceProvider services, BemAquiDbContext db, TimeProvider clock, TenantInfo tenant, Guid eventId, string error, CancellationToken cancellationToken)
    {
        var evt = await db.WebhookEvents.FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);
        if (evt is null || evt.IsFinal)
        {
            return evt?.Status ?? WebhookEventStatus.Ignored;
        }

        evt.Attempts++;
        evt.LastError = Truncate(error);
        evt.Status = evt.Attempts >= MaxAttempts ? WebhookEventStatus.DeadLettered : WebhookEventStatus.Failed;
        await db.SaveChangesAsync(cancellationToken);
        if (evt.Status == WebhookEventStatus.DeadLettered)
        {
            await DeadLetterAsync(services, tenant, evt, cancellationToken);
        }

        return evt.Status;
    }

    /// <summary>Aviso a quem edita o módulo dono, com "Reprocessar" (ADR 0019 §5), e alerta no painel da plataforma.</summary>
    private static async Task DeadLetterAsync(IServiceProvider services, TenantInfo tenant, WebhookEvent evt, CancellationToken cancellationToken)
    {
        var registry = services.GetRequiredService<WebhookProviderRegistry>();
        var name = registry.TryGet(evt.Provider, out var provider) ? provider.Name : evt.Provider;
        await services.GetRequiredService<IAlertSink>().RaiseAsync(
            new Alert(WebhookAlerts.DeadLetter, tenant.Key, "Aviso do " + name + " parou depois de " + MaxAttempts + " tentativas", evt.LastError), cancellationToken);
        if (provider is null)
        {
            return;
        }

        var users = await services.GetRequiredService<IUserDirectory>().UsersWithPermissionAsync(provider.EditPermission, cancellationToken);
        var notifications = services.GetRequiredService<INotificationService>();
        foreach (var user in users)
        {
            await notifications.NotifyOnceAsync(user, DeadLetterNotification, "Um aviso do " + name + " parou depois de 10 tentativas. Corrija a causa e reprocesse.",
                WebhookLinks.Stalled, "webhook-dead:" + evt.Id.ToString("N") + ":" + evt.Attempts, cancellationToken);
        }
    }

    /// <summary>Só o tipo do erro, ou a mensagem segura do handler: nada do corpo nem dado pessoal (CA 11 e 13).</summary>
    internal static string SafeError(Exception exception) => exception switch
    {
        WebhookHandlerException safe => safe.Message,
        DbUpdateException => "Falha ao gravar o efeito no banco.",
        _ => "Falha no processamento (" + exception.GetType().Name + ").",
    };

    private static string? Truncate(string? value) => value is { Length: > WebhookEvent.ErrorMaxLength } ? value[..WebhookEvent.ErrorMaxLength] : value;

    [LoggerMessage(Level = LogLevel.Information, Message = "Webhook {Provider} do tenant {TenantId}: evento {EventId} ({EventType}) {Status} em {Elapsed:0.0} ms")]
    private static partial void LogProcessed(ILogger logger, string provider, Guid tenantId, Guid eventId, string eventType, WebhookEventStatus status, double elapsed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook {Provider} do tenant {TenantId}: evento {EventId} ({EventType}) {Status} em {Elapsed:0.0} ms")]
    private static partial void LogFailed(ILogger logger, string provider, Guid tenantId, Guid eventId, string eventType, WebhookEventStatus status, double elapsed);
}

/// <summary>Faz o Hangfire tentar de novo; a mensagem não leva nada do corpo.</summary>
public sealed class WebhookProcessingFailedException(Guid eventId) : Exception("Processamento do evento " + eventId + " falhou; nova tentativa pelo Hangfire.");

/// <summary>Alertas da FND-12 para os webhooks (CA 3, 7 e 11).</summary>
public static class WebhookAlerts
{
    public const string Rejections = "webhook_rejections";
    public const string Stalled = "webhook_stalled";
    public const string DeadLetter = "webhook_dead_letter";
    public const string PreviousSecretUsed = "webhook_previous_secret";
}

/// <summary>Telas que os avisos abrem (todo alerta leva à ação, R-PRP-5).</summary>
public static class WebhookLinks
{
    public const string Events = "/app/configuracoes/integracoes/eventos";
    public const string Stalled = Events + "?situacao=DeadLettered";
}

/// <summary>Auditoria do reprocessamento e da leitura do corpo (CA 11 e 15).</summary>
internal static class WebhookAudit
{
    public const string Entity = "WebhookEvent";

    public static AuditEntry Reprocessed(WebhookEvent evt, Guid? userId, DateTimeOffset at) => new()
    {
        Entity = Entity,
        EntityId = evt.Id.ToString(),
        Action = AuditActions.Updated,
        UserId = userId,
        At = at,
        Changes = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["status"] = new { old = evt.Status.ToString(), @new = nameof(WebhookEventStatus.Received) },
            ["attempts"] = new { old = evt.Attempts, @new = 0 },
        }),
    };
}
