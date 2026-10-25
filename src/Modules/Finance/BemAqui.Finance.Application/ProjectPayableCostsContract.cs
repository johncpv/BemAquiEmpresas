using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// <see cref="IProjectPayableCosts"/> (DSH-02 CA 2 a 4 e 18): uma consulta agrupada no banco por projeto, natureza, origem e situação (uma
/// linha por combinação, não por título) e a classe pela <see cref="PayableCostRules"/> em memória.
/// </summary>
public sealed class ProjectPayableCostsContract(IFinanceDb db) : IProjectPayableCosts
{
    public async Task<IReadOnlyList<ProjectPayableCostTotals>> CostSummaryByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectIds);
        if (projectIds.Count == 0)
        {
            return [];
        }

        var ids = projectIds.Distinct().ToArray();
        var groups = await db.Payables.AsNoTracking()
            .Where(p => p.ProjectId != null && ids.Contains(p.ProjectId.Value) && p.Status != PayableStatus.Cancelled)
            .GroupBy(p => new { ProjectId = p.ProjectId!.Value, p.CostType, p.Origin, p.Status })
            .Select(g => new { g.Key.ProjectId, g.Key.CostType, g.Key.Origin, g.Key.Status, Amount = g.Sum(p => p.Amount) })
            .ToListAsync(cancellationToken);
        return groups
            .Select(g => new Classified(g.ProjectId, g.CostType ?? CostType.Other, PayableCostRules.Bucket(g.Origin, g.CostType, g.Status), g.Amount))
            .Where(g => g.Bucket is not null)
            .GroupBy(g => (g.ProjectId, g.CostType))
            .Select(g => new ProjectPayableCostTotals(
                g.Key.ProjectId, g.Key.CostType, Sum(g, PayableCostBucket.Confirmed), Sum(g, PayableCostBucket.Planned), Sum(g, PayableCostBucket.StockPurchase), Sum(g, PayableCostBucket.TaxGuide)))
            .ToList();
    }

    private static decimal Sum(IEnumerable<Classified> rows, PayableCostBucket bucket) => rows.Where(r => r.Bucket == bucket).Sum(r => r.Amount);

    private sealed record Classified(Guid ProjectId, CostType CostType, PayableCostBucket? Bucket, decimal Amount);

    public async Task<IReadOnlyList<ProjectPayableCostEntry>> CostEntriesAsync(Guid projectId, CostType costType, CancellationToken cancellationToken = default)
    {
        var rows = await db.Payables.AsNoTracking()
            .Where(p => p.ProjectId == projectId && p.Status != PayableStatus.Cancelled && (costType == CostType.Other ? p.CostType == null || p.CostType == CostType.Other : p.CostType == costType))
            .OrderBy(p => p.IssueDate).ThenBy(p => p.Number)
            .Select(p => new { p.Id, p.Number, p.PartyId, p.Origin, p.OriginId, p.Status, p.IssueDate, p.DueDate, p.Amount, p.CostType })
            .ToListAsync(cancellationToken);
        return rows.Select(p => new ProjectPayableCostEntry(
                p.Id, p.Number, p.PartyId, p.Origin, p.OriginId, p.Status, p.IssueDate, p.DueDate, p.Amount, p.CostType ?? CostType.Other, PayableCostRules.Bucket(p.Origin, p.CostType, p.Status)!.Value))
            .ToList();
    }
}
