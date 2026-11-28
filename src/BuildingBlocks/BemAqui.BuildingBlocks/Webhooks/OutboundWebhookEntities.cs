using BemAqui.SharedKernel;

namespace BemAqui.BuildingBlocks.Webhooks;

/// <summary>Situação da inscrição (INT-02, Regras): <c>Active</c> → <c>Held</c> (conexão suspensa) → <c>Ended</c> (final; conexão nova cria inscrição nova).</summary>
public enum OutboundSubscriptionStatus
{
    Active,
    Held,
    Ended,
}

/// <summary>Situação da entrega: <c>Pending</c> → <c>Retrying</c> → <c>Delivered</c> | <c>DeadLettered</c>; <c>Held</c> (inscrição suspensa); <c>Skipped</c>.</summary>
public enum OutboundDeliveryStatus
{
    Pending,
    Retrying,
    Delivered,
    DeadLettered,
    Held,
    Skipped,
}

/// <summary>
/// Inscrição de avisos de uma conexão (<c>shared.outbound_webhook_subscriptions</c>, INT-02 CA 1 a 6): URL, tipos assinados, situação e o segredo
/// de assinatura cifrado com <c>outbound-webhook-secret</c> (o anterior vale até <see cref="PreviousValidUntil"/>). Auditada sem o segredo
/// (<c>[ProtectedSecret]</c>).
/// </summary>
[DocumentName("A inscrição de avisos")]
public sealed class OutboundSubscription : Entity, IAuditable, IConcurrencyVersioned
{
    public const int MaxUrlLength = 500;
    public const int RotationsPerDay = 5;
    public static readonly TimeSpan Overlap = TimeSpan.FromHours(24);

    public Guid ConnectionId { get; init; }

    public required string ClientId { get; init; }

    public required string Url { get; set; }

    public string[] Events { get; set; } = [];

    public OutboundSubscriptionStatus Status { get; private set; } = OutboundSubscriptionStatus.Active;

    [ProtectedSecret]
    public string SecretCipher { get; private set; } = string.Empty;

    [ProtectedSecret]
    public string? PreviousSecretCipher { get; private set; }

    public DateTimeOffset? PreviousValidUntil { get; private set; }

    /// <summary>
    /// Segredo atual ainda não confirmado pelo aplicativo (INT-04 CA 5): vai em toda resposta do <c>/oauth/token</c> até o aplicativo renovar com o
    /// <c>bqr_</c> de uma resposta que o levou (<see cref="SecretVersion"/>).
    /// </summary>
    public bool PendingSecretDelivery { get; private set; }

    /// <summary>Versão do segredo atual (1 na criação, +1 a cada troca): a renovação prova ter recebido esta versão, não uma anterior.</summary>
    public int SecretVersion { get; private set; }

    public DateTimeOffset? SecretRotatedAt { get; private set; }

    public DateOnly? RotationDay { get; private set; }

    public int RotationsOnDay { get; private set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? EndedAt { get; private set; }

    public DateTimeOffset? LastSuccessAt { get; set; }

    public uint Version { get; set; }

    public static OutboundSubscription Create(Guid connectionId, string clientId, string url, IEnumerable<string> events, string secretCipher, DateTimeOffset now) => new()
    {
        ConnectionId = connectionId,
        ClientId = clientId,
        Url = url,
        Events = [.. events],
        SecretCipher = secretCipher,
        SecretVersion = 1,
        PendingSecretDelivery = true,
        CreatedAt = now,
    };

    public bool Subscribes(string type) => Events.Contains(type, StringComparer.Ordinal);

    /// <summary>Segredo anterior ainda aceito (sobreposição de 24 h).</summary>
    public string? PreviousIfValid(DateTimeOffset now) => PreviousValidUntil is { } until && until > now ? PreviousSecretCipher : null;

    public bool CanRotate(DateOnly today) => RotationDay != today || RotationsOnDay < RotationsPerDay;

    /// <summary>Troca (CA 6): a atual vira a anterior por 24 h, ou some na hora com <paramref name="immediate"/>; o novo sai na próxima renovação.</summary>
    public void Rotate(string newCipher, bool immediate, DateOnly today, DateTimeOffset now)
    {
        PreviousSecretCipher = immediate ? null : SecretCipher;
        PreviousValidUntil = immediate ? null : now + Overlap;
        SecretCipher = newCipher;
        PendingSecretDelivery = true;
        SecretVersion++;
        SecretRotatedAt = now;
        RotationsOnDay = RotationDay == today ? RotationsOnDay + 1 : 1;
        RotationDay = today;
    }

    /// <summary>O aplicativo provou ter gravado a resposta com o segredo <paramref name="version"/>: baixa o pendente só se for o atual.</summary>
    public bool ConfirmSecret(int version)
    {
        if (!PendingSecretDelivery || version != SecretVersion)
        {
            return false;
        }

        PendingSecretDelivery = false;
        return true;
    }

    public void Hold()
    {
        if (Status == OutboundSubscriptionStatus.Active)
        {
            Status = OutboundSubscriptionStatus.Held;
        }
    }

    public void End(DateTimeOffset now)
    {
        if (Status != OutboundSubscriptionStatus.Ended)
        {
            Status = OutboundSubscriptionStatus.Ended;
            EndedAt = now;
            PendingSecretDelivery = false;
        }
    }
}

/// <summary>
/// Aviso público gravado pelo tradutor do módulo dono (<c>shared.outbound_webhook_events</c>, CA 7): tipo, recurso, quando aconteceu e o <c>data</c>
/// mínimo em JSON (texto, para os bytes do corpo serem sempre os mesmos). O <c>data</c> some em 90 dias quando todas as entregas terminaram (CA 14).
/// </summary>
public sealed class OutboundEvent : Entity
{
    public required string Type { get; init; }

    public Guid ResourceId { get; init; }

    public required string Resource { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>JSON do <c>data</c>; nulo depois do expurgo.</summary>
    public string? Data { get; set; }
}

/// <summary>Uma entrega do aviso para uma inscrição (<c>shared.outbound_webhook_deliveries</c>, única por evento e inscrição; transição pelo <c>xmin</c>).</summary>
public sealed class OutboundDelivery : Entity, IConcurrencyVersioned
{
    public const int ErrorMaxLength = 500;

    public Guid EventId { get; init; }

    public Guid SubscriptionId { get; init; }

    public OutboundDeliveryStatus Status { get; set; } = OutboundDeliveryStatus.Pending;

    /// <summary>Envios da série atual ("Reenviar" começa outra).</summary>
    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    public int? LastStatusCode { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset? DeliveredAt { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Última vez que o job foi enfileirado (o varredor recoloca as paradas há mais de 5 min).</summary>
    public DateTimeOffset LastQueuedAt { get; set; }

    public uint Version { get; set; }

    public bool IsFinal => Status is OutboundDeliveryStatus.Delivered or OutboundDeliveryStatus.DeadLettered or OutboundDeliveryStatus.Skipped;

    public bool IsOpen => Status is OutboundDeliveryStatus.Pending or OutboundDeliveryStatus.Retrying or OutboundDeliveryStatus.Held;

    public void Skip(string reason)
    {
        Status = OutboundDeliveryStatus.Skipped;
        LastError = Cut(reason);
    }

    public static string? Cut(string? value) => value is { Length: > ErrorMaxLength } ? value[..ErrorMaxLength] : value;
}

/// <summary>Uma tentativa de envio (<c>shared.outbound_webhook_attempts</c>, CA 15): hora, código HTTP, duração e o erro resumido, nunca o corpo da resposta.</summary>
public sealed class OutboundAttempt : Entity
{
    public Guid DeliveryId { get; init; }

    public int Number { get; init; }

    public DateTimeOffset At { get; init; }

    public int? StatusCode { get; init; }

    public int DurationMs { get; init; }

    public string? Error { get; init; }
}
