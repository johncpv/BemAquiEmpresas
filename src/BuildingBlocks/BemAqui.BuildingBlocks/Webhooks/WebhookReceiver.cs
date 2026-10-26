using System.Security.Cryptography;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BemAqui.BuildingBlocks.Webhooks;

public enum WebhookReceiveResult
{
    Accepted,
    Duplicate,
    Rejected,
}

public sealed record WebhookReceiveOutcome(WebhookReceiveResult Result, Guid? EventId = null, string? EventType = null);

/// <summary>
/// Recepção no banco do tenant (FND-16 CA 5, 6, 9 e 10): confere a origem com as credenciais do provedor (o segredo é decifrado
/// só aqui), grava o evento cru e a mensagem <see cref="WebhookReceived"/> no mesmo <c>SaveChanges</c> (ADR 0031 §3) e recusa a
/// entrega repetida pelo id e pelo hash do corpo. Falha do banco sobe para o endpoint, que responde 503.
/// </summary>
public sealed partial class WebhookReceiver(
    BemAquiDbContext db,
    ITenantContext tenant,
    ISecretProtector secrets,
    IEventPublisher publisher,
    IUserDirectory users,
    INotificationService notifications,
    IAlertSink alerts,
    TimeProvider clock,
    ILogger<WebhookReceiver> logger)
{
    public const int ExternalIdMaxLength = 200;
    public const int EventTypeMaxLength = 100;
    public const string UnknownEventType = "desconhecido";
    public const string PreviousSecretNotification = "webhook_previous_secret";

    public const string UniqueExternalId = "ux_webhook_events_provider_external_id";
    public const string UniqueBodyHash = "ux_webhook_events_provider_body_sha256";

    public async Task<WebhookReceiveOutcome> ReceiveAsync(WebhookProvider provider, WebhookRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.GetUtcNow();
        var origin = await ValidateOriginAsync(provider, request, now, cancellationToken);
        if (origin.Match == WebhookOriginMatch.Invalid)
        {
            return new WebhookReceiveOutcome(WebhookReceiveResult.Rejected);
        }

        var bodyHash = SHA256.HashData(request.Body.Span);
        var externalId = ExternalId(provider, request, bodyHash);
        var eventType = Clean(provider.EventTypeHeader is null ? null : request.Header(provider.EventTypeHeader), EventTypeMaxLength) ?? UnknownEventType;
        if (origin.Match == WebhookOriginMatch.Previous)
        {
            await WarnPreviousCredentialAsync(provider, origin.ScopeId, now, cancellationToken);
        }

        var existing = await db.WebhookEvents.AsNoTracking()
            .Where(e => e.Provider == provider.Code && (e.ExternalId == externalId || e.BodySha256 == bodyHash))
            .Select(e => (Guid?)e.Id).FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return new WebhookReceiveOutcome(WebhookReceiveResult.Duplicate, existing, eventType);
        }

        var evt = new WebhookEvent
        {
            Provider = provider.Code,
            ExternalId = externalId,
            BodySha256 = bodyHash,
            EventType = eventType,
            ScopeId = origin.ScopeId,
            ReceivedAt = now,
            LastQueuedAt = now,
            Payload = request.Body.ToArray(),
        };
        db.WebhookEvents.Add(evt);
        publisher.Publish(new WebhookReceived(evt.Id, provider.Code));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, UniqueExternalId) || DbErrors.IsUniqueViolation(ex, UniqueBodyHash))
        {
            // Corrida da mesma entrega: só uma linha e uma mensagem ficam (CA 10 e 12).
            db.ChangeTracker.Clear();
            return new WebhookReceiveOutcome(WebhookReceiveResult.Duplicate, null, eventType);
        }

        return new WebhookReceiveOutcome(WebhookReceiveResult.Accepted, evt.Id, eventType);
    }

    private sealed record Origin(WebhookOriginMatch Match, Guid ScopeId);

    /// <summary>Tenta cada credencial do provedor (uma por escopo); a primeira que valida diz o escopo do evento.</summary>
    private async Task<Origin> ValidateOriginAsync(WebhookProvider provider, WebhookRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var credentials = await db.WebhookCredentials.AsNoTracking().Where(c => c.Provider == provider.Code).OrderBy(c => c.ScopeId).ToListAsync(cancellationToken);
        var tenantId = tenant.Tenant?.Id ?? throw new InvalidOperationException("Recepção de webhook fora do escopo de um tenant.");
        var result = new Origin(WebhookOriginMatch.Invalid, Guid.Empty);
        foreach (var credential in credentials.Where(c => c.Kind == provider.Validator.Kind))
        {
            var keys = KeysOf(credential, tenantId, now);
            try
            {
                var match = provider.Validator.Validate(request, keys, now);
                if (match != WebhookOriginMatch.Invalid && result.Match == WebhookOriginMatch.Invalid)
                {
                    result = new Origin(match, credential.ScopeId);
                }
            }
            finally
            {
                keys.Clear();
            }
        }

        return result;
    }

    /// <summary>Chaves prontas para comparar: o segredo decifrado no momento do uso, ou o hash do token (ADR 0007).</summary>
    internal WebhookKeys KeysOf(WebhookCredential credential, Guid tenantId, DateTimeOffset now)
    {
        if (credential.Kind == WebhookCredentialKind.UrlToken)
        {
            return new WebhookKeys(FromHex(credential.TokenHash), FromHex(credential.PreviousTokenHash), credential.PreviousValidUntil);
        }

        var scope = SecretScope.ForTenant(tenantId);
        var current = credential.SecretCipher is null ? null : System.Text.Encoding.UTF8.GetBytes(secrets.Unprotect(scope, SecretKind.WebhookSecret, credential.SecretCipher).Reveal());
        var previous = credential.PreviousSecretCipher is not null && credential.PreviousValidUntil > now
            ? System.Text.Encoding.UTF8.GetBytes(secrets.Unprotect(scope, SecretKind.WebhookSecret, credential.PreviousSecretCipher).Reveal())
            : null;
        return new WebhookKeys(current, previous, credential.PreviousValidUntil);
    }

    internal static string ExternalId(WebhookProvider provider, WebhookRequest request, byte[] bodyHash)
    {
        var delivery = provider.DeliveryIdHeader is null ? null : Clean(request.Header(provider.DeliveryIdHeader), ExternalIdMaxLength);
        return delivery ?? Clean(provider.CompositeKey?.Invoke(request), ExternalIdMaxLength) ?? "sha256:" + Convert.ToHexStringLower(bodyHash);
    }

    /// <summary>Texto curto vindo de fora: só ASCII imprimível, sem espaço nas pontas, no tamanho máximo; vazio vira nulo.</summary>
    internal static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > max || trimmed.Any(c => c < 0x20 || c > 0x7E))
        {
            return null;
        }

        return trimmed;
    }

    /// <summary>
    /// Segredo antigo ainda em uso dentro da janela de 24 h (CA 7): alerta (FND-12) e aviso aos membros do Proprietário, uma vez
    /// por dia. Falhar ao avisar não recusa o evento.
    /// </summary>
    private async Task WarnPreviousCredentialAsync(WebhookProvider provider, Guid scopeId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            await alerts.RaiseAsync(new Alert(WebhookAlerts.PreviousSecretUsed, tenant.Tenant?.Key, "O " + provider.Name + " ainda usa o endereço antigo do aviso."), cancellationToken);
            var day = DateOnly.FromDateTime(Time.BusinessTime.ToBusiness(now).DateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
            {
                await notifications.NotifyOnceAsync(owner, PreviousSecretNotification, "O " + provider.Name + " ainda usa o endereço antigo do aviso.", WebhookLinks.Events,
                    "webhook-previous:" + provider.Code + ":" + scopeId.ToString("N") + ":" + day, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogWarnFailed(logger, ex, provider.Code);
        }
    }

    private static byte[]? FromHex(string? hex) => string.IsNullOrEmpty(hex) ? null : Convert.FromHexString(hex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook {Provider}: não foi possível avisar o uso do segredo antigo.")]
    private static partial void LogWarnFailed(ILogger logger, Exception exception, string provider);
}
