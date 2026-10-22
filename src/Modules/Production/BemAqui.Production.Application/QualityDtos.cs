using System.Text.Json.Serialization;
using BemAqui.Production.Domain;

namespace BemAqui.Production.Application;

/// <summary>
/// OS na fila de qualidade (PRD-05 CA 2): etapa, OS, letras, produto, projeto, cliente, prometido, tentativa, desde quando espera,
/// se eu posso decidir (segregação do CA 5) e se seria com a marca "Autoinspeção". Sem custo.
/// </summary>
public sealed record QualityQueueItem(
    Guid StepId, Guid OrderId, string OrderNumber, string StepCode, string StepName, int StepIndex, IReadOnlyList<string> Codes, IReadOnlyList<string> StepNames,
    ProductionStepStatus Status, string ProductName, string ProjectNumber, string ProjectName, string PartyName, DateOnly? PromisedDate, int Attempt,
    DateTimeOffset? WaitingSince, bool CanDecide, bool SelfInspection, int OpenNonConformities);

/// <summary>A fila (CA 2): os itens do filtro, quantos eu posso decidir e o total aguardando qualidade.</summary>
public sealed record QualityQueueView(IReadOnlyList<QualityQueueItem> Items, int MineCount, int TotalCount);

/// <summary>Item do checklist da qualidade (CA 3 e 6): "Conforme" (<c>Done</c>), "Não conforme" ou "Não se aplica", com quem e quando.</summary>
public sealed record QualityChecklistItem(Guid Id, string Text, ChecklistResult? Result, DateTimeOffset? MarkedAt, string? MarkedByName);

/// <summary>Etapa de retorno (CA 6), com a tag "Sugerido" e quantas etapas voltam a esperar.</summary>
public sealed record QualityReturnOption(Guid StepId, string Code, string Name, bool Suggested, int Waiting, string Impact);

public sealed record QualityUserOption(Guid Id, string Name);

/// <summary>
/// Etapa de qualidade aberta (CA 3 a 6): o cartão, produto, checklist da tentativa, etapas de retorno, responsáveis possíveis (padrão:
/// eu), a próxima etapa e o impacto da aprovação, o motivo do bloqueio (segregação) e a versão da OS para a decisão.
/// </summary>
public sealed record QualityStepDetail(
    QualityQueueItem Step, string? ProductCode, decimal Quantity, IReadOnlyList<QualityChecklistItem> Checklist, IReadOnlyList<QualityReturnOption> ReturnOptions,
    IReadOnlyList<QualityUserOption> Responsibles, Guid? DefaultResponsibleId, string? NextStepName, string ApprovalImpact, string? BlockedReason, bool InspectionStarted,
    bool CanStartInspection, uint Version, DateTimeOffset ServerNow);

/// <summary>Marcar um item (CA 3 e 6); nulo desmarca.</summary>
public sealed record QualityChecklistInput(ChecklistResult? Result);

/// <summary>"Aprovar qualidade" (CA 3) com a versão da OS que a tela mostrou (CA 14).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QualityApproveInput(uint Version);

/// <summary>"Reprovar qualidade" (CA 6): descrição obrigatória, causa e ação opcionais, responsável (padrão: quem reprovou), etapa de retorno (padrão: a sugerida).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QualityRejectInput(string? Description, string? Cause, string? Action, Guid? ResponsibleUserId, Guid? ReturnToStepId, uint Version);

/// <summary>
/// Resultado da decisão (CA 3, 4 e 6): a próxima etapa ou "Pronta para concluir", se quem decidiu pode concluir a OS (<c>production.approve</c>),
/// a marca "Autoinspeção" e a NC aberta, com a mensagem do verbo ("Qualidade aprovada", "NC {número} aberta").
/// </summary>
public sealed record QualityDecisionResult(
    Guid OrderId, string OrderNumber, QualityResult Result, int Attempt, bool SelfInspection, string? NextStepName, Guid? NextStepId, bool ReadyToComplete, bool CanCompleteOrder,
    Guid? NonConformityId, string? NonConformityNumber, string? ReturnToStepName, string Message);

public sealed record QualityAttemptItem(string Text, ChecklistResult? Result, string? MarkedByName, DateTimeOffset? MarkedAt);

/// <summary>Uma tentativa na aba Qualidade da OS (CA 7): resultado, quem decidiu, a marca "Autoinspeção", a NC e os itens.</summary>
public sealed record QualityAttemptView(
    int Attempt, Guid StepId, string StepCode, string StepName, QualityResult? Result, DateTimeOffset? DecidedAt, string? DecidedByName, bool SelfInspection,
    Guid? NonConformityId, string? NonConformityNumber, string? ReturnToStepName, IReadOnlyList<QualityAttemptItem> Items);

/// <summary>Aba Qualidade da OS (CA 7 e 15): tentativa atual, "Pronta para concluir", as tentativas (mais nova primeiro) e as NC.</summary>
public sealed record OrderQualityView(
    Guid OrderId, string OrderNumber, int CurrentAttempt, bool SelfInspection, bool ReadyToComplete, IReadOnlyList<QualityAttemptView> Attempts, IReadOnlyList<NonConformitySummary> NonConformities);

/// <summary>Linha da lista de NC (CA 11): número, OS, projeto, etapa, responsável, situação e idade em dias.</summary>
public sealed record NonConformitySummary(
    Guid Id, string Number, Guid OrderId, string OrderNumber, Guid ProjectId, string ProjectNumber, string StepCode, string StepName, Guid ResponsibleUserId, string ResponsibleName,
    NonConformityStatus Status, int AgeDays, DateTimeOffset OpenedAt, string Description, int Attempt);

public sealed record NonConformityPage(IReadOnlyList<NonConformitySummary> Items, int Page, int PageSize, int Total, int MineOpen);

/// <summary>Filtros da lista na URL (CA 11): "aguardando" (eu responsável, não encerradas; padrão) ou "todas", situação, OS e projeto.</summary>
public sealed record NonConformityListQuery(string? Filter, string? Situation, Guid? OrderId, Guid? ProjectId);

/// <summary>
/// NC aberta (CA 9 e 10): OS, projeto, etapa de qualidade e de retorno, tentativa, textos, responsável, quem abriu, iniciou a ação e
/// encerrou, a idade e o custo estimado (só com <c>projects.view_cost</c>; senão nulo e <c>CostVisible</c> falso).
/// </summary>
public sealed record NonConformityDetail(
    Guid Id, string Number, NonConformityStatus Status, Guid OrderId, string OrderNumber, Guid ProjectId, string ProjectNumber, string ProjectName, string PartyName,
    Guid StepId, string StepCode, string StepName, Guid ReturnToStepId, string ReturnToStepName, int Attempt, string Description, string? Cause, string? Action,
    Guid ResponsibleUserId, string ResponsibleName, decimal? Cost, bool CostVisible, string? Result, string? OpenedByName, DateTimeOffset OpenedAt,
    string? ActionStartedByName, DateTimeOffset? ActionStartedAt, string? ClosedByName, DateTimeOffset? ClosedAt, int AgeDays, IReadOnlyList<QualityUserOption> Responsibles,
    uint Version);

/// <summary>"Salvar" da NC aberta ou em ação (CA 9): descrição, causa, ação e responsável.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NonConformityUpdateInput(string? Description, string? Cause, string? Action, Guid ResponsibleUserId, uint Version);

/// <summary>"Iniciar ação" (CA 9): causa e ação obrigatórias.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NonConformityActionInput(string? Cause, string? Action, uint Version);

/// <summary>"Encerrar NC" (CA 9): o resultado obrigatório.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NonConformityCloseInput(string? Result, uint Version);

/// <summary>Custo estimado da NC (CA 10), informativo; nulo apaga.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record NonConformityCostInput(decimal? Cost, uint Version);