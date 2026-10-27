using BemAqui.SharedKernel;

namespace BemAqui.Dashboard.Application;

// Resultado gerencial por projeto (DSH-01b CA 6 a 9, ADR 0028 §1): receita do período (IRevenueSummary) − custos diretos realizados com data
// no período (IProjectMargins.RealizedByPeriodAsync), sem rateio. Só com projects.view_cost e finance.view: todos os tipos levam custo.

/// <summary>Mês do período (<c>aaaa-mm</c>) e se a receita dele ainda é "Provisório" (pelos títulos, antes das notas reais).</summary>
[CostDto]
public sealed record ProjectResultMonth(string Month, bool Provisional);

/// <summary>Custos diretos de uma natureza.</summary>
[CostDto]
public sealed record ProjectResultCostAmount(CostType CostType, decimal Amount);

/// <summary>
/// Linha da lista (CA 6): projeto, cliente, situação, receita do período, custos diretos por natureza e no total, resultado = receita − custos
/// diretos e a margem % (1 casa; receita 0 → null).
/// </summary>
[CostDto]
public sealed record ProjectResultRow(
    Guid ProjectId, string Number, string Name, Guid PartyId, string PartyName, string Status, decimal Revenue, IReadOnlyList<ProjectResultCostAmount> Costs, decimal DirectCosts,
    decimal Result, decimal? MarginPct);

/// <summary>Somas da lista inteira (todas as páginas).</summary>
[CostDto]
public sealed record ProjectResultTotals(int Projects, decimal Revenue, IReadOnlyList<ProjectResultCostAmount> Costs, decimal DirectCosts, decimal Result, decimal? MarginPct);

/// <summary>
/// "Resultado por projeto" (CA 6): o período, os meses com a marca de provisório, a página ordenada (padrão: pior resultado primeiro), o
/// total de linhas e as somas. Só os projetos com receita ou custo no período.
/// </summary>
[CostDto]
public sealed record ProjectResultsPage(
    string From, string To, IReadOnlyList<ProjectResultMonth> Months, bool Provisional, IReadOnlyList<ProjectResultRow> Items, int Page, int PageSize, int Total,
    ProjectResultTotals Totals, DateTimeOffset UpdatedAt);

/// <summary>
/// Linha da visão mês a mês (CA 7): <c>revenue</c>, uma natureza de custo (nome de <see cref="CostType"/>), <c>costs</c> (= custos diretos) ou
/// <c>result</c>; um valor por mês, o total e se a célula abre o drill-down.
/// </summary>
[CostDto]
public sealed record ProjectResultLine(string Key, string Label, string Kind, IReadOnlyList<decimal> Values, decimal Total, bool Drillable);

/// <summary>Visão mês a mês de um projeto (CA 7): cabeçalho, meses, as linhas Receita, (−) custos por natureza, custos diretos e = Resultado, e a margem.</summary>
[CostDto]
public sealed record ProjectResultDetail(
    Guid ProjectId, string Number, string Name, string PartyName, string Status, string From, string To, IReadOnlyList<ProjectResultMonth> Months,
    IReadOnlyList<ProjectResultLine> Lines, decimal? MarginPct, DateTimeOffset UpdatedAt);

/// <summary>
/// Documento da célula (CA 8): tipo (<c>Receivable</c>, <c>DownPayment</c> na receita; os do drill-down do DSH-02 nos custos), documento, data,
/// valor, horas (agregadas por OS e centro, sem nome) e o link do documento de origem quando a permissão deixa.
/// </summary>
[CostDto]
public sealed record ProjectResultEntry(string Kind, string Document, DateOnly? Date, decimal Amount, decimal? Hours, string? Note, string? Link);

/// <summary>Drill-down de uma célula (CA 8): a linha, o mês (nulo = o período todo), o total (igual ao da célula) e os documentos.</summary>
[CostDto]
public sealed record ProjectResultCell(string Line, string Label, string? Month, decimal Total, IReadOnlyList<ProjectResultEntry> Entries);
