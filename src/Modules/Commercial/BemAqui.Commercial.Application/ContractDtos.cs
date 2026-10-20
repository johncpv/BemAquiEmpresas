using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Application;

public sealed record ContractSummary(Guid Id, string Number, Guid PartyId, string PartyName, decimal TotalValue, DateOnly SignedAt, ContractStatus Status, Guid? ProjectId, string? ProjectNumber);

public sealed record ContractPage(IReadOnlyList<ContractSummary> Items, int Page, int PageSize, int Total);

public sealed record ContractMilestoneView(Guid Id, int Sequence, string Description, decimal Percent, decimal Amount, MilestoneTrigger Trigger, TriggerEvent? TriggerEvent, DateOnly? DueDate, int? DaysAfterEvent);

/// <summary>Contrato (CA 16): o cliente vem do cadastro na hora; itens e marcos do snapshot, sem custo para quem não tem <c>projects.view_cost</c>.</summary>
public sealed record ContractDetail(
    Guid Id, string Number, ContractStatus Status, Guid PartyId, string PartyName, string? PartyDocument, decimal TotalValue, DateOnly SignedAt, Guid RevenueAccountId,
    string? RevenueAccount, Guid QuoteId, string QuoteNumber, int QuoteRevision, Guid? ProjectId, string? ProjectNumber, string? ProjectName, DateTimeOffset CreatedAt,
    IReadOnlyList<ContractMilestoneView> Milestones, ContractSnapshot Snapshot, int Receivables, uint Version);

public sealed record ProjectItemView(Guid Id, int Sequence, string Description, decimal Quantity, Guid? ItemId, Guid QuoteItemId);

public sealed record ProjectDetail(
    Guid Id, string Number, string Name, ProjectStatus Status, Guid PartyId, string PartyName, Guid ContractId, string ContractNumber, Guid? SiteAddressId, string? SiteAddress,
    DateOnly? PromisedDate, DateOnly? ForecastDate, int? Priority, Guid? ManagerUserId, string? ManagerName, DateTimeOffset CreatedAt, IReadOnlyList<ProjectItemView> Items, uint Version);

/// <summary>Orçado por natureza (CA 8), só com <c>projects.view_cost</c>.</summary>
public sealed record ProjectBudgetLine(CostType CostType, decimal BudgetedAmount);
