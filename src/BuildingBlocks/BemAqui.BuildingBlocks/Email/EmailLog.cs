using BemAqui.SharedKernel;

namespace BemAqui.BuildingBlocks.Email;

public enum EmailStatus
{
    Queued,
    Sent,
    Failed,
    Skipped,
}

/// <summary>Envio de e-mail (<c>shared.email_logs</c>): fila, tentativas e resultado, consultável pelo suporte.</summary>
public sealed class EmailLog : Entity
{
    [PersonalData]
    public required string To { get; init; }

    public required string Template { get; init; }

    public string Subject { get; set; } = string.Empty;

    /// <summary>JSONB: o modelo do template (sem segredo; links de convite já vêm com token de uso único).</summary>
    public required string Model { get; init; }

    public EmailStatus Status { get; set; } = EmailStatus.Queued;

    public int Attempts { get; set; }

    public string? Error { get; set; }

    public string? RelatedType { get; init; }

    public Guid? RelatedId { get; init; }

    /// <summary>Anexos do e-mail (COM-03): lidos pelo storage pelo id, só se forem do mesmo documento e estiverem limpos.</summary>
    public Guid[] AttachmentIds { get; init; } = [];

    /// <summary>Cópias, separadas por vírgula (até 3).</summary>
    [PersonalData]
    public string? Cc { get; init; }

    [PersonalData]
    public string? ReplyTo { get; init; }

    /// <summary>Nome de exibição do remetente ("{empresa} via BemAqui"); o endereço é sempre o da plataforma.</summary>
    public string? FromName { get; init; }

    /// <summary>Vezes que o envio esperou a varredura do anexo (até 10, a cada 1 min), sem gastar tentativa.</summary>
    public int ScanWaits { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? SentAt { get; set; }
}

/// <summary>Evento interno: e-mail gravado na fila (outbox) e pronto para virar job no Hangfire.</summary>
public sealed record EmailQueued(Guid EmailLogId) : IIntegrationEvent;
