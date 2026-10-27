using BemAqui.Commercial.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Dashboard.Application;

/// <summary>
/// Regras dos cards da DSH-01b sem banco: quem está em andamento, a ordem da tabela de projetos, a carteira e as entregas do mês. O painel não
/// recalcula número de origem (ADR 0028 §1): soma e ordena o que os contratos dos donos devolvem.
/// </summary>
public static class OwnerRules
{
    /// <summary>Linhas da tabela "Projetos em andamento" (CA 1).</summary>
    public const int ActiveRows = 8;

    /// <summary>Piores projetos pelo desvio no card Margem (CA 1).</summary>
    public const int WorstRows = 5;

    /// <summary>Semanas da ocupação: a atual e as 4 seguintes (PRD §5.14).</summary>
    public const int LoadWeeks = 5;

    private static readonly string[] ActiveStatuses = ["Planned", "InProgress", "Fabricated", "Installing"];

    /// <summary>Em andamento = <c>Planned</c>, <c>InProgress</c>, <c>Fabricated</c> e <c>Installing</c> (o filtro padrão do DSH-02).</summary>
    public static bool IsActive(string status) => ActiveStatuses.Contains(status, StringComparer.Ordinal);

    /// <summary>Ainda não fabricado (a produzir, CA 3): <c>Planned</c> e <c>InProgress</c>.</summary>
    public static bool IsToProduce(string status) => status is "Planned" or "InProgress";

    /// <summary>
    /// Ordem da tabela (CA 1): atrasados primeiro (mais dias de atraso antes), depois em risco (menor folga antes), depois pela data prometida
    /// (sem data no fim) e pelo número.
    /// </summary>
    public static IReadOnlyList<ProjectPortfolioItem> ActiveOrder(IEnumerable<ProjectPortfolioItem> projects) =>
        projects.Where(p => IsActive(p.Status))
            .OrderBy(p => p.Forecast.Risk switch { DeliveryRisk.Late => 0, DeliveryRisk.AtRisk => 1, _ => 2 })
            .ThenByDescending(p => p.Forecast.Risk == DeliveryRisk.Late ? p.Forecast.DelayDays ?? 0 : 0)
            .ThenBy(p => p.Forecast.Risk == DeliveryRisk.AtRisk ? p.Forecast.SlackDays ?? 0 : 0)
            .ThenBy(p => p.Forecast.PromisedDate is null)
            .ThenBy(p => p.Forecast.PromisedDate)
            .ThenBy(p => p.Number, StringComparer.Ordinal)
            .ToList();

    /// <summary>A produzir (CA 3): Σ receita contratada e quantos projetos <c>Planned</c> e <c>InProgress</c>.</summary>
    public static (decimal Amount, int Count) ToProduce(IEnumerable<ProjectPortfolioItem> projects)
    {
        var list = projects.Where(p => IsToProduce(p.Status)).ToList();
        return (list.Sum(p => p.ContractedAmount), list.Count);
    }

    /// <summary>Projetos que entram no "a faturar" (CA 3): os de contrato <c>Active</c>.</summary>
    public static IReadOnlyList<ProjectPortfolioItem> ToInvoiceProjects(IEnumerable<ProjectPortfolioItem> projects) =>
        projects.Where(p => p.ContractStatus == "Active").ToList();

    /// <summary>
    /// Entregas do mês de <paramref name="today"/> (CA 1): previstas no mês e ainda não entregues, entregues no mês, em risco e atrasadas (função
    /// única da PLN-01b) e o projeto que mais vai atrasar (maior previsto − prometido entre os em andamento).
    /// </summary>
    public static (int Forecast, int Delivered, int AtRisk, int Late, DelayedProjectItem? MostDelayed) Deliveries(IReadOnlyCollection<ProjectPortfolioItem> projects, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(projects);
        bool InMonth(DateOnly? d) => d is { } x && x.Year == today.Year && x.Month == today.Month;
        var active = projects.Where(p => IsActive(p.Status)).ToList();
        var worst = active.Where(p => p.Forecast.SlackDays < 0)
            .OrderBy(p => p.Forecast.SlackDays).ThenBy(p => p.Number, StringComparer.Ordinal).FirstOrDefault();
        return (
            active.Count(p => InMonth(p.Forecast.ForecastDate) && p.DeliveredOn is null),
            projects.Count(p => InMonth(p.DeliveredOn)),
            active.Count(p => p.Forecast.Risk == DeliveryRisk.AtRisk),
            active.Count(p => p.Forecast.Risk == DeliveryRisk.Late),
            worst is null ? null : new DelayedProjectItem(worst.Id, worst.Number, worst.Name, worst.Forecast.PromisedDate, worst.Forecast.ForecastDate, -worst.Forecast.SlackDays!.Value));
    }

    /// <summary>% com 1 casa (recebido do contratado); base 0 → null.</summary>
    public static decimal? Pct1(decimal part, decimal whole) => whole == 0 ? null : Math.Round(part / whole * 100, 1, MidpointRounding.AwayFromZero);
}
