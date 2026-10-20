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

public sealed record ProjectCostLine(CostType CostType, decimal Budgeted, decimal Realized, decimal Projected, decimal Deviation, decimal? DeviationPct);

/// <summary>Aba Custos (CA 8, 9) e margens do Resumo (CA 3), só com <c>projects.view_cost</c>.</summary>
public sealed record ProjectCostView(IReadOnlyList<ProjectCostLine> Lines, ProjectCostLine Total, decimal Revenue, decimal? BudgetedMarginPct, decimal? ProjectedMarginPct);

public sealed record ProjectTimelineEntry(DateTimeOffset At, string Kind, string Title, string? Who, string? Link);

public static class ProjectCosts
{
    /// <summary>
    /// Orçado × realizado por natureza (04-fluxos §3.3): projetado = realizado + max(orçado − realizado, 0); desvio = projetado − orçado;
    /// margem projetada = (receita − Σ projetado) ÷ receita. Nesta fase o realizado são os títulos a pagar não cancelados.
    /// </summary>
    public static ProjectCostView Compute(decimal revenue, IReadOnlyDictionary<CostType, decimal> budget, IEnumerable<(CostType? Type, decimal Amount, bool Cancelled)> payables)
    {
        ArgumentNullException.ThrowIfNull(budget);
        var realized = payables.Where(p => !p.Cancelled).GroupBy(p => p.Type ?? CostType.Other).ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
        var lines = Enum.GetValues<CostType>().Select(t => Line(t, budget.GetValueOrDefault(t), realized.GetValueOrDefault(t))).ToList();
        var total = Line(CostType.Other, lines.Sum(l => l.Budgeted), lines.Sum(l => l.Realized)) with { Projected = lines.Sum(l => l.Projected) };
        total = total with { Deviation = total.Projected - total.Budgeted, DeviationPct = Pct(total.Projected - total.Budgeted, total.Budgeted) };
        return new ProjectCostView(lines, total, revenue, revenue == 0 ? null : Pct(revenue - total.Budgeted, revenue), revenue == 0 ? null : Pct(revenue - total.Projected, revenue));
    }

    private static ProjectCostLine Line(CostType type, decimal budgeted, decimal realized)
    {
        var projected = realized + Math.Max(budgeted - realized, 0);
        return new ProjectCostLine(type, budgeted, realized, projected, projected - budgeted, Pct(projected - budgeted, budgeted));
    }

    private static decimal? Pct(decimal part, decimal whole) => whole == 0 ? null : Math.Round(part / whole * 100, 1, MidpointRounding.AwayFromZero);
}
