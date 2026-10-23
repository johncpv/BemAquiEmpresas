using System.Text.Json.Serialization;
using BemAqui.Commercial.Contracts;
using BemAqui.Production.Domain;

namespace BemAqui.Production.Application;

/// <summary>"Gerar OS de fabricação" (PRD-01 CA 5), pelo item do projeto.</summary>
public sealed record FabricationGenerateInput(Guid ProjectItemId);

/// <summary>"Gerar OS" em lote (CA 5): os itens escolhidos no filtro "Itens de projeto sem OS".</summary>
public sealed record FabricationBatchInput(IReadOnlyList<Guid> ProjectItemIds);

public sealed record FabricationBatchSkip(Guid ProjectItemId, string Reason);

/// <summary>Resultado do lote: quantas OS nasceram e por que algum item ficou de fora.</summary>
public sealed record FabricationBatchResult(int Created, IReadOnlyList<string> Numbers, IReadOnlyList<FabricationBatchSkip> Skipped);

/// <summary>Item de projeto sem OS de fabricação (CA 5): equipamento da Fase 2 ou componente fabricado.</summary>
public sealed record PendingFabricationItem(
    Guid ProjectItemId, Guid ProjectId, string ProjectNumber, string ProjectName, string PartyName, int Sequence, string Description, decimal Quantity, ProjectItemKind Kind,
    Guid? ProductId, string? ProductName, DateOnly? PromisedDate);

public sealed record PendingFabricationPage(IReadOnlyList<PendingFabricationItem> Items, int Page, int PageSize, int Total);

/// <summary>Etapa no salvar (CA 7): id nulo é etapa nova; com id, é uma etapa desta OS. A ordem é a posição na lista.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FabricationStepInput(Guid? Id, string Name, string Code, Guid? WorkCenterId, decimal StandardHours, bool IsQualityStep, bool RequiresApproval, IReadOnlyList<string> Checklist);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FabricationStepsInput(IReadOnlyList<FabricationStepInput> Steps, uint Version);

/// <summary>"Trocar roteiro" (CA 9).</summary>
public sealed record FabricationRoutingInput(Guid RoutingTemplateId, uint Version);

/// <summary>Roteiro ativo para o "Trocar roteiro" (CA 9), com o impacto e a tag "Sugerido" (CA 4, padrão 6).</summary>
public sealed record FabricationRoutingOption(Guid Id, string Name, string? ProductType, int StepCount, decimal TotalStandardHours, bool Suggested, bool HasAllWorkCenters);

public sealed record FabricationCancelInput(string? Reason, uint Version);

public sealed record FabricationStepView(
    Guid Id, int Sequence, string Code, string Name, Guid? WorkCenterId, string? WorkCenterName, string? Line, decimal StandardHours, ProductionStepStatus Status, bool IsQualityStep,
    bool RequiresApproval, IReadOnlyList<string> Checklist, decimal AppliedHours = 0m, bool AwaitingApproval = false, bool SelfApproved = false, bool SelfInspection = false);

/// <summary>
/// OS de fabricação (CA 10): cabeçalho (número, situação, projeto, cliente, produto, quantidade), fatos (prometido, horas padrão,
/// etapa atual) e as etapas. Sem custo.
/// </summary>
public sealed record FabricationOrderDetail(
    Guid Id, string Number, ProductionOrderStatus Status, Guid ProjectId, string ProjectNumber, string ProjectName, Guid PartyId, string PartyName, DateOnly? PromisedDate,
    Guid ProjectItemId, string ItemDescription, Guid? ProductId, string? ProductCode, string? ProductName, decimal Quantity, int Priority, Guid? RoutingTemplateId,
    string? RoutingTemplateName, IReadOnlyList<FabricationStepView> Steps, decimal TotalStandardHours, int CurrentStepIndex, bool Started, int? DelayDays, string? CancelReason,
    DateTimeOffset? CancelledAt, DateTimeOffset CreatedAt, uint Version, int QualityAttempt = 1, bool SelfInspection = false, bool ReadyToComplete = false, int OpenNonConformities = 0,
    DateTimeOffset? CompletedAt = null, string? CompletedByName = null, string? CompletionBlocker = null, DateTimeOffset? CompletionReversedAt = null,
    string? CompletionReversedByName = null, string? CompletionReversalReason = null);

/// <summary>Linha da lista (CA 15): etapa atual pelas letras, prometido com atraso e o chip "Sem roteiro".</summary>
public sealed record FabricationOrderSummary(
    Guid Id, string Number, ProductionOrderStatus Status, Guid ProjectId, string ProjectNumber, string ProjectName, Guid PartyId, string PartyName, Guid ProjectItemId,
    string ItemDescription, string? ProductName, decimal Quantity, DateOnly? PromisedDate, int? DelayDays, IReadOnlyList<string> Codes, IReadOnlyList<string> StepNames,
    int CurrentStepIndex, bool NoRouting);

public sealed record FabricationOrderPage(IReadOnlyList<FabricationOrderSummary> Items, int Page, int PageSize, int Total);

/// <summary>Filtros da lista na URL (CA 15).</summary>
public sealed record FabricationListQuery(Guid? ProjectId, Guid? PartyId, string? Line, string? Situation, bool Late);