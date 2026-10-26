using System.Globalization;
using BemAqui.Commercial.Contracts;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Dashboard.Application;

/// <summary>
/// Montagem do resultado gerencial por projeto sem banco (DSH-01b CA 6 e 7): período em meses inteiros, receita × custos diretos por natureza,
/// resultado = receita − custos diretos e margem % com 1 casa. Sem rateio de despesas indiretas; nada recalculado (as somas vêm dos contratos).
/// </summary>
public static class ProjectResultRules
{
    /// <summary>Meses padrão: os 12 até o mês corrente.</summary>
    public const int DefaultMonths = 12;

    /// <summary>Período máximo aceito.</summary>
    public const int MaxMonths = 24;

    public static readonly IReadOnlySet<string> Sorts = new HashSet<string>(StringComparer.Ordinal) { "result", "-result", "revenue", "-revenue", "margin", "-margin", "number" };

    /// <summary>Situações do filtro e as situações do projeto; <c>todos</c> (o padrão) = lista vazia.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Situations = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["todos"] = [],
        ["andamento"] = ["Planned", "InProgress", "Fabricated", "Installing"],
        ["entregue"] = ["Delivered"],
        ["encerrado"] = ["Closed"],
        ["concluido"] = ["Delivered", "Closed"],
        ["cancelado"] = ["Cancelled"],
    };

    /// <summary>Naturezas na ordem da tela (DSH-02).</summary>
    public static readonly CostType[] CostTypes = [CostType.Material, CostType.Labor, CostType.Installation, CostType.Freight, CostType.Tax, CostType.Other];

    public static string CostLabel(CostType type) => type switch
    {
        CostType.Material => "Material",
        CostType.Labor => "Mão de obra",
        CostType.Installation => "Instalação",
        CostType.Freight => "Frete",
        CostType.Tax => "Impostos",
        _ => "Outros",
    };

    public static string MonthKey(DateOnly month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>"aaaa-mm" para o primeiro dia do mês; inválido → null.</summary>
    public static DateOnly? ParseMonth(string? value) =>
        DateOnly.TryParseExact((value ?? string.Empty) + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>
    /// Período pedido (CA 6): <paramref name="from"/> e <paramref name="to"/> em "aaaa-mm"; sem eles, os 12 meses até o mês de hoje. Devolve o
    /// primeiro dia do primeiro mês, o último dia do último mês e os meses, ou a mensagem do 422.
    /// </summary>
    public static (DateOnly From, DateOnly To, IReadOnlyList<DateOnly> Months, string? Error) Period(string? from, string? to, DateOnly today)
    {
        var current = new DateOnly(today.Year, today.Month, 1);
        var end = string.IsNullOrWhiteSpace(to) ? current : ParseMonth(to);
        var start = string.IsNullOrWhiteSpace(from) ? end?.AddMonths(-(DefaultMonths - 1)) : ParseMonth(from);
        if (start is not { } s || end is not { } e)
        {
            return (default, default, [], "Informe o mês no formato aaaa-mm.");
        }

        if (s > e)
        {
            return (default, default, [], "O mês inicial deve ser antes do final.");
        }

        var count = ((e.Year - s.Year) * 12) + e.Month - s.Month + 1;
        if (count > MaxMonths)
        {
            return (default, default, [], $"Escolha no máximo {MaxMonths} meses.");
        }

        return (s, e.AddMonths(1).AddDays(-1), Enumerable.Range(0, count).Select(i => s.AddMonths(i)).ToList(), null);
    }

    public static decimal? MarginPct(decimal result, decimal revenue) => revenue == 0 ? null : Math.Round(result / revenue * 100, 1, MidpointRounding.AwayFromZero);

    /// <summary>Somas de um projeto no período: receita, custos por natureza (as 6, na ordem da tela) e resultado.</summary>
    public sealed record Figures(decimal Revenue, IReadOnlyList<ProjectResultCostAmount> Costs)
    {
        public decimal DirectCosts => Costs.Sum(c => c.Amount);

        public decimal Result => Revenue - DirectCosts;

        public decimal? MarginPct => ProjectResultRules.MarginPct(Result, Revenue);

        public bool IsEmpty => Revenue == 0 && Costs.All(c => c.Amount == 0);
    }

    /// <summary>Somas por projeto (CA 6) a partir das linhas por mês dos contratos.</summary>
    public static Dictionary<Guid, Figures> ByProject(IEnumerable<Guid> projectIds, IEnumerable<ProjectMonthlyRevenue> revenue, IEnumerable<ProjectRealizedByPeriod> costs)
    {
        var rev = revenue.GroupBy(r => r.ProjectId).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
        var cost = costs.GroupBy(c => (c.ProjectId, c.CostType)).ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));
        return projectIds.Distinct().ToDictionary(id => id, id => new Figures(rev.GetValueOrDefault(id),
            CostTypes.Select(t => new ProjectResultCostAmount(t, cost.GetValueOrDefault((id, t)))).ToList()));
    }

    /// <summary>Somas de todas as linhas (o rodapé da lista).</summary>
    public static ProjectResultTotals Totals(IReadOnlyCollection<Figures> rows)
    {
        var revenue = rows.Sum(r => r.Revenue);
        var costs = CostTypes.Select(t => new ProjectResultCostAmount(t, rows.Sum(r => r.Costs.First(c => c.CostType == t).Amount))).ToList();
        var total = new Figures(revenue, costs);
        return new ProjectResultTotals(rows.Count, revenue, costs, total.DirectCosts, total.Result, total.MarginPct);
    }

    /// <summary>Ordem da lista (allowlist): padrão o pior resultado primeiro; desempate pelo número.</summary>
    public static IEnumerable<(ProjectSummary Project, Figures Figures)> Sort(IEnumerable<(ProjectSummary Project, Figures Figures)> rows, string sort) => sort switch
    {
        "-result" => rows.OrderByDescending(r => r.Figures.Result).ThenBy(r => r.Project.Number, StringComparer.Ordinal),
        "revenue" => rows.OrderBy(r => r.Figures.Revenue).ThenBy(r => r.Project.Number, StringComparer.Ordinal),
        "-revenue" => rows.OrderByDescending(r => r.Figures.Revenue).ThenBy(r => r.Project.Number, StringComparer.Ordinal),
        "margin" => rows.OrderBy(r => r.Figures.MarginPct is null).ThenBy(r => r.Figures.MarginPct).ThenBy(r => r.Project.Number, StringComparer.Ordinal),
        "-margin" => rows.OrderBy(r => r.Figures.MarginPct is null).ThenByDescending(r => r.Figures.MarginPct).ThenBy(r => r.Project.Number, StringComparer.Ordinal),
        "number" => rows.OrderBy(r => r.Project.Number, StringComparer.Ordinal),
        _ => rows.OrderBy(r => r.Figures.Result).ThenBy(r => r.Project.Number, StringComparer.Ordinal),
    };

    /// <summary>
    /// Visão mês a mês (CA 7): Receita, (−) cada natureza, Custos diretos e = Resultado, com um valor por mês e o total. Receita e naturezas
    /// abrem o drill-down; as linhas de soma não.
    /// </summary>
    public static IReadOnlyList<ProjectResultLine> Lines(IReadOnlyList<DateOnly> months, IEnumerable<ProjectMonthlyRevenue> revenue, IEnumerable<ProjectRealizedByPeriod> costs)
    {
        ArgumentNullException.ThrowIfNull(months);
        var rev = revenue.GroupBy(r => new DateOnly(r.Year, r.Month, 1)).ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
        var cost = costs.GroupBy(c => (c.CostType, Month: new DateOnly(c.Year, c.Month, 1))).ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));
        var revenueValues = months.Select(m => rev.GetValueOrDefault(m)).ToList();
        var lines = new List<ProjectResultLine> { new("revenue", "Receita", "Revenue", revenueValues, revenueValues.Sum(), true) };
        var totalCosts = months.Select(_ => 0m).ToArray();
        foreach (var type in CostTypes)
        {
            var values = months.Select(m => cost.GetValueOrDefault((type, m))).ToList();
            for (var i = 0; i < values.Count; i++)
            {
                totalCosts[i] += values[i];
            }

            lines.Add(new ProjectResultLine(type.ToString(), "(−) " + CostLabel(type), "Cost", values, values.Sum(), true));
        }

        lines.Add(new ProjectResultLine("costs", "Custos diretos", "Costs", totalCosts, totalCosts.Sum(), false));
        var result = revenueValues.Select((v, i) => v - totalCosts[i]).ToList();
        lines.Add(new ProjectResultLine("result", "= Resultado", "Result", result, result.Sum(), false));
        return lines;
    }
}
