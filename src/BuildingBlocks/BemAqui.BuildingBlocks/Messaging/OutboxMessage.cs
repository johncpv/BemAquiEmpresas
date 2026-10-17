namespace BemAqui.BuildingBlocks.Messaging;

/// <summary>Evento gravado na transação do comando (<c>shared.outbox_messages</c>), entregue depois pelo dispatcher.</summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Type { get; init; }

    /// <summary>JSONB com o evento.</summary>
    public required string Payload { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public DateTimeOffset? DeadLetteredAt { get; set; }

    public string? Error { get; set; }

    public Guid? CorrelationId { get; init; }
}

/// <summary>Entrega já feita a um handler (<c>shared.inbox_messages</c>, único por mensagem e handler).</summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; init; }

    public required string Handler { get; init; }

    public DateTimeOffset ProcessedAt { get; init; }
}
