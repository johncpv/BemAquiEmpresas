using BemAqui.SharedKernel;

namespace BemAqui.Finance.Domain;

/// <summary>
/// Evento de gatilho recebido para um projeto (<c>finance.project_trigger_events</c>, COM-04b, ADR 0019 §1): o outbox não
/// garante ordem, então o evento fica gravado e a parcela que nascer depois já sai aberta. Um por projeto e evento.
/// </summary>
public sealed class ProjectTriggerEvent : Entity
{
    public Guid ProjectId { get; init; }

    public Guid ContractId { get; init; }

    public TriggerEvent Event { get; init; }

    public DateOnly OccurredOn { get; init; }

    public DateTimeOffset ReceivedAt { get; init; }

    /// <summary>Vencimento liberado (CA 6): data do evento + prazo do marco, em dias corridos.</summary>
    public static DateOnly DueDate(DateOnly occurredOn, int? daysAfterEvent) => occurredOn.AddDays(daysAfterEvent ?? 0);
}
