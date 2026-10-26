using BemAqui.SharedKernel;

namespace BemAqui.BuildingBlocks.Webhooks;

/// <summary>Situação do evento recebido (FND-16 CA 9 e 11; <c>04-fluxos</c> §webhooks).</summary>
public enum WebhookEventStatus
{
    Received,
    Processed,
    Ignored,
    Failed,
    DeadLettered,
}

/// <summary>
/// Evento cru recebido de um provedor (<c>shared.webhook_events</c>, ADR 0031 §4): gravado na mesma transação da mensagem
/// <see cref="WebhookReceived"/> do outbox, único por <c>(provider, external_id)</c> e por <c>(provider, body_sha256)</c>. O corpo
/// é dado pessoal e some depois de 90 dias (a linha fica para a deduplicação). Não é <see cref="IAuditable"/>: a trilha é a
/// própria linha; reprocessar e ler o corpo vão para a auditoria.
/// </summary>
[DocumentName("O evento")]
public sealed class WebhookEvent : Entity, IConcurrencyVersioned
{
    public const int ErrorMaxLength = 500;

    public required string Provider { get; init; }

    /// <summary>Id da entrega do provedor, a chave composta declarada ou, sem nenhum dos dois, o SHA-256 do corpo em hex.</summary>
    public required string ExternalId { get; init; }

    /// <summary>SHA-256 dos bytes crus do corpo (32 bytes).</summary>
    public required byte[] BodySha256 { get; init; }

    public required string EventType { get; init; }

    /// <summary>Credencial que validou a origem (ex.: a conta bancária do Inter); <see cref="Guid.Empty"/> é a do tenant.</summary>
    public Guid ScopeId { get; init; }

    public DateTimeOffset ReceivedAt { get; init; }

    /// <summary>Corpo cru como chegou; nulo depois do expurgo de 90 dias (CA 14).</summary>
    [PersonalData]
    public byte[]? Payload { get; set; }

    public WebhookEventStatus Status { get; set; } = WebhookEventStatus.Received;

    public int Attempts { get; set; }

    /// <summary>Motivo do <c>Ignored</c> ou erro do <c>Failed</c>, sem dado pessoal.</summary>
    public string? LastError { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Última vez que a mensagem foi posta no outbox (recepção, varredor ou "Reprocessar").</summary>
    public DateTimeOffset LastQueuedAt { get; set; }

    /// <summary>Vezes que o varredor de reserva recolocou o evento parado no outbox (ADR 0031 §3).</summary>
    public int Requeues { get; set; }

    public uint Version { get; set; }

    public bool IsFinal => Status is WebhookEventStatus.Processed or WebhookEventStatus.Ignored or WebhookEventStatus.DeadLettered;
}

/// <summary>
/// Credencial de um provedor no tenant (<c>shared.webhook_tokens</c>, CA 6 e 7): o hash do token da URL ou o segredo de
/// assinatura cifrado (<c>webhook-secret</c>), com o anterior aceito até <see cref="PreviousValidUntil"/> (24 h de sobreposição).
/// Única por <c>(provider, scope_id)</c>; a auditoria registra só que mudou.
/// </summary>
public sealed class WebhookCredential : Entity, IAuditable
{
    public static readonly TimeSpan Overlap = TimeSpan.FromHours(24);

    public required string Provider { get; init; }

    /// <summary>Escopo da credencial (ex.: a integração da conta do Inter); <see cref="Guid.Empty"/> vale para o tenant.</summary>
    public Guid ScopeId { get; init; }

    public WebhookCredentialKind Kind { get; init; }

    /// <summary>SHA-256 (hex) do token atual, para o <see cref="UrlTokenValidator"/>.</summary>
    [ProtectedSecret]
    public string? TokenHash { get; private set; }

    [ProtectedSecret]
    public string? PreviousTokenHash { get; private set; }

    /// <summary>Segredo de assinatura atual, cifrado com <c>webhook-secret</c> do tenant.</summary>
    [ProtectedSecret]
    public string? SecretCipher { get; private set; }

    [ProtectedSecret]
    public string? PreviousSecretCipher { get; private set; }

    public DateTimeOffset? PreviousValidUntil { get; private set; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? RotatedAt { get; private set; }

    /// <summary>Troca a credencial: a atual vira a anterior, aceita por mais 24 h (CA 7).</summary>
    public void Rotate(string? tokenHash, string? secretCipher, Guid? by, DateTimeOffset now)
    {
        var hadCurrent = TokenHash is not null || SecretCipher is not null;
        PreviousTokenHash = hadCurrent ? TokenHash : null;
        PreviousSecretCipher = hadCurrent ? SecretCipher : null;
        PreviousValidUntil = hadCurrent ? now + Overlap : null;
        TokenHash = tokenHash;
        SecretCipher = secretCipher;
        CreatedBy = by;
        RotatedAt = now;
    }
}
