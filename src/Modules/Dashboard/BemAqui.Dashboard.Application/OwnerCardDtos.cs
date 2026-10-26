using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Dashboard.Application;

// DTOs dos cards da DSH-01b, por forma de permissão (ADR 0028 §2): os cards sem custo (projetos em andamento, carteira, produção,
// entregas) não levam custo, orçado nem margem; o que leva fica em tipos marcados com [CostDto], servidos só pelas rotas com
// projects.view_cost (teste de arquitetura e de autorização).

/// <summary>
/// Marca o DTO do painel que leva custo, orçado ou margem (ADR 0028 §2, ADR 0019 §20): só sai por rota com <c>projects.view_cost</c>. O teste
/// de arquitetura confere que todo tipo do painel com custo tem a marca e que nenhum DTO sem a marca chega a um com ela.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CostDtoAttribute : Attribute;

/// <summary>Projeto em andamento no card (CA 1): etapa (nome de <c>ProjectStatus</c>), entrega prometida e prevista, folga, risco e contratado.</summary>
public sealed record ActiveProjectItem(
    Guid Id, string Number, string Name, string PartyName, string Status, DateOnly? PromisedDate, DateOnly? ForecastDate, int? SlackDays, DeliveryRisk Risk, int? DelayDays,
    decimal ContractedAmount);

/// <summary>"Projetos em andamento" (CA 1): quantos e até 8, os atrasados e em risco primeiro, sem margem nem recebido (rotas próprias).</summary>
public sealed record ActiveProjectsCard(int Total, IReadOnlyList<ActiveProjectItem> Items, DateTimeOffset UpdatedAt);

/// <summary>Recebido do projeto (com <c>finance.view</c>): Σ recebido e o % do contratado (1 casa; contratado 0 → null).</summary>
public sealed record ActiveProjectReceivedItem(Guid ProjectId, decimal Received, decimal? ReceivedPct);

/// <summary>Coluna "Margem" da tabela (com <c>projects.view_cost</c>): orçada → projetada (com custos reais) e o desvio em pp.</summary>
[CostDto]
public sealed record ActiveProjectMarginRow(Guid ProjectId, decimal? BudgetedMarginPct, decimal? ProjectedMarginPct, decimal? DeviationPp);

/// <summary>
/// Carteira (CA 3): a produzir = Σ receita contratada dos projetos <c>Planned</c> e <c>InProgress</c>; a faturar (só com <c>finance.view</c>) =
/// Σ contratada dos contratos <c>Active</c> − Σ receita já reconhecida desses projetos, com <see cref="Provisional"/> antes das notas reais.
/// </summary>
public sealed record BacklogCard(
    decimal ToProduce, int ToProduceCount, decimal? Contracted, decimal? Recognized, decimal? ToInvoice, int? ToInvoiceCount, bool Provisional, DateTimeOffset UpdatedAt);

/// <summary>Ocupação da linha "Fábrica" numa semana (PLN-04): horas comprometidas, capacidade, % (1 casa) e a faixa.</summary>
public sealed record ProductionWeekItem(DateOnly Start, DateOnly End, int IsoWeek, decimal CommittedHours, decimal CapacityHours, decimal? Percent, FactoryLoadBand Band);

/// <summary>
/// Produção (CA 1): projetos em andamento, OS em aberto, atrasadas e em risco (função única da PLN-01b, pelo quadro do Kanban) e a ocupação da
/// fábrica na semana atual e nas 4 seguintes, com o "ocupado até".
/// </summary>
public sealed record ProductionCard(
    int ActiveProjects, int OpenOrders, int LateOrders, int AtRiskOrders, IReadOnlyList<ProductionWeekItem> Weeks, DateOnly? BusyUntil, DateTimeOffset? PlanComputedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Projeto que vai atrasar (faixa "Atenção", CA 4): prometido, previsto e os dias de atraso previstos (previsto − prometido).</summary>
public sealed record DelayedProjectItem(Guid ProjectId, string Number, string Name, DateOnly? PromisedDate, DateOnly? ForecastDate, int DelayDays);

/// <summary>
/// Entregas do mês (CA 1): previstas no mês (data prevista no mês e não entregue), entregues no mês, em risco (prevista depois da prometida),
/// atrasadas (prometida já passou) e o projeto que mais vai atrasar.
/// </summary>
public sealed record DeliveriesCard(
    int Year, int Month, int ForecastInMonth, int DeliveredInMonth, int AtRisk, int Late, DelayedProjectItem? MostDelayed, DateTimeOffset UpdatedAt);

/// <summary>Projeto no ranking da margem (CA 1): orçada, projetada, desvio em pp e a natureza que mais desviou (o "Ver origem" vai a ela).</summary>
[CostDto]
public sealed record MarginProjectRow(
    Guid ProjectId, string Number, string Name, string PartyName, decimal? BudgetedMarginPct, decimal? ProjectedMarginPct, decimal? DeviationPp, CostType? WorstCostType);

/// <summary>
/// Margem (CA 1 e 2), só com <c>projects.view_cost</c>: projetada (com custos reais) dos projetos em andamento e realizada dos entregues ou
/// encerrados no ano, ponderadas pela receita (os números do relatório "Orçado × realizado" com o filtro), a orçada como contexto, a diferença
/// em pp e os 5 piores pelo desvio.
/// </summary>
[CostDto]
public sealed record MarginOverview(
    int Year, int ActiveCount, decimal ActiveRevenue, decimal? ProjectedMarginPct, decimal? BudgetedMarginPct, decimal? DeviationPp, int FinishedCount,
    decimal? FinishedRealizedMarginPct, decimal? FinishedBudgetedMarginPct, decimal? FinishedDeviationPp, IReadOnlyList<MarginProjectRow> Worst, DateTimeOffset UpdatedAt);
