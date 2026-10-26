using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Domain;

/// <summary>
/// Títulos a pagar de um projeto numa natureza, já separados pela regra do Finance (DSH-02, ADR 0025 adendo): <c>Confirmed</c> (fora de
/// <c>Planned</c> e <c>Cancelled</c>, mais o <c>Opening</c> pelo saldo da data de corte), <c>Planned</c> (previsto), <c>StockPurchase</c>
/// (compra de item estocável, confirmada ou não, fora de <c>Cancelled</c>) e <c>TaxGuide</c> (guia de imposto, que nunca é custo do projeto).
/// </summary>
public sealed record PayableCostTotals(CostType CostType, decimal Confirmed, decimal Planned, decimal StockPurchase, decimal TaxGuide);

/// <summary>Soma das linhas da razão <c>commercial.project_costs</c> por natureza, fonte e marca de recomposição; <c>InvoiceAmount</c> só no imposto.</summary>
public sealed record LedgerCostTotals(CostType CostType, ProjectCostSource Source, bool IsBackfill, decimal Amount, decimal InvoiceAmount = 0);

/// <summary>De onde vem o imposto projetado (DSH-02 CA 5): "Imposto orçado" ou "Imposto das notas + orçado no restante".</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TaxProjectionBasis>))]
public enum TaxProjectionBasis
{
    Budgeted,
    Invoices,
}

/// <summary>Uma natureza: orçado, realizado, comprometido, projetado e desvio (valor e % do orçado, <c>numeric(9,4)</c>; orçado 0 → null).</summary>
public sealed record MarginLine(CostType CostType, decimal Budgeted, decimal Realized, decimal Committed, decimal Projected, decimal Deviation, decimal? DeviationPct, bool IncludesBackfill);

/// <summary>
/// Resultado do cálculo de um projeto ou do consolidado (DSH-02 CA 1, 5, 6 e 10): as naturezas, o total, a receita contratada e as margens
/// orçada, realizada e projetada (valor e %; receita 0 → % null), o desvio da margem em pontos percentuais (projetada − orçada), a base do
/// imposto projetado e o valor das guias de imposto com o projeto (fora do custo, CA 3).
/// </summary>
public sealed record MarginResult(
    IReadOnlyList<MarginLine> Lines, MarginLine Total, decimal Revenue, decimal BudgetedMargin, decimal? BudgetedMarginPct, decimal RealizedMargin, decimal? RealizedMarginPct,
    decimal ProjectedMargin, decimal? ProjectedMarginPct, decimal? MarginDeviationPp, TaxProjectionBasis TaxBasis, decimal TaxGuides);

/// <summary>
/// Um cálculo só de orçado × realizado e margem (DSH-02 CA 1 a 6; ADR 0025 §1 e adendo D8; 04-fluxos §3.3), usado pela aba Custos do
/// Projeto 360, pelo relatório "Orçado × realizado" e pelo painel do dono:
/// <list type="bullet">
/// <item><b>Realizado</b> = títulos confirmados da natureza + Σ da razão (baixa líquida de material, horas); <b>Impostos</b> só pela
/// razão <c>InvoiceTax</c>/<c>InvoiceTaxReversal</c> (nota autorizada); título de natureza Impostos é guia e nunca entra.</item>
/// <item><b>Comprometido</b> = títulos previstos da natureza + max(0, compra de item estocável − baixa líquida de material da natureza):
/// a diferença negativa não vira crédito do projeto.</item>
/// <item><b>Projetado</b> = realizado + max(orçado − realizado, comprometido); em <b>Impostos</b>, sem nota, o imposto orçado; com nota,
/// imposto das notas + orçado × (1 − faturado ÷ receita).</item>
/// <item><b>Desvio</b> = projetado − orçado; margens orçada, realizada e projetada sobre a receita contratada.</item>
/// </list>
/// Somas em <c>decimal</c>, arredondamento só no imposto projetado (2 casas) e nos percentuais (4 casas, <c>numeric(9,4)</c>).
/// </summary>
public static class ProjectMarginCalculator
{
    /// <summary>Linhas da razão que são imposto da nota (o realizado de Impostos).</summary>
    public static bool IsInvoiceTax(ProjectCostSource source) => source is ProjectCostSource.InvoiceTax or ProjectCostSource.InvoiceTaxReversal;

    /// <summary>Linhas da razão que são baixa ou devolução de material para a OS (a "baixa líquida" do comprometido por saldo).</summary>
    public static bool IsStockMovement(ProjectCostSource source) => source is ProjectCostSource.StockIssue or ProjectCostSource.StockReturn;

    public static MarginResult Compute(decimal revenue, IReadOnlyDictionary<CostType, decimal> budget, IEnumerable<PayableCostTotals> payables, IEnumerable<LedgerCostTotals> ledger)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(payables);
        ArgumentNullException.ThrowIfNull(ledger);
        var realized = new Dictionary<CostType, decimal>();
        var planned = new Dictionary<CostType, decimal>();
        var purchased = new Dictionary<CostType, decimal>();
        var issued = new Dictionary<CostType, decimal>();
        var backfilled = new HashSet<CostType>();
        decimal taxGuides = 0, taxRealized = 0, invoiced = 0;
        foreach (var p in payables)
        {
            if (p.CostType == CostType.Tax)
            {
                // Guia de imposto (DARF, GARE, DAS) é custo da empresa: vai para o DRE, nunca para o projeto (CA 3).
                taxGuides += p.Confirmed + p.Planned + p.StockPurchase + p.TaxGuide;
                continue;
            }

            taxGuides += p.TaxGuide;
            Add(realized, p.CostType, p.Confirmed);
            Add(planned, p.CostType, p.Planned);
            Add(purchased, p.CostType, p.StockPurchase);
        }

        foreach (var l in ledger)
        {
            if (IsInvoiceTax(l.Source))
            {
                taxRealized += l.Amount;
                invoiced += l.InvoiceAmount;
                continue;
            }

            Add(realized, l.CostType, l.Amount);
            if (IsStockMovement(l.Source))
            {
                Add(issued, l.CostType, l.Amount);
            }

            if (l.IsBackfill && l.Amount != 0)
            {
                backfilled.Add(l.CostType);
            }
        }

        // Com a nota estornada (líquido 0), o projetado volta ao imposto orçado.
        var hasInvoice = taxRealized != 0 || invoiced != 0;
        var lines = new List<MarginLine>();
        foreach (var type in Enum.GetValues<CostType>())
        {
            var budgeted = budget.GetValueOrDefault(type);
            if (type == CostType.Tax)
            {
                var remaining = revenue == 0 ? 0m : Math.Clamp(1 - (invoiced / revenue), 0m, 1m);
                var projectedTax = hasInvoice ? taxRealized + Math.Round(budgeted * remaining, 2, MidpointRounding.AwayFromZero) : budgeted;
                lines.Add(new MarginLine(type, budgeted, taxRealized, 0, projectedTax, projectedTax - budgeted, Pct(projectedTax - budgeted, budgeted), false));
                continue;
            }

            var committed = planned.GetValueOrDefault(type) + Math.Max(0, purchased.GetValueOrDefault(type) - issued.GetValueOrDefault(type));
            lines.Add(Line(type, budgeted, realized.GetValueOrDefault(type), committed, backfilled.Contains(type)));
        }

        return Result(lines, revenue, hasInvoice ? TaxProjectionBasis.Invoices : TaxProjectionBasis.Budgeted, taxGuides);
    }

    /// <summary>
    /// Consolidado de vários projetos (DSH-02 CA 10; adendo D8): soma por natureza e margens <b>ponderadas pela receita</b> (Σ ÷ Σ), nunca
    /// a média simples das margens. A base do imposto é "das notas" se algum projeto já tem nota.
    /// </summary>
    public static MarginResult Consolidate(IEnumerable<MarginResult> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var list = projects.ToList();
        var lines = Enum.GetValues<CostType>().Select(type =>
        {
            var mine = list.Select(p => p.Lines.First(l => l.CostType == type)).ToList();
            var budgeted = mine.Sum(l => l.Budgeted);
            var projected = mine.Sum(l => l.Projected);
            return new MarginLine(type, budgeted, mine.Sum(l => l.Realized), mine.Sum(l => l.Committed), projected, projected - budgeted, Pct(projected - budgeted, budgeted), mine.Any(l => l.IncludesBackfill));
        }).ToList();
        return Result(lines, list.Sum(p => p.Revenue), list.Any(p => p.TaxBasis == TaxProjectionBasis.Invoices) ? TaxProjectionBasis.Invoices : TaxProjectionBasis.Budgeted,
            list.Sum(p => p.TaxGuides));
    }

    /// <summary>Percentual com 4 casas (<c>numeric(9,4)</c>); a tela mostra 1 casa. Base 0 → null ("—").</summary>
    public static decimal? Pct(decimal part, decimal whole) => whole == 0 ? null : Math.Round(part / whole * 100, 4, MidpointRounding.AwayFromZero);

    private static MarginResult Result(List<MarginLine> lines, decimal revenue, TaxProjectionBasis basis, decimal taxGuides)
    {
        var budgeted = lines.Sum(l => l.Budgeted);
        var realized = lines.Sum(l => l.Realized);
        var projected = lines.Sum(l => l.Projected);
        var total = new MarginLine(CostType.Other, budgeted, realized, lines.Sum(l => l.Committed), projected, projected - budgeted, Pct(projected - budgeted, budgeted),
            lines.Any(l => l.IncludesBackfill));
        var budgetedPct = Pct(revenue - budgeted, revenue);
        var projectedPct = Pct(revenue - projected, revenue);
        return new MarginResult(lines, total, revenue, revenue - budgeted, budgetedPct, revenue - realized, Pct(revenue - realized, revenue), revenue - projected, projectedPct,
            budgetedPct is { } b && projectedPct is { } p ? p - b : null, basis, taxGuides);
    }

    private static MarginLine Line(CostType type, decimal budgeted, decimal realized, decimal committed, bool backfill)
    {
        var projected = realized + Math.Max(budgeted - realized, committed);
        return new MarginLine(type, budgeted, realized, committed, projected, projected - budgeted, Pct(projected - budgeted, budgeted), backfill);
    }

    private static void Add(Dictionary<CostType, decimal> target, CostType type, decimal amount)
    {
        if (amount != 0)
        {
            target[type] = target.GetValueOrDefault(type) + amount;
        }
    }
}
