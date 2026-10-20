using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Contracts;

/// <summary>Projeto visto pelos outros módulos (COM-04a CA 21): Finance (COM-04b), Production (COM-06, PRD-01).</summary>
public sealed record ProjectSummary(Guid Id, string Number, string Name, Guid PartyId, Guid ContractId, string Status, Guid? SiteAddressId);

public interface IProjects
{
    Task<ProjectSummary?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectSummary>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>Projetos abertos para os seletores (título a pagar e a receber), com busca por número ou nome.</summary>
    Task<IReadOnlyList<ProjectSummary>> SearchAsync(string? term, int limit = 20, CancellationToken cancellationToken = default);
}

/// <summary>Marco do contrato no evento (sem dado pessoal).</summary>
public sealed record ContractMilestoneInfo(Guid MilestoneId, int Sequence, string Description, decimal Amount, MilestoneTrigger Trigger, TriggerEvent? TriggerEvent, DateOnly? DueDate, int? DaysAfterEvent);

/// <summary>Orçamento aprovado (COM-04a CA 5).</summary>
public sealed record QuoteApproved(Guid QuoteId, Guid ContractId) : IIntegrationEvent;

/// <summary>Contrato assinado (COM-04a CA 5): o Finance gera as parcelas a receber (COM-04b) com a conta de receita.</summary>
public sealed record ContractSigned(
    Guid ContractId, Guid ProjectId, Guid PartyId, DateOnly SignedAt, decimal TotalValue, Guid RevenueAccountId, IReadOnlyList<ContractMilestoneInfo> Milestones) : IIntegrationEvent;

/// <summary>Item do projeto criado (COM-04a CA 7), sem consumidor até a PRD-01.</summary>
public sealed record ProjectItemCreated(Guid ProjectId, Guid ProjectItemId, Guid QuoteItemId, Guid? ItemId, decimal Quantity) : IIntegrationEvent;

[JsonConverter(typeof(JsonStringEnumConverter<ContractState>))]
public enum ContractState
{
    Active,
    Completed,
    Cancelled,
}
