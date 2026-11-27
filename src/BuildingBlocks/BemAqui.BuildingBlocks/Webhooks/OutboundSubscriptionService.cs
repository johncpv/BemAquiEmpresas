using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Integration;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.SharedKernel;
using Hangfire;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BemAqui.BuildingBlocks.Webhooks;

/// <summary>Enfileira o envio de uma entrega depois do commit (CA 7): o job leva só o <c>tenantId</c> e o id da entrega.</summary>
public sealed class OutboundDeliveryScheduler(ITenantContext tenant, IBackgroundJobClient jobs, IAfterCommit afterCommit)
{
    public Task EnqueueAfterCommitAsync(IReadOnlyCollection<Guid> deliveryIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deliveryIds);
        if (deliveryIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        var tenantId = tenant.Tenant?.Id ?? throw new InvalidOperationException("Aviso de saída fora do escopo de um tenant.");
        var ids = deliveryIds.ToList();
        return afterCommit.RunAsync(_ =>
        {
            foreach (var id in ids)
            {
                Enqueue(tenantId, id, null);
            }

            return Task.CompletedTask;
        }, cancellationToken);
    }

    public void Enqueue(Guid tenantId, Guid deliveryId, TimeSpan? delay)
    {
        var args = new OutboundDeliveryJobArgs(deliveryId);
        IState state = delay is { } wait && wait > TimeSpan.Zero ? new ScheduledState(wait) : new EnqueuedState(JobQueues.Default);
        jobs.Create<OutboundWebhookDeliveryJob>(j => j.ExecuteAsync(tenantId, args, CancellationToken.None), state);
    }
}

/// <summary>
/// Inscrições de avisos (CA 1 a 4 e 6): criação na troca do código, encerramento e suspensão pela vida da conexão, mudança de URL e tipos, troca do
/// segredo e "Enviar teste". O segredo nunca vai para log, auditoria (<c>[ProtectedSecret]</c>) ou tela: sai só no <c>/oauth/token</c>.
/// </summary>
public sealed class OutboundSubscriptionService(
    BemAquiDbContext db,
    ITenantContext tenant,
    ISecretProtector secrets,
    IOptions<ConectaWebhookOptions> conecta,
    IOptions<OutboundWebhookOptions> outbound,
    IHostEnvironment environment,
    OutboundDeliveryScheduler scheduler,
    IConnectedAppAccess access,
    IUserDirectory users,
    INotificationService notifications,
    TimeProvider clock) : IOutboundSubscriptions
{
    public const string ReplacedReason = "Conexão substituída";
    public const string EndedReason = "Conexão encerrada";
    public const string RotatedNotification = "outbound_webhook_secret_rotated";

    /// <summary>Rede privada só fora de produção (CA 16): <c>Development</c> e os testes.</summary>
    public static bool PrivateAllowed(OutboundWebhookOptions options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        return options.AllowPrivateNetworks && (environment.IsDevelopment() || environment.IsEnvironment("Testing"));
    }

    private bool AllowPrivate => PrivateAllowed(outbound.Value, environment);

    private SecretScope Scope => SecretScope.ForTenant(tenant.Tenant?.Id ?? throw new InvalidOperationException("Inscrição de avisos fora do escopo de um tenant."));

    public async Task<string?> CreateForConnectionAsync(Guid connectionId, string clientId, Guid? replacedConnectionId, IReadOnlyCollection<string> grantedScopes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grantedScopes);
        var options = conecta.Value;
        if (string.IsNullOrWhiteSpace(options.WebhookUrl))
        {
            return null;
        }

        if (OutboundUrlGuard.Validate(options.WebhookUrl, options.AllowedHosts(), AllowPrivate) is { } invalid)
        {
            // Falha da inscrição desfaz a conexão (CA 1): quem chama não grava.
            throw new InvalidOperationException("Integrations:Conecta:WebhookUrl inválida: " + invalid);
        }

        var now = clock.GetUtcNow();
        var events = DefaultEvents(options).Where(t => OutboundWebhookCatalog.Allowed(t, grantedScopes)).Select(t => t.Type).ToList();
        var secret = OutboundWebhookSigner.NewSecret();
        db.OutboundSubscriptions.Add(OutboundSubscription.Create(connectionId, clientId, options.WebhookUrl, events,
            secrets.Protect(Scope, SecretKind.OutboundWebhookSecret, new Secret(secret)), now));

        // CA 2: conexão nova → inscrição nova → a anterior Ended, com as pendentes Skipped.
        if (replacedConnectionId is { } replaced)
        {
            await EndAsync(replaced, ReplacedReason, disconnected: false, cancellationToken);
        }

        return secret;
    }

    private static IEnumerable<OutboundEventType> DefaultEvents(ConectaWebhookOptions options) =>
        options.DefaultEvents.Length == 0
            ? OutboundWebhookCatalog.All.Where(t => t.Type != OutboundWebhookCatalog.Ping)
            : options.DefaultEvents.Select(OutboundWebhookCatalog.Find).OfType<OutboundEventType>().Where(t => t.Type != OutboundWebhookCatalog.Ping);

    public async Task<string?> TakePendingSecretAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        var subscription = await db.OutboundSubscriptions.FirstOrDefaultAsync(
            s => s.ConnectionId == connectionId && s.PendingSecretDelivery && s.Status != OutboundSubscriptionStatus.Ended, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        subscription.SecretDelivered();
        await db.SaveChangesAsync(cancellationToken); // xmin: duas renovações simultâneas, só uma leva o segredo
        return secrets.Unprotect(Scope, SecretKind.OutboundWebhookSecret, subscription.SecretCipher).Reveal();
    }

    public Task EndForConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default) =>
        EndAsync(connectionId, EndedReason, disconnected: true, cancellationToken);

    public async Task HoldForConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        var subscription = await db.OutboundSubscriptions.FirstOrDefaultAsync(s => s.ConnectionId == connectionId, cancellationToken);
        if (subscription is null || subscription.Status != OutboundSubscriptionStatus.Active)
        {
            return;
        }

        subscription.Hold();
        var open = await db.OutboundDeliveries
            .Where(d => d.SubscriptionId == subscription.Id && (d.Status == OutboundDeliveryStatus.Pending || d.Status == OutboundDeliveryStatus.Retrying))
            .ToListAsync(cancellationToken);
        foreach (var delivery in open)
        {
            delivery.Status = OutboundDeliveryStatus.Held;
        }
    }

    private async Task EndAsync(Guid connectionId, string reason, bool disconnected, CancellationToken cancellationToken)
    {
        var subscription = await db.OutboundSubscriptions.FirstOrDefaultAsync(s => s.ConnectionId == connectionId, cancellationToken);
        if (subscription is null || subscription.Status == OutboundSubscriptionStatus.Ended)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var open = await db.OutboundDeliveries
            .Where(d => d.SubscriptionId == subscription.Id
                && (d.Status == OutboundDeliveryStatus.Pending || d.Status == OutboundDeliveryStatus.Retrying || d.Status == OutboundDeliveryStatus.Held))
            .ToListAsync(cancellationToken);
        foreach (var delivery in open)
        {
            delivery.Skip(reason);
        }

        subscription.End(now);
        if (disconnected)
        {
            // CA 3: um último app.disconnected, assinado com o segredo atual, com uma série de tentativas (o job aceita a inscrição Ended só para ele).
            var id = AddEvent(OutboundWebhookCatalog.AppDisconnected, connectionId, now, new Dictionary<string, object?> { ["connection_id"] = connectionId });
            var delivery = AddDelivery(id, subscription.Id, OutboundDeliveryStatus.Pending, now);
            await scheduler.EnqueueAfterCommitAsync([delivery.Id], cancellationToken);
        }
    }

    internal Guid AddEvent(string type, Guid resourceId, DateTimeOffset occurredAt, IReadOnlyDictionary<string, object?> data)
    {
        var definition = OutboundWebhookCatalog.Find(type) ?? throw new InvalidOperationException("Tipo de aviso fora do catálogo: " + type);
        var evt = new OutboundEvent
        {
            Type = type, ResourceId = resourceId, Resource = OutboundWebhookCatalog.ResourceFor(definition, resourceId), OccurredAt = occurredAt,
            CreatedAt = clock.GetUtcNow(), Data = OutboundWebhookBody.Data(data),
        };
        db.OutboundEvents.Add(evt);
        return evt.Id;
    }

    internal OutboundDelivery AddDelivery(Guid eventId, Guid subscriptionId, OutboundDeliveryStatus status, DateTimeOffset now)
    {
        var delivery = new OutboundDelivery { EventId = eventId, SubscriptionId = subscriptionId, Status = status, CreatedAt = now, NextAttemptAt = now, LastQueuedAt = now };
        db.OutboundDeliveries.Add(delivery);
        return delivery;
    }

    /// <summary>Mudar URL e tipos (CA 4): só no ERP, com <c>conecta.edit</c>; URL na lista de hosts (CA 16) e tipos dentro da concessão.</summary>
    public async Task<Result> UpdateAsync(Guid id, OutboundSubscriptionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var subscription = await db.OutboundSubscriptions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (subscription is null)
        {
            return NotFound();
        }

        if (subscription.Status == OutboundSubscriptionStatus.Ended)
        {
            return Error.Conflict("outbound_subscription_ended", "A inscrição foi encerrada. Conecte o Conecta de novo.");
        }

        var url = string.IsNullOrWhiteSpace(input.Url) ? subscription.Url : input.Url.Trim();
        var invalid = url.Length > OutboundSubscription.MaxUrlLength ? OutboundUrlGuard.InvalidUrl : OutboundUrlGuard.Validate(url, conecta.Value.AllowedHosts(), AllowPrivate);
        if (invalid is not null)
        {
            return Error.Validation(Error.Codes.Validation, invalid,
                new Dictionary<string, string[]> { ["url"] = [OutboundUrlGuard.InvalidUrl] });
        }

        var requested = (input.Events ?? []).Distinct(StringComparer.Ordinal).ToList();
        var granted = await access.GrantedScopesAsync(subscription.ConnectionId, cancellationToken);
        var refused = requested.Where(t => OutboundWebhookCatalog.Find(t) is not { } def || def.Type == OutboundWebhookCatalog.Ping || !OutboundWebhookCatalog.Allowed(def, granted)).ToList();
        if (refused.Count > 0)
        {
            return Error.Validation(Error.Codes.Validation, "Avisos fora do que o aplicativo pode ver: " + string.Join(", ", refused) + ".",
                new Dictionary<string, string[]> { ["events"] = [.. refused] });
        }

        subscription.Url = url;
        subscription.Events = [.. requested];
        await db.SaveChangesAsync(cancellationToken); // auditada pelo interceptor (IAuditable)
        return Result.Success();
    }

    /// <summary>"Trocar segredo" (CA 6): o novo sai no próximo <c>/oauth/token</c>; o anterior vale 24 h, ou para na hora com <paramref name="immediate"/>.</summary>
    public async Task<Result<OutboundSecretRotation>> RotateAsync(Guid id, bool immediate, CancellationToken cancellationToken = default)
    {
        var subscription = await db.OutboundSubscriptions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (subscription is null)
        {
            return NotFound();
        }

        if (subscription.Status == OutboundSubscriptionStatus.Ended)
        {
            return Error.Conflict("outbound_subscription_ended", "A inscrição foi encerrada. Conecte o Conecta de novo.");
        }

        var now = clock.GetUtcNow();
        var today = Time.BusinessTime.Today(clock);
        if (!subscription.CanRotate(today))
        {
            return Error.TooManyRequests("outbound_secret_rotation_limit", "O segredo já foi trocado 5 vezes hoje. Tente de novo amanhã.");
        }

        subscription.Rotate(secrets.Protect(Scope, SecretKind.OutboundWebhookSecret, new Secret(OutboundWebhookSigner.NewSecret())), immediate, today, now);
        var text = immediate
            ? "O segredo dos avisos do Conecta foi trocado e o anterior parou de valer na hora."
            : "O segredo dos avisos do Conecta foi trocado. O anterior vale por mais 24 horas.";
        foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
        {
            notifications.Notify(owner, RotatedNotification, text, OutboundWebhookLinks.Sent);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new OutboundSecretRotation(subscription.SecretRotatedAt!.Value, subscription.PreviousValidUntil);
    }

    /// <summary>"Enviar teste" (CA 12): um <c>ping</c> pela mesma esteira.</summary>
    public async Task<Result<Guid>> TestAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var subscription = await db.OutboundSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (subscription is null)
        {
            return NotFound();
        }

        if (subscription.Status != OutboundSubscriptionStatus.Active)
        {
            return Error.Conflict("outbound_subscription_inactive", "A conexão com o Conecta não está ativa.");
        }

        var now = clock.GetUtcNow();
        var eventId = AddEvent(OutboundWebhookCatalog.Ping, subscription.ConnectionId, now, new Dictionary<string, object?> { ["connection_id"] = subscription.ConnectionId });
        var delivery = AddDelivery(eventId, subscription.Id, OutboundDeliveryStatus.Pending, now);
        await scheduler.EnqueueAfterCommitAsync([delivery.Id], cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return delivery.Id;
    }

    private static Error NotFound() => Error.NotFound(Error.Codes.NotFound, "Inscrição não encontrada.");
}

public sealed record OutboundSubscriptionInput(string? Url, IReadOnlyList<string>? Events);

/// <summary>Troca feita: quando, e até quando o anterior vale (nulo com "Invalidar o anterior agora"). Nunca o valor.</summary>
public sealed record OutboundSecretRotation(DateTimeOffset RotatedAt, DateTimeOffset? PreviousValidUntil);

/// <summary>Publicador do catálogo (CA 7): grava o aviso e uma entrega por inscrição que assina o tipo, na transação do consumidor do módulo dono.</summary>
public sealed class OutboundWebhookPublisher(BemAquiDbContext db, OutboundSubscriptionService subscriptions, OutboundDeliveryScheduler scheduler, TimeProvider clock)
    : IOutboundWebhookPublisher
{
    public async Task PublishAsync(OutboundWebhookMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var definition = OutboundWebhookCatalog.Find(message.Type) ?? throw new InvalidOperationException("Tipo de aviso fora do catálogo: " + message.Type);
        var extra = message.Data.Keys.Where(k => !definition.Fields.Contains(k, StringComparer.Ordinal)).ToList();
        if (extra.Count > 0)
        {
            // CA 5: só os campos do catálogo; nada de nome, documento, contato, custo, imposto ou margem.
            throw new InvalidOperationException("Campo fora do catálogo no aviso " + message.Type + ": " + string.Join(", ", extra));
        }

        if (System.Text.Encoding.UTF8.GetByteCount(OutboundWebhookBody.Data(message.Data)) > OutboundWebhookBody.MaxBytes - 1024)
        {
            throw new InvalidOperationException("Aviso " + message.Type + " acima de 64 KB.");
        }

        var type = message.Type;
        var targets = await db.OutboundSubscriptions.AsNoTracking()
            .Where(s => s.Status != OutboundSubscriptionStatus.Ended && s.Events.Contains(type))
            .Select(s => new { s.Id, s.Status })
            .ToListAsync(cancellationToken);
        if (targets.Count == 0)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var eventId = subscriptions.AddEvent(type, message.ResourceId, message.OccurredAt, message.Data);
        var pending = new List<Guid>();
        foreach (var target in targets)
        {
            var held = target.Status == OutboundSubscriptionStatus.Held;
            var delivery = subscriptions.AddDelivery(eventId, target.Id, held ? OutboundDeliveryStatus.Held : OutboundDeliveryStatus.Pending, now);
            if (!held)
            {
                pending.Add(delivery.Id);
            }
        }

        await scheduler.EnqueueAfterCommitAsync(pending, cancellationToken);
    }
}

/// <summary>Telas que os avisos de saída abrem (todo alerta leva à ação, R-PRP-5).</summary>
public static class OutboundWebhookLinks
{
    public const string Sent = WebhookLinks.Events + "?aba=enviados";
    public const string Stalled = Sent + "&situacao=DeadLettered";
    public const string Conecta = "/app/configuracoes/integracoes/conecta";
}
