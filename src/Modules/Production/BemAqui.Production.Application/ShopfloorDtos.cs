using System.Text.Json.Serialization;
using BemAqui.Production.Domain;

namespace BemAqui.Production.Application;

/// <summary>Quem está no app (CA 1): nome, primeiro nome, centro do cargo e o dia de hoje em São Paulo.</summary>
public sealed record ShopfloorOperator(Guid EmployeeId, string Name, string FirstName, Guid? WorkCenterId, string? WorkCenterName, string? Line, DateOnly Today);

/// <summary>
/// Etapa na fila do operador (CA 1 a 3): OS, etapa, centro, produto, projeto, cliente, prometido, horas padrão e apontadas e
/// quem está nela. Sem custo (CA 25, D7).
/// </summary>
public sealed record ShopfloorQueueStep(
    Guid StepId, Guid OrderId, string OrderNumber, string StepCode, string StepName, int StepIndex, IReadOnlyList<string> Codes, IReadOnlyList<string> StepNames,
    ProductionStepStatus Status, Guid? WorkCenterId, string? WorkCenterName, bool IsMyCenter, string ProductName, string ProjectNumber, string ProjectName,
    string PartyName, DateOnly? PromisedDate, decimal StandardHours, decimal AppliedHours, IReadOnlyList<string> People, string? PausedReason, bool AwaitingApproval);

/// <summary>A fila (CA 1): a etapa em andamento do operador (se houver) em destaque e as etapas prontas, as do centro dele primeiro.</summary>
public sealed record ShopfloorQueueView(ShopfloorOperator Operator, ShopfloorQueueStep? Current, IReadOnlyList<ShopfloorQueueStep> Steps, DateTimeOffset ServerNow);

public sealed record ShopfloorChecklistItem(Guid Id, string Text, bool Done, DateTimeOffset? MarkedAt, string? MarkedByName);

/// <summary>Quem está na etapa agora (CA 5 e 14).</summary>
public sealed record ShopfloorPerson(string Name, bool Paused, PauseReason? PauseReason, DateTimeOffset StartedAt, bool IsMe);

/// <summary>Apontamento aberto do operador na etapa (cronômetro, CA 7, 11, 12): pausas fechadas em segundos e a pausa aberta.</summary>
public sealed record ShopfloorMyEntry(Guid EntryId, DateTimeOffset StartedAt, int ClosedPauseSeconds, DateTimeOffset? PauseStartedAt, PauseReason? PauseReason, string? PauseNotes, int NetMinutes);

public sealed record ShopfloorAttachment(Guid Id, string FileName, string ContentType, long Size, bool Viewable);

/// <summary>
/// Etapa aberta no app (CA 5): OS, produto, projeto, cliente, indicador de etapa, horas padrão e apontadas (todos), quem está
/// nela, checklist, anexos técnicos e o que o operador pode fazer. Sem custo (CA 25).
/// </summary>
public sealed record ShopfloorStepDetail(
    ShopfloorQueueStep Step, string? ProductCode, decimal Quantity, IReadOnlyList<ShopfloorChecklistItem> Checklist, IReadOnlyList<ShopfloorPerson> People,
    ShopfloorMyEntry? Mine, IReadOnlyList<ShopfloorAttachment> Attachments, bool RequiresApproval, bool IsQualityStep, string? NextStepName, PauseReason SuggestedPauseReason,
    bool CanStart, string? StartBlockedReason, DateTimeOffset ServerNow);

/// <summary>"Pausar" (CA 11): motivo e texto opcional de até 120.</summary>
public sealed record ShopfloorPauseInput(PauseReason Reason, string? Notes);

/// <summary>"Finalizar etapa" (CA 13) ou "Concluir etapa para todos" (<c>ForAll</c>, CA 14).</summary>
public sealed record ShopfloorFinishInput(bool ForAll);

public sealed record ShopfloorChecklistInput(bool Done);

/// <summary>Resultado de uma ação no app: a etapa como ficou, as horas apontadas pelo operador e a próxima da fila (propositivo).</summary>
public sealed record ShopfloorActionResult(ShopfloorStepDetail? Step, decimal MyHours, ShopfloorQueueStep? NextInQueue, IReadOnlyList<string> ClosedFor);

/// <summary>Pausa no "Apontar por funcionário" e na correção (CA 18, 20).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LaborPauseInput(PauseReason Reason, string? Notes, DateTimeOffset StartedAt, DateTimeOffset EndedAt);

/// <summary>"Apontar por funcionário" (CA 18): funcionário ativo, etapa, início e fim (nulo = agora) e as pausas.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LaborEntryInput(Guid EmployeeId, Guid StepId, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, IReadOnlyList<LaborPauseInput>? Pauses, string? Notes);

/// <summary>"Corrigir apontamento" (CA 20): início, fim, pausas e o motivo obrigatório; a versão que a tela mostrou.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LaborCorrectionInput(DateTimeOffset StartedAt, DateTimeOffset EndedAt, IReadOnlyList<LaborPauseInput>? Pauses, string? Reason, uint Version);

/// <summary>"Aprovar etapa" (CA 15) e a versão da OS que a tela mostrou.</summary>
public sealed record ApproveStepInput(uint Version);

public sealed record LaborPauseView(Guid Id, PauseReason Reason, string? Notes, DateTimeOffset StartedAt, DateTimeOffset? EndedAt);

/// <summary>
/// Linha da aba Apontamentos (CA 31): funcionário, etapa, início, fim, pausas com motivo, minutos líquidos, quem registrou, as
/// marcas "Apontado por", "Encerrado por", "Autoaprovado" e a correção. Sem custo.
/// </summary>
public sealed record LaborEntryView(
    Guid Id, Guid StepId, string StepCode, string StepName, Guid EmployeeId, string EmployeeName, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, int PausedMinutes,
    int NetMinutes, decimal Hours, IReadOnlyList<LaborPauseView> Pauses, string? RecordedByName, bool OnBehalf, LaborCloseKind? CloseKind, string? ClosedByName,
    DateTimeOffset? CorrectedAt, string? CorrectedByName, string? CorrectionReason, bool SelfApproved, string? Notes, bool IsMine, uint Version);

/// <summary>Aba Apontamentos (CA 18, 31): os apontamentos, as horas por etapa e a etapa aguardando aprovação.</summary>
public sealed record OrderLaborView(Guid OrderId, string OrderNumber, IReadOnlyList<LaborEntryView> Entries, decimal TotalHours, Guid? AwaitingApprovalStepId, string? AwaitingApprovalStepName, uint Version);

/// <summary>Apontamento aberto há muito tempo (CA 22): funcionário, OS, etapa, início e a última pausa.</summary>
public sealed record OpenLaborEntry(
    Guid Id, Guid OrderId, string OrderNumber, Guid StepId, string StepCode, string StepName, string EmployeeName, DateTimeOffset StartedAt, decimal OpenHours,
    PauseReason? LastPauseReason, DateTimeOffset? LastPauseAt, uint OrderVersion, uint Version);

public sealed record OpenLaborList(int Hours, IReadOnlyList<OpenLaborEntry> Items);

/// <summary>Funcionário ativo para o "Apontar por funcionário" (CA 18).</summary>
public sealed record LaborEmployeeOption(Guid Id, string Name, string? Registration, string PositionName);