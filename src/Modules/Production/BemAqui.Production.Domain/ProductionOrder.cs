using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Production.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<ProductionOrderType>))]
public enum ProductionOrderType
{
    Installation,
    Fabrication,
}

/// <summary>Situação da OS (04-fluxos §1); a instalação usa Open → InProgress → Completed e Cancelled.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProductionOrderStatus>))]
public enum ProductionOrderStatus
{
    Open,
    InProgress,
    Completed,
    Cancelled,
}

[JsonConverter(typeof(JsonStringEnumConverter<ChecklistResult>))]
public enum ChecklistResult
{
    Done,
    NotApplicable,
}

/// <summary>
/// Ordem de serviço (<c>production.production_orders</c>). Nesta fase só a de instalação (COM-06): o executor é o instalador
/// terceirizado, com o valor combinado virando título a pagar previsto até o aceite técnico.
/// </summary>
[DocumentName("A OS")]
public sealed class ProductionOrder : Entity, IAuditable, IConcurrencyVersioned
{
    public string Number { get; set; } = string.Empty;

    public ProductionOrderType Type { get; init; }

    public Guid ProjectId { get; init; }

    public Guid ContractId { get; init; }

    /// <summary>Instalador (terceiro com papel Instalador).</summary>
    public Guid InstallerId { get; set; }

    public decimal ContractValue { get; set; }

    public DateOnly PlannedStart { get; set; }

    public DateOnly PlannedEnd { get; set; }

    public ProductionOrderStatus Status { get; set; } = ProductionOrderStatus.Open;

    public DateOnly? StartedOn { get; set; }

    public DateOnly? AcceptedOn { get; set; }

    /// <summary>Quem aceitou pelo cliente.</summary>
    [PersonalData]
    public string? AcceptedByName { get; set; }

    public string? CancelReason { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public Guid? CancelledBy { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public uint Version { get; set; }
}

/// <summary>Etapa da OS (<c>production.production_order_steps</c>); na instalação, sem centro e sem apontamento.</summary>
public sealed class ProductionOrderStep : Entity, IAuditable
{
    public Guid OrderId { get; init; }

    public int Sequence { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid? WorkCenterId { get; init; }

    public DateTimeOffset? CompletedAt { get; set; }

    public Guid? CompletedBy { get; set; }
}

/// <summary>Item do checklist de entrega técnica (<c>production.production_order_checklist</c>), com o resultado.</summary>
public sealed class ProductionOrderChecklistItem : Entity, IAuditable
{
    public Guid OrderId { get; init; }

    public int Sequence { get; set; }

    public string Text { get; set; } = string.Empty;

    public ChecklistResult? Result { get; set; }

    public DateTimeOffset? MarkedAt { get; set; }

    public Guid? MarkedBy { get; set; }
}

/// <summary>Item do projeto coberto pela OS (<c>production.production_order_project_items</c>).</summary>
public sealed class ProductionOrderProjectItem : Entity
{
    public Guid OrderId { get; init; }

    public Guid ProjectItemId { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<TemplateLineKind>))]
public enum TemplateLineKind
{
    Step,
    Checklist,
}

/// <summary>
/// Modelo da OS de instalação do tenant (<c>production.installation_template</c>, P-22): etapas e itens do checklist. Mudar
/// o modelo não altera as OS já criadas.
/// </summary>
public sealed class InstallationTemplateLine : Entity, IAuditable
{
    public TemplateLineKind Kind { get; init; }

    public int Sequence { get; set; }

    public string Text { get; set; } = string.Empty;
}
