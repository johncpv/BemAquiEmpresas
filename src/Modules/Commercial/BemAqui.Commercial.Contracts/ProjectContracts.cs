using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Contracts;

/// <summary>
/// Projeto visto pelos outros módulos (COM-04a CA 21): Finance (COM-04b), Production (COM-06, PRD-01). <c>PromisedDate</c> é a
/// data prometida ao cliente (PRD-01 CA 22: atraso da OS de fabricação); <c>SignedAt</c> é a assinatura do contrato (PLN-01a CA 2: ordem
/// da fila antes da PLN-02, depois da data prometida).
/// </summary>
public sealed record ProjectSummary(
    Guid Id, string Number, string Name, Guid PartyId, Guid ContractId, string Status, Guid? SiteAddressId, Guid? ManagerUserId = null, DateOnly? PromisedDate = null,
    DateOnly? SignedAt = null);

public interface IProjects
{
    Task<ProjectSummary?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectSummary>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>Projetos abertos para os seletores (título a pagar e a receber), com busca por número ou nome.</summary>
    Task<IReadOnlyList<ProjectSummary>> SearchAsync(string? term, int limit = 20, CancellationToken cancellationToken = default);

    /// <summary>Itens do projeto (COM-06: a OS de instalação cobre itens; PRD-01: tipo, implantação e roteiro decidem a OS de fabricação).</summary>
    Task<IReadOnlyList<ProjectItemInfo>> ItemsAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Itens do projeto por id (PRD-01 CA 5 e 15); os que não existem ficam fora.</summary>
    Task<IReadOnlyList<ProjectItemInfo>> FindItemsAsync(IReadOnlyCollection<Guid> projectItemIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Itens que podem gerar OS de fabricação (PRD-01 CA 5 e 6): equipamento ou componente fabricado, de projeto planejado ou em
    /// produção, com contrato ativo que não é de implantação. Quem chama tira os que já têm OS.
    /// </summary>
    Task<IReadOnlyList<ProjectItemInfo>> FabricationCandidatesAsync(Guid? projectId, int limit, CancellationToken cancellationToken = default);

    /// <summary>Orçado de uma natureza (COM-06: valor padrão da OS e "Orçado" na OP). O chamador confere <c>projects.view_cost</c>.</summary>
    Task<decimal?> BudgetedAsync(Guid projectId, CostType costType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fabricação do projeto para a conclusão da OS (PRD-06a1 CA 2, 10 e 11): situação, se foi fabricado pelas OS, as parcelas com gatilho
    /// "fabricação concluída" (lidas por <c>IReceivables.ForContractAsync</c>) e o motivo que impede o estorno da conclusão (parcela já
    /// recebida, projeto além de <c>Fabricated</c>), conferido de forma síncrona antes de gravar. Null se o projeto não existe. O chamador
    /// confere <c>finance.view</c> antes de mostrar valor e vencimento.
    /// </summary>
    Task<ProjectFabricationInfo?> FabricationAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Projetos cuja data prevista o plano recalcula (PLN-01b CA 5 e 6): <c>Planned</c> e <c>InProgress</c>, com contrato não cancelado. A partir de
    /// <c>Fabricated</c> a previsão fica como estava e o risco passa a ser só da instalação.
    /// </summary>
    Task<IReadOnlyList<ProjectSummary>> PlanningCandidatesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProjectSummary>>([]);

    /// <summary>Orçado de uma natureza para vários projetos de uma vez (PLN-01b CA 5, caso b: custo de instalação no orçamento); sem custo na tela.</summary>
    async Task<IReadOnlyDictionary<Guid, decimal>> BudgetedManyAsync(IReadOnlyCollection<Guid> projectIds, CostType costType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectIds);
        var result = new Dictionary<Guid, decimal>();
        foreach (var id in projectIds)
        {
            if (await BudgetedAsync(id, costType, cancellationToken) is { } amount)
            {
                result[id] = amount;
            }
        }

        return result;
    }

    /// <summary>
    /// Previsão de entrega, folga e risco dos projetos pedidos (PLN-01b CA 11: o painel do dono na DSH-01b), lidos de
    /// <c>commercial.project_forecasts</c> e avaliados pela função única de risco.
    /// </summary>
    Task<IReadOnlyList<ProjectForecastSummary>> ForecastsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ProjectForecastSummary>>([]);
}

/// <summary>
/// Previsão do projeto vista pelos outros módulos (PLN-01b CA 11): prometido, entrega prevista, fim da fabricação, motivo (código) da
/// previsão vazia, folga em dias corridos (prometido − previsto), risco e atraso pela função única, e quando foi calculada.
/// </summary>
public sealed record ProjectForecastSummary(
    Guid ProjectId, DateOnly? PromisedDate, DateOnly? ForecastDate, DateOnly? FabricationEnd, string? Reason, int? SlackDays, DeliveryRisk Risk, int? DelayDays,
    DateTimeOffset? ComputedAt);

/// <summary>Parcela "fabricação concluída" do contrato (PRD-06a1 CA 2 e 11): o marco, o valor, o prazo e o título, se já existe.</summary>
public sealed record FabricationMilestoneInfo(
    Guid MilestoneId, string Description, decimal Amount, int DaysAfterEvent, Guid? ReceivableId, string? ReceivableNumber, bool Released, bool Received);

/// <summary>
/// Fabricação do projeto (PRD-06a1): <c>Status</c> é o nome de <c>ProjectStatus</c>; <c>ReversalBlocker</c> é a mensagem do 409 do estorno
/// da conclusão (CA 11) ou null quando o estorno pode seguir.
/// </summary>
public sealed record ProjectFabricationInfo(
    Guid ProjectId, string Number, string Status, DateOnly? FabricatedOn, bool FabricatedByOrders, IReadOnlyList<FabricationMilestoneInfo> Milestones, string? ReversalBlocker);

/// <summary>Tipo do item do projeto, copiado do item do orçamento (PRD-01, D14): só o equipamento gera OS de fabricação sozinho.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProjectItemKind>))]
public enum ProjectItemKind
{
    Equipment,
    Product,
    Service,
}

/// <summary>
/// Item do projeto (COM-06; PRD-01 CA 22): produto (<c>ItemId</c>), tipo, se o contrato é de implantação e o roteiro do item
/// (herdado do item do orçamento).
/// </summary>
public sealed record ProjectItemInfo(
    Guid Id, Guid ProjectId, int Sequence, string Description, decimal Quantity, Guid? ItemId, ProjectItemKind Kind, bool IsOnboarding, Guid? RoutingTemplateId);

/// <summary>Marco do contrato no evento (sem dado pessoal).</summary>
public sealed record ContractMilestoneInfo(Guid MilestoneId, int Sequence, string Description, decimal Amount, MilestoneTrigger Trigger, TriggerEvent? TriggerEvent, DateOnly? DueDate, int? DaysAfterEvent, decimal Percent = 0,
    bool ReceivedBeforeOnboarding = false);

/// <summary>Orçamento aprovado (COM-04a CA 5).</summary>
public sealed record QuoteApproved(Guid QuoteId, Guid ContractId) : IIntegrationEvent;

/// <summary>Contrato assinado (COM-04a CA 5): o Finance gera as parcelas a receber (COM-04b) com a conta de receita.</summary>
public sealed record ContractSigned(
    Guid ContractId, Guid ProjectId, Guid PartyId, DateOnly SignedAt, decimal TotalValue, Guid RevenueAccountId, IReadOnlyList<ContractMilestoneInfo> Milestones,
    bool IsOnboarding = false) : IIntegrationEvent;

/// <summary>
/// Item do projeto criado (COM-04a CA 7; PRD-01 CA 1 a 3, ADR 0023 §7): a Produção gera a OS de fabricação do equipamento. Os
/// campos novos têm valor padrão para as mensagens antigas do outbox: sem <c>Kind</c>, a OS não nasce sozinha.
/// </summary>
public sealed record ProjectItemCreated(
    Guid ProjectId, Guid ProjectItemId, Guid QuoteItemId, Guid? ItemId, decimal Quantity, ProjectItemKind? Kind = null, bool? IsOnboarding = null, Guid? RoutingTemplateId = null)
    : IIntegrationEvent;

[JsonConverter(typeof(JsonStringEnumConverter<ContractState>))]
public enum ContractState
{
    Active,
    Completed,
    Cancelled,
}

/// <summary>Marco do projeto atingido (COM-04b CA 6 a 11): o Finance libera as parcelas com esse gatilho.</summary>
public interface IProjectMilestoneReached : IIntegrationEvent
{
    Guid ProjectId { get; }

    Guid ContractId { get; }

    DateOnly OccurredOn { get; }

    TriggerEvent Event { get; }
}

/// <summary>Fabricação concluída (COM-04b CA 9: registro manual; PRD-06: automático).</summary>
public sealed record ProjectFabricated(Guid ProjectId, Guid ContractId, DateOnly OccurredOn) : IProjectMilestoneReached
{
    [JsonIgnore]
    public TriggerEvent Event => TriggerEvent.FabricationDone;
}

/// <summary>Entrega (COM-04b CA 10: venda sem instalação; COM-06: aceite técnico da OS).</summary>
public sealed record ProjectDelivered(Guid ProjectId, Guid ContractId, DateOnly OccurredOn) : IProjectMilestoneReached
{
    [JsonIgnore]
    public TriggerEvent Event => TriggerEvent.Delivered;
}

/// <summary>Aceite técnico (COM-04b CA 10; COM-06).</summary>
public sealed record TechnicalAcceptance(Guid ProjectId, Guid ContractId, DateOnly OccurredOn) : IProjectMilestoneReached
{
    [JsonIgnore]
    public TriggerEvent Event => TriggerEvent.TechnicalAcceptance;
}

/// <summary>Contrato cancelado (COM-04b CA 12), sem texto livre (ADR 0019 §19).</summary>
public sealed record ContractCancelled(Guid ContractId, Guid ProjectId) : IIntegrationEvent;

/// <summary>Redução de título pedida pelo aditivo (COM-07 CA 4, 7).</summary>
public sealed record AmendmentReduction(Guid ReceivableId, decimal NewAmount);

/// <summary>
/// Aditivo aprovado (COM-07 CA 6, 7): o Finance cria os títulos dos marcos do acréscimo (como o <see cref="ContractSigned"/>)
/// ou reduz os títulos indicados, revalidando cada um.
/// </summary>
public sealed record ContractAmended(
    Guid ContractId, Guid ProjectId, Guid AmendmentId, int AmendmentNumber, Guid PartyId, Guid RevenueAccountId, DateOnly ApprovedOn, decimal ValueDelta,
    IReadOnlyList<ContractMilestoneInfo> NewMilestones, IReadOnlyList<AmendmentReduction> Reductions) : IIntegrationEvent;
