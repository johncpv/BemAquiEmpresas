using System.Text.Json.Serialization;
using BemAqui.Commercial.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Domain;

/// <summary>Situação da etapa do projeto de serviço (EXE-03a): a primeira nasce <see cref="Ready"/>, as demais <see cref="Waiting"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProjectStageStatus>))]
public enum ProjectStageStatus
{
    Waiting,
    Ready,
    InProgress,
    AwaitingApproval,
    Done,
}

/// <summary>Resultado de um item do checklist da etapa: feito ou não se aplica; nulo = ainda não marcado.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ChecklistResult>))]
public enum ChecklistResult
{
    Done,
    NotApplicable,
}

/// <summary>
/// Etapa do projeto de serviço (<c>commercial.project_stages</c>, EXE-03a): cópia de uma etapa do modelo (EXE-02), com as horas previstas já
/// multiplicadas pela quantidade do item. É a etapa <b>do modelo</b> (ex.: "T Treinamento"); a etapa <b>do projeto</b> (P · I · E · A · C) continua
/// sendo o estado, pela <see cref="ProjectStageMap"/>. Iniciar, concluir e aprovar são da EXE-03b.
/// </summary>
[DocumentName("A etapa do projeto")]
public sealed class ProjectServiceStage : Entity, IAuditable, IConcurrencyVersioned
{
    public Guid ProjectId { get; init; }

    public int Sequence { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public StageRole Role { get; set; }

    /// <summary>Horas previstas da etapa (numeric(18,4)) = horas por unidade do modelo × quantidade do item que originou a sugestão.</summary>
    public decimal PlannedHours { get; set; }

    public Guid? PositionId { get; set; }

    public bool RequiresApproval { get; set; }

    public ProjectStageStatus Status { get; set; } = ProjectStageStatus.Waiting;

    public Guid? ResponsibleUserId { get; set; }

    public DateOnly? PlannedStart { get; set; }

    public DateOnly? PlannedEnd { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>Data do fato da conclusão (data de negócio).</summary>
    public DateOnly? CompletedOn { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public Guid? CompletedBy { get; set; }

    /// <summary>Sobe a cada conclusão (EXE-03b/05a); 0 até a primeira conclusão registrada por alguém.</summary>
    public int CompletionVersion { get; set; }

    public Guid? ApprovedBy { get; set; }

    public bool SelfApproved { get; set; }

    public uint Version { get; set; }

    /// <summary>Etapa já começou: só responsável e datas previstas mudam (CA 5).</summary>
    public bool IsStarted => Status is ProjectStageStatus.InProgress or ProjectStageStatus.AwaitingApproval or ProjectStageStatus.Done || StartedAt is not null;
}

/// <summary>Item do checklist da etapa (<c>commercial.project_stage_checklist</c>, EXE-03a); marcar é da EXE-03b.</summary>
public sealed class ProjectServiceStageCheck : Entity, IAuditable
{
    public Guid StageId { get; init; }

    public int Sequence { get; set; }

    public string Text { get; set; } = string.Empty;

    public ChecklistResult? Result { get; set; }

    public DateTimeOffset? MarkedAt { get; set; }

    public Guid? MarkedBy { get; set; }
}

/// <summary>Etapa copiada do modelo, ainda sem id de projeto gravado: o serviço cria as entidades a partir daqui.</summary>
public sealed record StageCopy(
    int Sequence, string Code, string Name, StageRole Role, decimal PlannedHours, Guid? PositionId, bool RequiresApproval, IReadOnlyList<string> Checklist, ProjectStageStatus Status,
    DateOnly? CompletedOn);

/// <summary>
/// Regras puras das etapas do projeto de serviço (EXE-03a CA 2, 3 e 8): a cópia do modelo pela situação do projeto e a etapa atual.
/// </summary>
public static class ServiceStagePlan
{
    /// <summary>Horas previstas = horas por unidade × quantidade (4 casas); sem item, × 1.</summary>
    public static decimal PlannedHours(decimal hoursPerUnit, decimal quantity) =>
        Math.Round(hoursPerUnit * (quantity > 0 ? quantity : 1m), 4, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Cópia (CA 2 e 3): em <c>Planned</c> e <c>InProgress</c>, a primeira <c>Ready</c> e as demais <c>Waiting</c>. Em <c>Delivered</c>, as de Execução
    /// e a de Entrega nascem <c>Done</c> na data da entrega; a de Aceite, <c>Done</c> na data do aceite vigente ou <c>Ready</c> sem aceite.
    /// </summary>
    public static IReadOnlyList<StageCopy> Copy(IReadOnlyList<StageTemplateStepInfo> steps, decimal quantity, ProjectStatus status, DateOnly? deliveredOn, DateOnly? acceptedOn)
    {
        ArgumentNullException.ThrowIfNull(steps);
        return steps.Select((s, i) =>
        {
            var (state, on) = Initial(i, s.Role, status, deliveredOn, acceptedOn);
            return new StageCopy(i + 1, s.Code, s.Name, s.Role, PlannedHours(s.StandardHoursPerUnit, quantity), s.PositionId, s.RequiresApproval, s.Checklist.ToList(), state, on);
        }).ToList();
    }

    /// <summary>Etapa atual (CA 8): a primeira não concluída pela sequência; com todas concluídas, a última. Sem etapas, nula.</summary>
    public static ProjectCurrentStage? Current(IReadOnlyCollection<(int Sequence, string Code, string Name, ProjectStageStatus Status)> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        if (stages.Count == 0)
        {
            return null;
        }

        var ordered = stages.OrderBy(s => s.Sequence).ToList();
        var index = ordered.FindIndex(s => s.Status != ProjectStageStatus.Done);
        if (index < 0)
        {
            index = ordered.Count - 1;
        }

        return new ProjectCurrentStage(ordered[index].Code, ordered[index].Name, index + 1, ordered.Count);
    }

    /// <summary>
    /// Situação inicial da etapa pela situação do projeto (CA 2, 3 e 5): no entregue, Execução e Entrega concluídas na data da entrega e o Aceite
    /// concluído na data do aceite (ou <c>Ready</c> sem aceite); nos demais, a primeira <c>Ready</c> e as outras <c>Waiting</c>.
    /// </summary>
    public static (ProjectStageStatus Status, DateOnly? CompletedOn) Initial(int index, StageRole role, ProjectStatus status, DateOnly? deliveredOn, DateOnly? acceptedOn) =>
        status != ProjectStatus.Delivered
            ? (index == 0 ? ProjectStageStatus.Ready : ProjectStageStatus.Waiting, null)
            : role != StageRole.Acceptance
                ? (ProjectStageStatus.Done, deliveredOn)
                : acceptedOn is { } accepted ? (ProjectStageStatus.Done, accepted) : (ProjectStageStatus.Ready, null);

    public const string StartedRefusal = "O projeto já começou: as etapas não mudam.";

    public const string ClosedRefusal = "Projeto encerrado ou cancelado: as etapas não mudam.";

    /// <summary>Etapas mudam em projeto <c>Planned</c>, <c>InProgress</c> ou <c>Delivered</c> (CA 3); encerrado ou cancelado, nunca.</summary>
    public static bool AcceptsStages(ProjectStatus status) => status is not (ProjectStatus.Closed or ProjectStatus.Cancelled);
}
