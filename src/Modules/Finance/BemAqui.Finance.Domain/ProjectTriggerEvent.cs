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

    public DateOnly OccurredOn { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>
    /// Versão do aceite do cliente (EXE-01a CA 18): evento com versão menor que a gravada é velho e é descartado; 0 nos outros gatilhos e no
    /// aceite técnico da OS de instalação.
    /// </summary>
    public int Version { get; set; }

    /// <summary>Aceite desfeito (EXE-01a CA 18): o gatilho não vale para título que nascer depois, até um aceite de versão maior.</summary>
    public bool Reverted { get; set; }

    /// <summary>Por que o desfazer não voltou o título (baixa, boleto ou mudança depois da checagem): compensação manual.</summary>
    public string? RevertNote { get; set; }

    /// <summary>Vencimento liberado (CA 6): data do evento + prazo do marco, em dias corridos.</summary>
    public static DateOnly DueDate(DateOnly occurredOn, int? daysAfterEvent) => occurredOn.AddDays(daysAfterEvent ?? 0);
}
