using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Domain;

/// <summary>Passo da régua (FIN-09 CA 1): dias em relação ao vencimento (−30 a +90) e o texto literal com os campos da lista fixa.</summary>
public sealed record CollectionStep(int Days, string Text);

/// <summary>
/// Régua de cobrança por e-mail (<c>finance.collection_rules</c>, FIN-09): uma por tenant, pré-configurada e <b>desligada</b> (ADR 0042),
/// com até 6 passos, o horário de envio (São Paulo) e o Reply-To do financeiro. Auditada (antes e depois); <c>xmin</c> barra duas gravações.
/// </summary>
[DocumentName("A régua de cobrança")]
public sealed class CollectionRule : Entity, IAuditable, IConcurrencyVersioned
{
    public List<CollectionStep> Steps { get; set; } = [];

    /// <summary>Horário de envio em São Paulo (07:00 a 20:00).</summary>
    public TimeOnly SendTime { get; set; } = CollectionRules.DefaultSendTime;

    public bool IsActive { get; set; }

    /// <summary>E-mail do financeiro para a resposta do cliente; vazio: o e-mail da empresa (Configurações → Empresa).</summary>
    public string? ReplyTo { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public uint Version { get; set; }
}

/// <summary>Situação do lembrete (padrão 6): na fila, adiado pelo limite diário ou ignorado com o motivo; o "enviado" vem do e-mail.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CollectionNoticeStatus>))]
public enum CollectionNoticeStatus
{
    Queued,

    /// <summary>Limite diário de e-mails atingido: sai no próximo dia útil, sem pular o passo (CA 7).</summary>
    Deferred,

    Skipped,
}

/// <summary>
/// Lembrete de um passo para um título (<c>finance.collection_notices</c>, FIN-09 CA 4): único por <c>(receivable_id, step)</c>, então o
/// passo sai uma vez só, mesmo com o job rodando duas vezes. Nunca é apagado.
/// </summary>
public sealed class CollectionNotice : Entity
{
    public Guid ReceivableId { get; init; }

    /// <summary>Cliente do título (lembretes do terceiro, CA 8).</summary>
    public Guid PartyId { get; init; }

    /// <summary>Dias do passo em relação ao vencimento.</summary>
    public int Step { get; init; }

    /// <summary>Dia (São Paulo) em que o passo caiu.</summary>
    public DateOnly ScheduledFor { get; init; }

    public Guid? EmailLogId { get; private set; }

    public CollectionNoticeStatus Status { get; private set; }

    public string? SkipReason { get; private set; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Quando o e-mail entrou na fila.</summary>
    public DateTimeOffset? QueuedAt { get; private set; }

    public void Queue(Guid emailLogId, DateTimeOffset at)
    {
        EmailLogId = emailLogId;
        Status = CollectionNoticeStatus.Queued;
        SkipReason = null;
        QueuedAt = at;
    }

    public void Defer(string reason)
    {
        Status = CollectionNoticeStatus.Deferred;
        SkipReason = reason;
    }

    public void Skip(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Status = CollectionNoticeStatus.Skipped;
        SkipReason = reason.Length > 200 ? reason[..200] : reason;
    }
}

/// <summary>
/// "Pausar cobrança" (<c>finance.collection_pauses</c>, FIN-09 CA 6): do cliente ou de um título, com motivo obrigatório e fim opcional.
/// "Retomar" só marca o fim; a linha fica (histórico de pausar e retomar), auditada.
/// </summary>
[DocumentName("A pausa da cobrança")]
public sealed class CollectionPause : Entity, IAuditable
{
    public Guid? PartyId { get; init; }

    public Guid? ReceivableId { get; init; }

    public string Reason { get; init; } = string.Empty;

    public DateOnly From { get; init; }

    /// <summary>Último dia da pausa (inclusive); nulo: até alguém retomar.</summary>
    public DateOnly? Until { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public DateTimeOffset? ResumedAt { get; private set; }

    public Guid? ResumedBy { get; private set; }

    public bool ActiveOn(DateOnly day) => ResumedAt is null && From <= day && (Until is null || Until >= day);

    public void Resume(Guid? by, DateTimeOffset at)
    {
        ResumedAt = at;
        ResumedBy = by;
    }
}
