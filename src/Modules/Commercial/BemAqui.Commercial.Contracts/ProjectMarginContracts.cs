using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Contracts;

/// <summary>De onde vem o imposto projetado (DSH-02 CA 5): "Imposto orçado" (sem nota) ou "Imposto das notas + orçado no restante".</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProjectTaxBasis>))]
public enum ProjectTaxBasis
{
    Budgeted,
    Invoices,
}

/// <summary>
/// Uma natureza de custo do projeto (DSH-02 CA 1; ADR 0025 §1): orçado, realizado, comprometido, projetado e desvio (valor e % do orçado,
/// 4 casas; orçado 0 → null). <c>IncludesBackfill</c>: parte do realizado foi recomposta na implantação ("Calculado na implantação").
/// </summary>
public sealed record ProjectMarginLine(
    CostType CostType, decimal Budgeted, decimal Realized, decimal Committed, decimal Projected, decimal Deviation, decimal? DeviationPct, bool IncludesBackfill);

/// <summary>
/// Números de um projeto ou do consolidado (DSH-02 CA 1, 6 e 10), só com <c>projects.view_cost</c>: as naturezas, o total, a receita
/// contratada (contrato + aditivos aprovados) e as margens orçada, realizada e projetada (valor e %, 4 casas; receita 0 → null), o desvio da
/// margem em pontos percentuais, a base do imposto projetado e as guias de imposto com o projeto (fora do custo, CA 3).
/// </summary>
public sealed record ProjectMarginFigures(
    IReadOnlyList<ProjectMarginLine> Lines, ProjectMarginLine Total, decimal Revenue, decimal BudgetedMargin, decimal? BudgetedMarginPct, decimal RealizedMargin,
    decimal? RealizedMarginPct, decimal ProjectedMargin, decimal? ProjectedMarginPct, decimal? MarginDeviationPp, ProjectTaxBasis TaxBasis, decimal TaxGuides);

/// <summary>Projeto no relatório, sem custo (DSH-02 CA 9 e 19): número, nome, cliente, situação, responsável e assinatura.</summary>
public sealed record ProjectMarginHeader(
    Guid Id, string Number, string Name, Guid PartyId, string PartyName, string Status, Guid? ManagerUserId, string? ManagerName, Guid ContractId, DateOnly SignedAt);

/// <summary>
/// Projeto com os números (DSH-02 CA 9 e 11): o cabeçalho sem custo, os números e a natureza que mais desviou (para o chip "Margem abaixo"
/// levar à aba Custos com ela destacada; null sem desvio positivo).
/// </summary>
public sealed record ProjectMarginRow(ProjectMarginHeader Project, ProjectMarginFigures Figures, CostType? WorstCostType);

/// <summary>Consolidado dos projetos filtrados (DSH-02 CA 10): quantos, os números somados e a natureza que mais desviou.</summary>
public sealed record ProjectMarginPortfolio(int ProjectCount, ProjectMarginFigures Figures, CostType? WorstCostType);

/// <summary>
/// Filtro do relatório (DSH-02 CA 9 e 11): situação (<c>andamento</c>, o padrão: <c>Planned</c>, <c>InProgress</c>, <c>Fabricated</c>,
/// <c>Installing</c>; <c>entregue</c>, <c>encerrado</c>, <c>concluido</c> = entregues e encerrados, <c>cancelado</c>, <c>todos</c>), cliente,
/// responsável, período de assinatura, "Só com margem abaixo da orçada" e o período da entrega (DSH-01b: os encerrados no ano do card Margem;
/// sem data de entrega, vale a do encerramento).
/// </summary>
public sealed record ProjectMarginFilter(
    string? Situation = null, Guid? PartyId = null, Guid? ManagerId = null, DateOnly? SignedFrom = null, DateOnly? SignedTo = null, bool BelowBudgetOnly = false,
    DateOnly? DeliveredFrom = null, DateOnly? DeliveredTo = null);

/// <summary>Consolidado e os piores projetos pelo desvio da margem (DSH-01b, card Margem), de um cálculo só: o mesmo do relatório com o filtro.</summary>
public sealed record ProjectMarginSummary(ProjectMarginPortfolio Portfolio, IReadOnlyList<ProjectMarginRow> Worst);

/// <summary>
/// Custo realizado de um projeto num mês e numa natureza (DSH-01b, resultado gerencial por projeto): a mesma regra do realizado do DSH-02, pela
/// data de cada lançamento (baixa, devolução e horas pela data da razão, imposto pela data da nota, título confirmado pela emissão).
/// </summary>
public sealed record ProjectRealizedByPeriod(Guid ProjectId, int Year, int Month, CostType CostType, decimal Amount);

/// <summary>
/// Lançamento do realizado num período (DSH-01b CA 8): tipo (o nome de <c>ProjectCostEntryKind</c> do DSH-02), natureza, documento, data, valor
/// com sinal, horas (agregadas por OS e centro, sem nome) e o link só com a permissão do módulo dono do documento.
/// </summary>
public sealed record ProjectRealizedEntry(string Kind, CostType CostType, string Document, DateOnly? Date, decimal Amount, decimal? Hours, string? Link);

/// <summary>
/// Orçado × realizado e margem para os outros módulos (DSH-02 CA 1; painel do dono, DSH-01a/01b): um cálculo só, o mesmo da aba Custos e do
/// relatório. Sem <c>projects.view_cost</c>, 403 <c>forbidden</c> (CA 19).
/// </summary>
public interface IProjectMargins
{
    Task<Result<ProjectMarginRow>> ForProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<ProjectMarginRow>>> ForProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default);

    Task<Result<ProjectMarginPortfolio>> PortfolioAsync(ProjectMarginFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Consolidado do filtro e os <paramref name="worst"/> piores pelo desvio da margem, na ordem do relatório (DSH-01b, card Margem).</summary>
    Task<Result<ProjectMarginSummary>> SummaryAsync(ProjectMarginFilter filter, int worst, CancellationToken cancellationToken = default);

    /// <summary>
    /// Realizado por projeto, mês e natureza de <paramref name="from"/> a <paramref name="to"/> (DSH-01b CA 6): só os valores diferentes de zero;
    /// somado em todos os meses, é o realizado do DSH-02. Não inclui comprometido nem projetado.
    /// </summary>
    Task<Result<IReadOnlyList<ProjectRealizedByPeriod>>> RealizedByPeriodAsync(IReadOnlyCollection<Guid> projectIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Lançamentos do realizado do projeto no período (uma natureza ou todas), com o total igual ao do <see cref="RealizedByPeriodAsync"/> (DSH-01b CA 8).</summary>
    Task<Result<IReadOnlyList<ProjectRealizedEntry>>> RealizedEntriesAsync(Guid projectId, CostType? costType, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
