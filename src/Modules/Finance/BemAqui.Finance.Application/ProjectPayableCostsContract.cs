using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// <see cref="IProjectPayableCosts"/> (DSH-02 CA 2 a 4 e 18): uma consulta agrupada no banco por projeto, com as classes da
/// <see cref="PayableCostRules"/> em somas condicionais (uma linha por projeto, não por título).
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
        // Uma linha por projeto (DSH-02 CA 18): as somas de cada natureza já nas classes da PayableCostRules.Bucket, para trafegar 5 mil linhas
        // e não uma por projeto, natureza, origem e situação. Impostos é guia; Material de compra (pedido ou recebimento) é compra de item
        // estocável; o resto é confirmado (abertura ou fora de previsto) ou previsto (o total menos as outras classes). Sem natureza vai para
        // Outros. O array vai num parâmetro só (= ANY), não em 5 mil parâmetros.
        var rows = await db.Payables.AsNoTracking()
            .Where(p => p.ProjectId != null && EF.Parameter(ids).Contains(p.ProjectId.Value) && p.Status != PayableStatus.Cancelled)
            .GroupBy(p => p.ProjectId!.Value)
            .Select(g => new PayableSums(
                g.Key,
                g.Sum(p => p.CostType == CostType.Material ? p.Amount : 0),
                g.Sum(p => p.CostType == CostType.Material && (p.Origin == DocumentOrigin.Purchase || p.Origin == DocumentOrigin.GoodsReceipt) ? p.Amount : 0),
                g.Sum(p => p.CostType == CostType.Material && p.Origin != DocumentOrigin.Purchase && p.Origin != DocumentOrigin.GoodsReceipt
                    && (p.Origin == DocumentOrigin.Opening || p.Status != PayableStatus.Planned) ? p.Amount : 0),
                g.Sum(p => p.CostType == CostType.Labor ? p.Amount : 0),
                g.Sum(p => p.CostType == CostType.Labor && (p.Origin == DocumentOrigin.Opening || p.Status != PayableStatus.Planned) ? p.Amount : 0),
                g.Sum(p => p.CostType == CostType.Installation ? p.Amount : 0),
                g.Sum(p => p.CostType == CostType.Installation && (p.Origin == DocumentOrigin.Opening || p.Status != PayableStatus.Planned) ? p.Amount : 0),
                g.Sum(p => p.CostType == CostType.Freight ? p.Amount : 0),
                g.Sum(p => p.CostType == CostType.Freight && (p.Origin == DocumentOrigin.Opening || p.Status != PayableStatus.Planned) ? p.Amount : 0),
                g.Sum(p => p.CostType == null || p.CostType == CostType.Other ? p.Amount : 0),
                g.Sum(p => (p.CostType == null || p.CostType == CostType.Other) && (p.Origin == DocumentOrigin.Opening || p.Status != PayableStatus.Planned) ? p.Amount : 0),
                g.Sum(p => p.CostType == CostType.Tax ? p.Amount : 0)))
            .ToListAsync(cancellationToken);
        return rows.SelectMany(r => r.Totals()).ToList();
    }

    /// <summary>As somas de um projeto (<see cref="CostSummaryByProjectsAsync"/>); <c>Total</c> de cada natureza inclui todas as classes.</summary>
    private sealed record PayableSums(
        Guid ProjectId, decimal MaterialTotal, decimal MaterialPurchase, decimal MaterialConfirmed, decimal LaborTotal, decimal LaborConfirmed, decimal InstallationTotal,
        decimal InstallationConfirmed, decimal FreightTotal, decimal FreightConfirmed, decimal OtherTotal, decimal OtherConfirmed, decimal Tax)
    {
        public IEnumerable<ProjectPayableCostTotals> Totals()
        {
            var lines = new[]
            {
                new ProjectPayableCostTotals(ProjectId, CostType.Material, MaterialConfirmed, MaterialTotal - MaterialPurchase - MaterialConfirmed, MaterialPurchase, 0),
                Plain(CostType.Labor, LaborTotal, LaborConfirmed),
                Plain(CostType.Installation, InstallationTotal, InstallationConfirmed),
                Plain(CostType.Freight, FreightTotal, FreightConfirmed),
                new ProjectPayableCostTotals(ProjectId, CostType.Tax, 0, 0, 0, Tax),
                Plain(CostType.Other, OtherTotal, OtherConfirmed),
            };
            return lines.Where(l => l.Confirmed != 0 || l.Planned != 0 || l.StockPurchase != 0 || l.TaxGuide != 0);
        }

        private ProjectPayableCostTotals Plain(CostType type, decimal total, decimal confirmed) => new(ProjectId, type, confirmed, total - confirmed, 0, 0);
    }

    public async Task<IReadOnlyList<ProjectPayableCostByMonth>> ConfirmedByMonthAsync(IReadOnlyCollection<Guid> projectIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectIds);
        if (projectIds.Count == 0)
        {
            return [];
        }

        var ids = projectIds.Distinct().ToArray();
        // A classe Confirmed da PayableCostRules.Bucket, em SQL: fora de cancelado e de Impostos (guia), fora da compra de item estocável
        // (Material de pedido ou recebimento) e abertura ou fora de previsto.
        var rows = await db.Payables.AsNoTracking()
            .Where(p => p.ProjectId != null && EF.Parameter(ids).Contains(p.ProjectId.Value) && p.Status != PayableStatus.Cancelled && p.IssueDate >= from && p.IssueDate <= to
                && (p.CostType == null || p.CostType != CostType.Tax)
                && !(p.CostType == CostType.Material && (p.Origin == DocumentOrigin.Purchase || p.Origin == DocumentOrigin.GoodsReceipt))
                && (p.Origin == DocumentOrigin.Opening || p.Status != PayableStatus.Planned))
            .GroupBy(p => new { ProjectId = p.ProjectId!.Value, p.CostType, p.IssueDate.Year, p.IssueDate.Month })
            .Select(g => new { g.Key.ProjectId, g.Key.CostType, g.Key.Year, g.Key.Month, Amount = g.Sum(p => p.Amount) })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => (r.ProjectId, Type: r.CostType ?? CostType.Other, r.Year, r.Month))
            .Select(g => new ProjectPayableCostByMonth(g.Key.ProjectId, g.Key.Type, g.Key.Year, g.Key.Month, g.Sum(r => r.Amount))).ToList();
    }

    public async Task<IReadOnlyList<ProjectPayableCostEntry>> CostEntriesAsync(Guid projectId, CostType costType, CancellationToken cancellationToken = default)
    {
        var rows = await db.Payables.AsNoTracking()
            .Where(p => p.ProjectId == projectId && p.Status != PayableStatus.Cancelled && (costType == CostType.Other ? p.CostType == null || p.CostType == CostType.Other : p.CostType == costType))
            .OrderBy(p => p.IssueDate).ThenBy(p => p.Number)
            .Select(p => new { p.Id, p.Number, p.PartyId, p.Origin, p.OriginId, p.Status, p.IssueDate, p.DueDate, p.Amount, p.CostType })
            .ToListAsync(cancellationToken);
        return rows.Select(p => new ProjectPayableCostEntry(
                p.Id, p.Number, p.PartyId ?? Guid.Empty, p.Origin, p.OriginId, p.Status, p.IssueDate, p.DueDate, p.Amount, p.CostType ?? CostType.Other, PayableCostRules.Bucket(p.Origin, p.CostType, p.Status)!.Value))
            .ToList();
    }
}
