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

    /// <summary>Contrato que já estava em andamento antes do sistema (COM-04c).</summary>
    public bool IsOnboarding { get; init; }

    /// <summary>Motivo do cancelamento (COM-04b CA 12), até 500 caracteres; não vai no evento.</summary>
    public string? CancelReason { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public Guid? CancelledBy { get; set; }

    /// <summary>Projeto entregue e parcelas quitadas (COM-04b CA 14); volta a nulo se uma baixa for estornada (CA 15).</summary>
    public DateTimeOffset? CompletedAt { get; set; }

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

    /// <summary>Recebido antes da implantação (COM-04c CA 2): o valor já está no saldo de corte; não gera título.</summary>
    public bool ReceivedBeforeOnboarding { get; init; }
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

    /// <summary>Fabricação concluída (COM-04b CA 9; PRD-06 automático).</summary>
    public DateOnly? FabricatedOn { get; set; }

    /// <summary>
    /// "Fabricado" pelas OS (PRD-06a1 CA 7): o projeto passou a fabricado quando a última OS de fabricação não cancelada concluiu. O
    /// estorno da conclusão só volta o projeto a <c>InProgress</c> quando é true (o registro manual da COM-04b fica false).
    /// </summary>
    public bool FabricatedByOrders { get; set; }

    public DateOnly? DeliveredOn { get; set; }

    /// <summary>Entrega registrada à mão, venda sem instalação (COM-04b CA 10, D15).</summary>
    public bool DeliveredManually { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>
    /// Última edição direta do custo orçado (COM-09): gravada no projeto para a edição conferir e mudar a versão do documento; o que mudou,
    /// quem e o motivo ficam em <see cref="ProjectBudgetRevision"/>.
    /// </summary>
    public DateTimeOffset? BudgetRevisedAt { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public uint Version { get; set; }

    /// <summary>Tipo do projeto (EXE-01a CA 1 a 4): vale o do projeto, não o do snapshot.</summary>
    public ExecutionKind ExecutionKind { get; set; } = ExecutionKind.Fabrication;

    public ExecutionKindSource ExecutionKindSource { get; set; } = ExecutionKindSource.Default;

    /// <summary>Início da execução do projeto de serviço (EXE-01a CA 10; vindo de <c>Planned</c> na entrega, a data da entrega).</summary>
    public DateOnly? StartedOn { get; set; }

    /// <summary>Cópia, para leitura, da data do aceite vigente (<see cref="ProjectAcceptance"/>); nula sem aceite vigente.</summary>
    public DateOnly? AcceptedOn { get; set; }

    /// <summary>"Fabricação" de compatibilidade publicada na entrega do serviço com marco antigo <c>FabricationDone</c> (EXE-01a CA 12): uma vez só.</summary>
    public DateOnly? FabricationCompatOn { get; set; }

    /// <summary>Quem registrou a entrega (EXE-01a CA 14: segregação opcional entre entrega e aceite).</summary>
    public Guid? DeliveredBy { get; set; }

    /// <summary>Modelo de etapas copiado para o projeto (EXE-03a CA 1): id e revisão na hora da cópia; nulo = sem etapas do modelo.</summary>
    public Guid? StageTemplateId { get; set; }

    public int? StageTemplateRevision { get; set; }

    /// <summary>Nome do modelo na hora da cópia, para o histórico "Etapas do modelo {nome} (revisão {n})" (CA 4) não mudar se o modelo mudar.</summary>
    public string? StageTemplateName { get; set; }

    public DateTimeOffset? StagesAppliedAt { get; set; }

    public Guid? StagesAppliedBy { get; set; }

    /// <summary>Última edição das etapas (CA 5): gravada no projeto para a edição conferir e mudar a versão (xmin) do projeto.</summary>
    public DateTimeOffset? StagesChangedAt { get; set; }
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

    /// <summary>Roteiro herdado do item do orçamento (PRD-01 CA 4 e 22): primeira opção da sugestão da OS de fabricação.</summary>
    public Guid? RoutingTemplateId { get; init; }
}

/// <summary>Orçado por natureza (<c>commercial.project_budget</c>): base do orçado × realizado; só com <c>projects.view_cost</c>.</summary>
public sealed class ProjectBudget : Entity, IAuditable
{
    public Guid ProjectId { get; init; }

    public CostType CostType { get; init; }

    [ProtectedSecret]
    public decimal BudgetedAmount { get; set; }
}

/// <summary>
/// Edição direta do custo orçado (<c>commercial.project_budget_revisions</c>, COM-09): uma linha por natureza alterada, com o valor
/// anterior, o novo, o motivo, quem e quando. Só inclusão: a linha nunca é alterada nem apagada. É a trilha com os valores, porque a
/// auditoria geral registra o orçado só como "alterado" (sigilo comercial, D7); leitura só com <c>projects.view_cost</c>.
/// </summary>
public sealed class ProjectBudgetRevision : Entity, IAuditable
{
    public const int ReasonMaxLength = 500;

    public Guid ProjectId { get; init; }

    public CostType CostType { get; init; }

    [ProtectedSecret]
    public decimal PreviousAmount { get; init; }

    [ProtectedSecret]
    public decimal NewAmount { get; init; }

    /// <summary>Motivo da edição, obrigatório, até 500 caracteres.</summary>
    public string Reason { get; init; } = string.Empty;

    public DateTimeOffset ChangedAt { get; init; }

    public Guid? ChangedBy { get; init; }
}
