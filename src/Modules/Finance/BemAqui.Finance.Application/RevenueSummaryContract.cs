using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;

namespace BemAqui.Finance.Application;

/// <summary>
/// <see cref="IRevenueSummary"/> (FIN-08 CA 15): o mensal sai das mesmas entradas e da mesma árvore do DRE (a linha "Receita
/// bruta"); o por projeto, da receita reconhecida pela mesma regra (<see cref="RevenueRecognitionRouter"/>), nas contas do grupo
/// Receita bruta. Meses inteiros, com zero nos meses sem receita.
/// </summary>
public sealed class RevenueSummaryContract(DreService dre, RevenueRecognitionRouter revenue) : IRevenueSummary
{
    public async Task<IReadOnlyList<MonthlyRevenue>> GrossRevenueAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var months = DreBuilder.MonthsBetween(from, to);
        var start = DreBuilder.MonthOf(from);
        var end = DreBuilder.MonthOf(to).AddMonths(1).AddDays(-1);
        var (accounts, _) = await dre.AccountsAsync(cancellationToken);
        var (entries, routed) = await dre.LedgerAsync(start, end, cancellationToken);
        var statement = DreBuilder.Build(months, accounts, entries.Select(e => new DreAmount(e.AccountId, DreBuilder.MonthOf(e.Date), e.Value)));
        var line = statement.Row(DreBuilder.GroupKey(DreGroup.GrossRevenue));
        return months.Select((m, i) => new MonthlyRevenue(m.Year, m.Month, line.Values[i], routed.IsProvisional(m))).ToList();
    }

    public async Task<IReadOnlyList<ProjectMonthlyRevenue>> GrossRevenueByProjectsAsync(IReadOnlyCollection<Guid> projectIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectIds);
        if (projectIds.Count == 0)
        {
            return [];
        }

        var months = DreBuilder.MonthsBetween(from, to);
        var start = DreBuilder.MonthOf(from);
        var end = DreBuilder.MonthOf(to).AddMonths(1).AddDays(-1);
        var (accounts, _) = await dre.AccountsAsync(cancellationToken);
        var gross = accounts.Where(a => a.Group == DreGroup.GrossRevenue).Select(a => a.Id).ToHashSet();
        var routed = await revenue.RecognizeAsync(start, end, projectIds, cancellationToken);
        var sums = routed.Amounts.Where(a => a.ProjectId is not null && gross.Contains(a.AccountId))
            .GroupBy(a => (Project: a.ProjectId!.Value, Month: DreBuilder.MonthOf(a.Date))).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));
        return projectIds.Distinct().SelectMany(p => months.Select(m => new ProjectMonthlyRevenue(p, m.Year, m.Month, sums.GetValueOrDefault((p, m)), routed.IsProvisional(m)))).ToList();
    }

    public async Task<IReadOnlyList<ProjectRevenueDocument>> GrossRevenueDocumentsAsync(Guid projectId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var (accounts, _) = await dre.AccountsAsync(cancellationToken);
        var gross = accounts.Where(a => a.Group == DreGroup.GrossRevenue).Select(a => a.Id).ToList();
        if (gross.Count == 0)
        {
            return [];
        }

        var documents = await revenue.DocumentsAsync(gross, from, to, cancellationToken);
        return documents.Where(d => d.ProjectId == projectId).OrderBy(d => d.Date).ThenBy(d => d.Number, StringComparer.Ordinal)
            .Select(d => new ProjectRevenueDocument(d.Kind, d.DocumentId, d.Number, d.Date, d.Amount, d.Note, d.Link)).ToList();
    }
}
