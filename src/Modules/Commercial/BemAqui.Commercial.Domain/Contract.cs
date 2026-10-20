using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<ContractStatus>))]
public enum ContractStatus
{
    Active,
    Completed,
    Cancelled,
}

/// <summary>
/// Contrato (<c>commercial.contracts</c>, COM-04a): nasce da aprovação do orçamento, com o snapshot congelado (muda só por
/// aditivo, COM-07). O cliente vai no snapshot só por id e nome; o resto vem do <c>IParties</c> na hora de exibir.
/// </summary>
[DocumentName("O contrato")]
public sealed class Contract : Entity, IAuditable, IConcurrencyVersioned
{
    public string Number { get; set; } = string.Empty;

    public Guid QuoteId { get; init; }

    public Guid PartyId { get; init; }

    public DateOnly SignedAt { get; init; }

    public decimal TotalValue { get; init; }

    public Guid RevenueAccountId { get; init; }

    public ContractStatus Status { get; set; } = ContractStatus.Active;

    /// <summary>JSON do orçamento aprovado (versão <c>v1</c>), sem dado pessoal; o histórico registra só a criação.</summary>
    [AuditOnCreateOnly]
    public string Snapshot { get; init; } = "{}";

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public uint Version { get; set; }
}

/// <summary>Marco do contrato (<c>commercial.contract_milestones</c>): copiado do orçamento, com o valor congelado.</summary>
public sealed class ContractMilestone : Entity, IAuditable
{
    public Guid ContractId { get; init; }

    public int Sequence { get; init; }

    public string Description { get; init; } = string.Empty;

    public decimal Percent { get; init; }

    public decimal Amount { get; init; }

    public MilestoneTrigger Trigger { get; init; }

    public TriggerEvent? TriggerEvent { get; init; }

    public DateOnly? DueDate { get; init; }

    public int? DaysAfterEvent { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<ProjectStatus>))]
public enum ProjectStatus
{
    Planned,
    InProgress,
    Fabricated,
    Installing,
    Delivered,
    Closed,
    Cancelled,
}

/// <summary>Projeto (<c>commercial.projects</c>, COM-04a): o que a fábrica entrega do contrato; etapas P E F I T C.</summary>
[DocumentName("O projeto")]
public sealed class Project : Entity, IAuditable, IConcurrencyVersioned
{
    public string Number { get; set; } = string.Empty;

    public Guid ContractId { get; init; }

    public Guid PartyId { get; init; }

    public string Name { get; set; } = string.Empty;

    public Guid? SiteAddressId { get; set; }

    public DateOnly? PromisedDate { get; set; }

    public DateOnly? ForecastDate { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Planned;

    public int? Priority { get; set; }

    public Guid? ManagerUserId { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public uint Version { get; set; }
}

/// <summary>Item do projeto (<c>commercial.project_items</c>): o item do orçamento que a produção vai fabricar (PRD-01).</summary>
public sealed class ProjectItem : Entity, IAuditable
{
    public Guid ProjectId { get; init; }

    public Guid QuoteItemId { get; init; }

    public int Sequence { get; init; }

    public string Description { get; init; } = string.Empty;

    public decimal Quantity { get; init; }

    public Guid? ItemId { get; init; }
}

/// <summary>Orçado por natureza (<c>commercial.project_budget</c>): base do orçado × realizado; só com <c>projects.view_cost</c>.</summary>
public sealed class ProjectBudget : Entity, IAuditable
{
    public Guid ProjectId { get; init; }

    public CostType CostType { get; init; }

    [ProtectedSecret]
    public decimal BudgetedAmount { get; set; }
}
