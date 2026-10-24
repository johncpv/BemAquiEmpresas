using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Production.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<ProductionOrderType>))]
public enum ProductionOrderType
{
    Installation,
    Fabrication,
}

/// <summary>
/// Situação da OS (04-fluxos §1). A instalação usa Open → InProgress → Completed e Cancelled; a fabricação também Paused
/// (PRD-03a), Quality (PRD-05) e ReadyToComplete (PRD-06a1, D12).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProductionOrderStatus>))]
public enum ProductionOrderStatus
{
    Open,
    InProgress,
    Completed,
    Cancelled,
    Paused,
    Quality,

    /// <summary>"Pronta para concluir" (PRD-06a1 CA 1, D12): todas as etapas <c>Done</c>; falta o supervisor concluir a OS.</summary>
    ReadyToComplete,
}

/// <summary>Situação da etapa da OS de fabricação (04-fluxos §1), em sequência: a primeira nasce Ready e as demais Waiting.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProductionStepStatus>))]
public enum ProductionStepStatus
{
    Waiting,
    Ready,
    InProgress,
    Paused,
    Done,
    Rejected,
}

[JsonConverter(typeof(JsonStringEnumConverter<ChecklistResult>))]
public enum ChecklistResult
{
    Done,
    NotApplicable,

    /// <summary>"Não conforme" (só na etapa de qualidade, PRD-05 CA 6).</summary>
    NonConforming,
}

/// <summary>
/// Ordem de serviço (<c>production.production_orders</c>), de instalação (COM-06) ou de fabricação (PRD-01), na mesma tabela com
/// um <c>CHECK</c> por tipo: a instalação tem instalador, valor combinado e datas previstas; a fabricação tem item do projeto,
/// produto e quantidade, sem instalador nem valor.
/// </summary>
[DocumentName("A OS")]
public sealed class ProductionOrder : Entity, IAuditable, IConcurrencyVersioned
{
    public string Number { get; set; } = string.Empty;

    public ProductionOrderType Type { get; init; }

    public Guid ProjectId { get; init; }

    public Guid ContractId { get; init; }

    /// <summary>Instalador (terceiro com papel Instalador); só na instalação.</summary>
    public Guid? InstallerId { get; set; }

    /// <summary>Valor combinado com o instalador; só na instalação.</summary>
    public decimal? ContractValue { get; set; }

    /// <summary>Previsão: obrigatória na instalação; na fabricação nasce vazia (agendamento da PLN-01).</summary>
    public DateOnly? PlannedStart { get; set; }

    public DateOnly? PlannedEnd { get; set; }

    public ProductionOrderStatus Status { get; set; } = ProductionOrderStatus.Open;

    public DateOnly? StartedOn { get; set; }

    public DateOnly? AcceptedOn { get; set; }

    /// <summary>Quem aceitou pelo cliente.</summary>
    [PersonalData]
    public string? AcceptedByName { get; set; }

    /// <summary>Item do projeto fabricado (PRD-01): uma OS de fabricação não cancelada por item.</summary>
    public Guid? ProjectItemId { get; init; }

    /// <summary>
    /// Produto (item do estoque) do item do projeto, quando houver. PRD-06a2 CA 1: a conclusão exige o produto; a OS sem ele ganha o item
    /// pelo "Escolher item do produto" (<c>production.edit</c>) enquanto não está concluída nem cancelada.
    /// </summary>
    public Guid? ProductId { get; set; }

    /// <summary>Quantidade do item do projeto (D14: uma OS por item, com a quantidade do item).</summary>
    public decimal? Quantity { get; init; }

    /// <summary>Prioridade na fila (0 ao nascer; arrastável na PLN-02).</summary>
    public int Priority { get; set; }

    /// <summary>Roteiro de onde as etapas foram copiadas (só informativo: nada aponta para a etapa do template).</summary>
    public Guid? RoutingTemplateId { get; set; }

    public DateTimeOffset? ActualStart { get; set; }

    /// <summary>Fim real: a conclusão da OS (PRD-06a1 CA 3); o estorno da conclusão limpa.</summary>
    public DateTimeOffset? ActualEnd { get; set; }

    /// <summary>Quem concluiu a OS de fabricação (PRD-06a1 CA 3); o estorno limpa.</summary>
    public Guid? CompletedBy { get; set; }

    /// <summary>Último estorno da conclusão (PRD-06a1 CA 10): quando, quem e o motivo (até 500). O histórico da OS tem todos.</summary>
    public DateTimeOffset? CompletionReversedAt { get; set; }

    public Guid? CompletionReversedBy { get; set; }

    public string? CompletionReversalReason { get; set; }

    /// <summary>
    /// Entrada do produto acabado no estoque (PRD-06a2 CA 5): o movimento <c>Production</c> lançado na conclusão; o estorno da conclusão
    /// lança o inverso e limpa (CA 10).
    /// </summary>
    public Guid? EntryMovementId { get; set; }

    /// <summary>
    /// "Concluída antes do controle de estoque" (PRD-06a2 CA 9): a OS concluída pela PRD-06a1 (migration) ou com o módulo Estoque não
    /// liberado no tenant, sem entrada no estoque nem séries. O estorno limpa: a reconclusão segue a regra do momento.
    /// </summary>
    public bool CompletedWithoutStock { get; set; }

    public string? CancelReason { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public Guid? CancelledBy { get; set; }

    /// <summary>Tentativa atual da qualidade (PRD-05): 1 no início da OS, + 1 a cada reprovação.</summary>
    public int QualityAttempt { get; set; } = 1;

    /// <summary>Início da tentativa atual (a última reprovação); nulo na tentativa 1, que começa com a OS (segregação, CA 5).</summary>
    public DateTimeOffset? QualityAttemptStartedAt { get; set; }

    /// <summary>"Autoinspeção" (CA 5): alguma qualidade da OS foi aprovada por quem fez parte do trabalho, sem outro aprovador.</summary>
    public bool SelfInspection { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public uint Version { get; set; }
}

/// <summary>
/// Etapa da OS (<c>production.production_order_steps</c>). Na instalação, sem letra, centro, horas nem situação; na fabricação,
/// copiada do roteiro e editável enquanto nenhuma etapa começou (PRD-01 CA 7).
/// </summary>
public sealed class ProductionOrderStep : Entity, IAuditable
{
    public Guid OrderId { get; init; }

    public int Sequence { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Letra da etapa (A a Z), copiada do roteiro: coluna do Kanban e display de andar.</summary>
    public string? Code { get; set; }

    public Guid? WorkCenterId { get; set; }

    public decimal? StandardHours { get; set; }

    public ProductionStepStatus? Status { get; set; }

    public bool IsQualityStep { get; set; }

    public bool RequiresApproval { get; set; }

    public DateOnly? PlannedStart { get; set; }

    public DateOnly? PlannedEnd { get; set; }

    public DateTimeOffset? ActualStart { get; set; }

    public DateTimeOffset? ActualEnd { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public Guid? CompletedBy { get; set; }

    /// <summary>"Concluir etapa" do supervisor no Kanban (PRD-02 CA 8, D10): concluída sem apontamento, com o motivo.</summary>
    public bool CompletedBySupervisor { get; set; }

    /// <summary>Motivo da conclusão pelo supervisor (até 200).</summary>
    public string? CompletionReason { get; set; }

    /// <summary>
    /// "Aguardando aprovação" (PRD-03a CA 15): a etapa que exige aprovação foi finalizada pelo operador e espera quem tem
    /// <c>production.approve</c>; continua <c>InProgress</c>, sem apontamento aberto.
    /// </summary>
    public DateTimeOffset? AwaitingApprovalSince { get; set; }

    /// <summary>Aprovada ou concluída por quem apontou, sem outro aprovador elegível (CA 21, "Autoaprovado").</summary>
    public bool SelfApproved { get; set; }

    /// <summary>
    /// Tentativa do checklist da etapa (PRD-05 CA 7): 1 ao nascer; a etapa de qualidade devolvida ganha a tentativa nova da OS e um
    /// checklist zerado, e os itens das tentativas anteriores ficam guardados.
    /// </summary>
    public int Attempt { get; set; } = 1;

    /// <summary>Última decisão da etapa de qualidade (o histórico de cada tentativa está em <see cref="QualityDecision"/>).</summary>
    public Guid? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>"Autoinspeção" na última aprovação da etapa de qualidade (CA 5).</summary>
    public bool SelfInspection { get; set; }
}

/// <summary>
/// Item de checklist (<c>production.production_order_checklist</c>): o da entrega técnica na instalação (sem etapa) e o de cada
/// etapa na fabricação (<c>StepId</c>), linha a linha, com o resultado.
/// </summary>
public sealed class ProductionOrderChecklistItem : Entity, IAuditable
{
    public Guid OrderId { get; init; }

    /// <summary>Etapa da OS de fabricação; nulo no checklist de entrega técnica da instalação.</summary>
    public Guid? StepId { get; init; }

    public int Sequence { get; set; }

    public string Text { get; set; } = string.Empty;

    public ChecklistResult? Result { get; set; }

    public DateTimeOffset? MarkedAt { get; set; }

    public Guid? MarkedBy { get; set; }

    /// <summary>Tentativa do item (PRD-05 CA 7): o checklist atual da etapa é o da tentativa da etapa (<see cref="ProductionOrderStep.Attempt"/>).</summary>
    public int Attempt { get; set; } = 1;
}

/// <summary>Item do projeto coberto pela OS de instalação (<c>production.production_order_project_items</c>).</summary>
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