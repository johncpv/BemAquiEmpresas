using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Application;

// Projeto 360 (COM-05). Sem custo nos DTOs da lista e do resumo; custo só em ProjectCosts, com projects.view_cost (D7).

public sealed record ProjectListRow(
    Guid Id, string Number, string Name, Guid PartyId, string PartyName, ProjectStatus Status, DateOnly? PromisedDate, int? DelayDays, decimal ContractValue, Guid? ManagerUserId,
    string? ManagerName, Guid ContractId);

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
/// Natureza do custo do projeto (PRD-04 CA 9, ADR 0025 §1): orçado, realizado, comprometido (já contratado, ainda não gasto),
/// projetado e desvio. <c>IncludesBackfill</c>: parte do realizado foi recomposta na implantação ("Calculado na implantação", CA 13).
/// </summary>
public sealed record ProjectCostLine(
    CostType CostType, decimal Budgeted, decimal Realized, decimal Committed, decimal Projected, decimal Deviation, decimal? DeviationPct, bool IncludesBackfill);

/// <summary>Aba Custos (COM-05 CA 8, 9; PRD-04 CA 9) e margens do Resumo (CA 3), só com <c>projects.view_cost</c>.</summary>
public sealed record ProjectCostView(IReadOnlyList<ProjectCostLine> Lines, ProjectCostLine Total, decimal Revenue, decimal? BudgetedMarginPct, decimal? ProjectedMarginPct);

/// <summary>Título a pagar com o projeto, como o cálculo do custo enxerga (lido por <c>IPayables.ByProjectAsync</c>, nunca copiado).</summary>
public sealed record CostPayable(CostType? CostType, DocumentOrigin Origin, PayableStatus Status, decimal Amount);

/// <summary>Soma das linhas da razão <c>commercial.project_costs</c> por natureza, fonte e marca de recomposição.</summary>
public sealed record CostLedgerLine(CostType CostType, ProjectCostSource Source, decimal Amount, bool IsBackfill);

public sealed record ProjectTimelineEntry(DateTimeOffset At, string Kind, string Title, string? Who, string? Link);

public static class ProjectCosts
{
    /// <summary>
    /// Orçado, realizado, comprometido e projetado por natureza (ADR 0025 §1 e adendo de 21/10, PRD-04 CA 9; 04-fluxos §3.3):
    /// <list type="bullet">
    /// <item><b>Realizado</b> = títulos a pagar confirmados (fora de <c>Planned</c> e <c>Cancelled</c>) que não são compra de item
    /// estocável + títulos <c>Opening</c> (saldo da data de corte) + Σ da razão <c>project_costs</c> (horas; baixa de material na EST-03).</item>
    /// <item><b>Comprometido</b> = títulos <c>Planned</c> que não são compra de item estocável + max(0, Σ títulos de compra de item
    /// estocável (origem <c>Purchase</c> e natureza <c>Material</c>, confirmados ou não) − Σ baixas líquidas de material da razão).</item>
    /// <item><b>Projetado</b> = realizado + max(orçado − realizado, comprometido); <b>desvio</b> = projetado − orçado; margem projetada
    /// = (receita − Σ projetado) ÷ receita.</item>
    /// </list>
    /// Título sem natureza vai para Outros. Os movimentos <c>Production</c> e <c>Shipment</c> não são fonte da razão (nunca entram).
    /// </summary>
    public static ProjectCostView Compute(decimal revenue, IReadOnlyDictionary<CostType, decimal> budget, IEnumerable<CostPayable> payables, IEnumerable<CostLedgerLine> ledger)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(payables);
        ArgumentNullException.ThrowIfNull(ledger);
        var realized = new Dictionary<CostType, decimal>();
        var committed = new Dictionary<CostType, decimal>();
        var purchased = new Dictionary<CostType, decimal>();
        var issued = new Dictionary<CostType, decimal>();
        var backfilled = new HashSet<CostType>();
        foreach (var p in payables.Where(p => p.Status != PayableStatus.Cancelled))
        {
            var type = p.CostType ?? CostType.Other;
            var target = IsStockPurchase(p) ? purchased : p.Origin == DocumentOrigin.Opening || p.Status != PayableStatus.Planned ? realized : committed;
            target[type] = target.GetValueOrDefault(type) + p.Amount;
        }

        foreach (var l in ledger)
        {
            realized[l.CostType] = realized.GetValueOrDefault(l.CostType) + l.Amount;
            if (l.Source is ProjectCostSource.StockIssue or ProjectCostSource.StockReturn)
            {
                issued[l.CostType] = issued.GetValueOrDefault(l.CostType) + l.Amount;
            }

            if (l.IsBackfill && l.Amount != 0)
            {
                backfilled.Add(l.CostType);
            }
        }

        foreach (var (type, amount) in purchased)
        {
            committed[type] = committed.GetValueOrDefault(type) + Math.Max(0, amount - issued.GetValueOrDefault(type));
        }

        var lines = Enum.GetValues<CostType>()
            .Select(t => Line(t, budget.GetValueOrDefault(t), realized.GetValueOrDefault(t), committed.GetValueOrDefault(t), backfilled.Contains(t)))
            .ToList();
        var totalBudget = lines.Sum(l => l.Budgeted);
        var totalProjected = lines.Sum(l => l.Projected);
        var total = new ProjectCostLine(
            CostType.Other, totalBudget, lines.Sum(l => l.Realized), lines.Sum(l => l.Committed), totalProjected, totalProjected - totalBudget, Pct(totalProjected - totalBudget, totalBudget),
            lines.Any(l => l.IncludesBackfill));
        return new ProjectCostView(lines, total, revenue, revenue == 0 ? null : Pct(revenue - totalBudget, revenue), revenue == 0 ? null : Pct(revenue - totalProjected, revenue));
    }

    /// <summary>
    /// Título de compra de item estocável (ADR 0025 adendo): origem <c>Purchase</c> com natureza <c>Material</c>. Conta como
    /// comprometido até a baixa para a OS, confirmado ou não; as origens de compra são o previsto do pedido (<c>Purchase</c>, CMP-03a) e o
    /// título do recebimento (<c>GoodsReceipt</c>, CMP-04 CA 10, D2).
    /// </summary>
    public static bool IsStockPurchase(CostPayable payable) =>
        payable is { Origin: DocumentOrigin.Purchase or DocumentOrigin.GoodsReceipt, CostType: CostType.Material };

    private static ProjectCostLine Line(CostType type, decimal budgeted, decimal realized, decimal committed, bool backfill)
    {
        var projected = realized + Math.Max(budgeted - realized, committed);
        return new ProjectCostLine(type, budgeted, realized, committed, projected, projected - budgeted, Pct(projected - budgeted, budgeted), backfill);
    }

    private static decimal? Pct(decimal part, decimal whole) => whole == 0 ? null : Math.Round(part / whole * 100, 1, MidpointRounding.AwayFromZero);
}
