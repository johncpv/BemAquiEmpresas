using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Application;

// Projeto 360 (COM-05). Sem custo nos DTOs da lista e do resumo; custo só em ProjectCosts, com projects.view_cost (D7).

/// <summary>
/// Linha da lista de projetos (COM-05 CA 1; PLN-01b CA 8 e 9): a entrega prevista, a folga (prometido − previsto, dias corridos), o risco e o
/// motivo da previsão vazia saem da mesma função do Projeto 360 e do Kanban; o atraso também.
/// </summary>
public sealed record ProjectListRow(
    Guid Id, string Number, string Name, Guid PartyId, string PartyName, ProjectStatus Status, DateOnly? PromisedDate, int? DelayDays, decimal ContractValue, Guid? ManagerUserId,
    string? ManagerName, Guid ContractId, DateOnly? ForecastDate = null, int? SlackDays = null, DeliveryRisk Risk = DeliveryRisk.None, string? ForecastReason = null);

public sealed record ProjectListPage(IReadOnlyList<ProjectListRow> Items, int Page, int PageSize, int Total);

/// <summary>Margem orçada por projeto, para a coluna da lista (só com <c>projects.view_cost</c>).</summary>
public sealed record ProjectMargin(Guid Id, decimal? MarginPct);

/// <summary>Recebido por projeto, para a coluna da lista (só com <c>finance.view</c>).</summary>
public sealed record ProjectReceived(Guid Id, decimal Received, decimal ReceivedPct);

public sealed record ProjectUpdateInput(string Name, Guid? ManagerUserId, Guid? SiteAddressId, uint Version);

public sealed record ProjectReceivableView(Guid Id, string Number, string Description, ReceivableStatus Status, DateOnly? DueDate, decimal Amount, decimal Balance);

public sealed record ProjectPayableView(Guid Id, string Number, string PartyName, DocumentOrigin Origin, CostType? CostType, DateOnly? DueDate, decimal Amount, decimal Balance, PayableStatus Status);

/// <summary>Aba Financeiro (CA 6), só com <c>finance.view</c>.</summary>
public sealed record ProjectFinancial(
    IReadOnlyList<ProjectReceivableView> Receivables, IReadOnlyList<ProjectPayableView> Payables, decimal Received, decimal ToReceive, decimal Paid, decimal ToPay);

/// <summary>
/// Aba Custos (COM-05 CA 8, 9; PRD-04 CA 9; DSH-02 CA 1 a 6 e 12) e margens do Resumo, só com <c>projects.view_cost</c>: os mesmos números do
/// relatório "Orçado × realizado" (um cálculo só, <see cref="ProjectMarginCalculator"/>), mais a nota das NC (custo estimado só informativo).
/// </summary>
public sealed record ProjectCostView(
    IReadOnlyList<ProjectMarginLine> Lines, ProjectMarginLine Total, decimal Revenue, decimal? BudgetedMarginPct, decimal? ProjectedMarginPct, decimal BudgetedMargin,
    decimal RealizedMargin, decimal? RealizedMarginPct, decimal ProjectedMargin, decimal? MarginDeviationPp, ProjectTaxBasis TaxBasis, decimal TaxGuides, int NonConformityCount,
    decimal NonConformityCost);

/// <summary>Título a pagar com o projeto, como o cálculo do custo enxerga (somado pelo Finance, nunca copiado).</summary>
public sealed record CostPayable(CostType? CostType, DocumentOrigin Origin, PayableStatus Status, decimal Amount);

/// <summary>Soma das linhas da razão <c>commercial.project_costs</c> por natureza, fonte e marca de recomposição; <c>InvoiceAmount</c> só no imposto.</summary>
public sealed record CostLedgerLine(CostType CostType, ProjectCostSource Source, decimal Amount, bool IsBackfill, decimal InvoiceAmount = 0);

public sealed record ProjectTimelineEntry(DateTimeOffset At, string Kind, string Title, string? Who, string? Link);

/// <summary>
/// Ponte do Projeto 360 para o <see cref="ProjectMarginCalculator"/> (DSH-02 CA 1): classifica os títulos pela <see cref="PayableCostRules"/>
/// do Finance e devolve os números no formato do contrato. A aba Custos, o relatório e o <see cref="IProjectMargins"/> passam por aqui.
/// </summary>
public static class ProjectCosts
{
    public static ProjectMarginFigures Compute(decimal revenue, IReadOnlyDictionary<CostType, decimal> budget, IEnumerable<CostPayable> payables, IEnumerable<CostLedgerLine> ledger) =>
        Figures(ProjectMarginCalculator.Compute(revenue, budget, Totals(payables), Ledger(ledger)));

    /// <summary>Soma dos títulos por natureza e classe (a mesma regra do <c>IProjectPayableCosts.CostSummaryByProjectsAsync</c>).</summary>
    public static IReadOnlyList<PayableCostTotals> Totals(IEnumerable<CostPayable> payables)
    {
        ArgumentNullException.ThrowIfNull(payables);
        return payables
            .Select(p => (Type: p.CostType ?? CostType.Other, Bucket: PayableCostRules.Bucket(p.Origin, p.CostType, p.Status), p.Amount))
            .Where(p => p.Bucket is not null)
            .GroupBy(p => p.Type)
            .Select(g => new PayableCostTotals(
                g.Key, g.Where(p => p.Bucket == PayableCostBucket.Confirmed).Sum(p => p.Amount), g.Where(p => p.Bucket == PayableCostBucket.Planned).Sum(p => p.Amount),
                g.Where(p => p.Bucket == PayableCostBucket.StockPurchase).Sum(p => p.Amount), g.Where(p => p.Bucket == PayableCostBucket.TaxGuide).Sum(p => p.Amount)))
            .ToList();
    }

    public static IReadOnlyList<PayableCostTotals> Totals(IEnumerable<ProjectPayableCostTotals> totals)
    {
        ArgumentNullException.ThrowIfNull(totals);
        return totals.Select(t => new PayableCostTotals(t.CostType, t.Confirmed, t.Planned, t.StockPurchase, t.TaxGuide)).ToList();
    }

    public static IReadOnlyList<LedgerCostTotals> Ledger(IEnumerable<CostLedgerLine> ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        return ledger.Select(l => new LedgerCostTotals(l.CostType, l.Source, l.IsBackfill, l.Amount, l.InvoiceAmount)).ToList();
    }

    /// <summary>
    /// Título de compra de item estocável (ADR 0025 adendo): origem <c>Purchase</c> com natureza <c>Material</c>. Conta como
    /// comprometido até a baixa para a OS, confirmado ou não; as origens de compra são o previsto do pedido (<c>Purchase</c>, CMP-03a) e o
    /// título do recebimento (<c>GoodsReceipt</c>, CMP-04 CA 10, D2).
    /// </summary>
    public static bool IsStockPurchase(CostPayable payable) =>
        payable is not null && PayableCostRules.Bucket(payable.Origin, payable.CostType, payable.Status) == PayableCostBucket.StockPurchase;

    public static ProjectMarginFigures Figures(MarginResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ProjectMarginFigures(
            result.Lines.Select(Line).ToList(), Line(result.Total), result.Revenue, result.BudgetedMargin, result.BudgetedMarginPct, result.RealizedMargin, result.RealizedMarginPct,
            result.ProjectedMargin, result.ProjectedMarginPct, result.MarginDeviationPp, result.TaxBasis == TaxProjectionBasis.Invoices ? ProjectTaxBasis.Invoices : ProjectTaxBasis.Budgeted,
            result.TaxGuides);
    }

    /// <summary>Natureza que mais desviou acima do orçado (o chip "Margem abaixo" leva a ela); null sem desvio positivo.</summary>
    public static CostType? Worst(ProjectMarginFigures figures)
    {
        ArgumentNullException.ThrowIfNull(figures);
        return figures.Lines.Where(l => l.Deviation > 0).OrderByDescending(l => l.Deviation).Select(l => (CostType?)l.CostType).FirstOrDefault();
    }

    private static ProjectMarginLine Line(MarginLine l) =>
        new(l.CostType, l.Budgeted, l.Realized, l.Committed, l.Projected, l.Deviation, l.DeviationPct, l.IncludesBackfill);
}
