using System.Text.Json.Serialization;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;

namespace BemAqui.Commercial.Application;

/// <summary>Item do checklist da etapa na leitura (EXE-03a CA 7), com quem marcou e quando (EXE-03b CA 2).</summary>
public sealed record ProjectStageCheckView(Guid Id, int Sequence, string Text, StageChecklistResult? Result, DateTimeOffset? MarkedAt = null, string? MarkedByName = null);

/// <summary>
/// Etapa do projeto de serviço na aba Etapas (CA 7): letra, nome, papel, situação, responsável, previsto, realizado e o progresso do checklist.
/// O nome do responsável só sai aqui (nunca na API v1 nem no PDF, CA 9). EXE-03b: <c>HasMilestone</c> (etapa ligada a marco financeiro), o selo
/// "Autoaprovada", quem concluiu e quem aprovou.
/// </summary>
public sealed record ProjectStageView(
    Guid Id,
    int Sequence,
    string Code,
    string Name,
    StageRole Role,
    ProjectStageStatus Status,
    decimal PlannedHours,
    Guid? PositionId,
    string? PositionName,
    bool RequiresApproval,
    Guid? ResponsibleUserId,
    string? ResponsibleName,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd,
    DateTimeOffset? StartedAt,
    DateOnly? CompletedOn,
    int ChecklistDone,
    int ChecklistTotal,
    IReadOnlyList<ProjectStageCheckView> Checklist,
    bool HasMilestone = false,
    bool SelfApproved = false,
    string? CompletedByName = null,
    string? ApprovedByName = null,
    int CompletionVersion = 0);

/// <summary>
/// Etapas do projeto (CA 7): a lista, o modelo copiado (nome e revisão), se já começou (só responsável e datas mudam, CA 5), a etapa atual e, para
/// "Usar um modelo de etapas" (CA 3), os modelos ativos com o sugerido. <c>Version</c> é a do projeto (xmin).
/// </summary>
public sealed record ProjectStagesView(
    Guid ProjectId,
    IReadOnlyList<ProjectStageView> Stages,
    Guid? TemplateId,
    string? TemplateName,
    int? TemplateRevision,
    bool Started,
    ProjectCurrentStage? CurrentStage,
    bool CanApplyTemplate,
    Guid? SuggestedTemplateId,
    IReadOnlyList<StageTemplateOption> Templates,
    uint Version);

/// <summary>"Usar um modelo de etapas" (CA 3) ou trocar o modelo antes de começar (CA 5), com a versão do projeto que o cliente abriu.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProjectStageTemplateInput(Guid TemplateId, uint Version);

/// <summary>
/// Etapa no salvar (CA 5): <c>Id</c> nulo é etapa nova; com id, é uma etapa deste projeto (404 se não for). A sequência é a posição na lista.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProjectStageInput(
    Guid? Id,
    string Name,
    string Code,
    StageRole Role,
    decimal PlannedHours,
    Guid? PositionId,
    bool RequiresApproval,
    IReadOnlyList<string>? Checklist,
    Guid? ResponsibleUserId,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd);

/// <summary>Edição das etapas (CA 5) com a versão do projeto que o cliente abriu.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProjectStagesInput(IReadOnlyList<ProjectStageInput> Stages, uint Version);

/// <summary>
/// Ponto de extensão da checagem de marcos do contrato na edição das etapas (CA 5): devolve a descrição dos marcos <c>StageCompleted</c> que
/// apontam para as letras informadas. Até a EXE-05a não há marco por etapa e a implementação padrão passa sempre; a EXE-05a a preenche.
/// </summary>
public interface IStageMilestoneGuard
{
    Task<IReadOnlyList<string>> MilestonesForAsync(Guid projectId, IReadOnlyCollection<string> stageCodes, CancellationToken cancellationToken = default);
}

/// <summary>Sem marco por etapa até a EXE-05a: nenhuma etapa é apontada por marco.</summary>
public sealed class NoStageMilestoneGuard : IStageMilestoneGuard
{
    public Task<IReadOnlyList<string>> MilestonesForAsync(Guid projectId, IReadOnlyCollection<string> stageCodes, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}

/// <summary>Iniciar a etapa (EXE-03b CA 1): com o projeto <c>Planned</c>, a data, a situação vista e o motivo vão ao "Iniciar execução" da EXE-01a.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProjectStageStartInput(uint Version, ProjectStatus? ExpectedStatus = null, DateOnly? Date = null, string? BackdateReason = null);

/// <summary>Marcar (Feito / Não se aplica) ou desmarcar (nulo) um item do checklist (EXE-03b CA 2).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProjectStageCheckInput(StageChecklistResult? Result, uint Version);

/// <summary>
/// Concluir a etapa (EXE-03b CA 3 a 7): data do fato; situação vista e motivo da data anterior (Entrega, Aceite e marco financeiro); quem aceitou,
/// papel e o termo (Aceite do cliente).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProjectStageCompleteInput(
    DateOnly Date, uint Version, ProjectStatus? ExpectedStatus = null, string? BackdateReason = null, string? AcceptedByName = null, string? AcceptedByRole = null,
    Guid? AttachmentId = null);

/// <summary>
/// Aprovar a etapa (EXE-03b CA 5). A data é a da conclusão; na Entrega, no Aceite e com marco, a situação vista e o motivo da data anterior
/// valem como no concluir (o Aceite leva quem aceitou).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProjectStageApproveInput(
    uint Version, ProjectStatus? ExpectedStatus = null, string? BackdateReason = null, string? AcceptedByName = null, string? AcceptedByRole = null, Guid? AttachmentId = null);
