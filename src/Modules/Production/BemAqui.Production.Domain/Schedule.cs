using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Production.Domain;

/// <summary>
/// Execução do plano (<c>production.schedule_runs</c>, ADR 0029): o histórico do plano. Fora da auditoria (não é <see cref="IAuditable"/>);
/// escrita só pelo <c>ScheduleService</c>.
/// </summary>
public sealed class ScheduleRun : Entity
{
    public ScheduleTrigger Trigger { get; set; }

    public ScheduleRunStatus Status { get; set; }

    public DateTimeOffset RequestedAt { get; init; }

    /// <summary>Quem pediu o "Recalcular agora"; nulo no job.</summary>
    public Guid? RequestedBy { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>OS de fabricação calculadas.</summary>
    public int OrdersCount { get; set; }

    /// <summary>Etapas não concluídas calculadas.</summary>
    public int StepsCount { get; set; }

    /// <summary>OS cuja data prevista (início, fim ou motivo) mudou em relação ao plano anterior.</summary>
    public int ChangedOrdersCount { get; set; }

    /// <summary>OS sem previsão (CA 8).</summary>
    public int WithoutForecastCount { get; set; }

    public int DurationMs { get; set; }

    /// <summary>Erro da execução que falhou (até 2000), sem dado pessoal.</summary>
    public string? Error { get; set; }
}

/// <summary>Horas de uma etapa num centro e num dia da última execução bem-sucedida (<c>production.schedule_allocations</c>).</summary>
public sealed class ScheduleAllocation : Entity
{
    public Guid StepId { get; init; }

    public Guid ProductionOrderId { get; init; }

    public Guid WorkCenterId { get; init; }

    public DateOnly Date { get; init; }

    public decimal Hours { get; init; }
}

/// <summary>
/// Previsão de uma etapa não concluída (<c>production.schedule_step_forecasts</c>, <c>step_id</c> único): horas restantes, início e fim
/// previstos, ou o motivo de não ter data. A previsão da OS sai das etapas dela (menor início, maior fim, primeiro motivo).
/// </summary>
public sealed class ScheduleStepForecast
{
    public Guid StepId { get; init; }

    public Guid ProductionOrderId { get; init; }

    public int Sequence { get; init; }

    public decimal Hours { get; init; }

    public DateOnly? PlannedStart { get; init; }

    public DateOnly? PlannedEnd { get; init; }

    public string? NoForecastReason { get; init; }

    public Guid RunId { get; init; }
}
