using BemAqui.HR.Contracts;
using BemAqui.Production.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// <see cref="IProjectProductionCosts"/> (DSH-02 CA 12 e 14): minutos da última versão do custo do apontamento (histórico da PRD-04), centro
/// de trabalho da etapa e o nome pelo <see cref="IEmployees"/>; NC das OS do projeto com o custo estimado.
/// </summary>
public sealed class ProjectProductionCostsContract(IProductionDb db, IEmployees employees) : IProjectProductionCosts
{
    public async Task<IReadOnlyList<LaborEntryCostInfo>> LaborEntriesAsync(IReadOnlyCollection<Guid> laborEntryIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(laborEntryIds);
        if (laborEntryIds.Count == 0)
        {
            return [];
        }

        var ids = laborEntryIds.Distinct().ToArray();
        var entries = await db.LaborEntries.AsNoTracking().Where(e => ids.Contains(e.Id)).Select(e => new { e.Id, e.OrderId, e.OrderStepId }).ToListAsync(cancellationToken);
        var orderIds = entries.Select(e => e.OrderId).Distinct().ToArray();
        var stepIds = entries.Select(e => e.OrderStepId).Distinct().ToArray();
        var orders = await db.ProductionOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Number, cancellationToken);
        var steps = await db.ProductionOrderSteps.AsNoTracking().Where(s => stepIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.WorkCenterId, cancellationToken);
        var history = await db.LaborCostHistory.AsNoTracking().Where(h => ids.Contains(h.LaborEntryId)).Select(h => new { h.LaborEntryId, h.CostVersion, h.Minutes }).ToListAsync(cancellationToken);
        var minutes = history.GroupBy(h => h.LaborEntryId).ToDictionary(g => g.Key, g => g.MaxBy(h => h.CostVersion)!.Minutes);
        var centerIds = steps.Values.Where(c => c != null).Select(c => c!.Value).Distinct().ToArray();
        var centers = await db.WorkCenters.AsNoTracking().Where(c => centerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        return entries.Select(e =>
        {
            var center = steps.GetValueOrDefault(e.OrderStepId);
            return new LaborEntryCostInfo(e.Id, e.OrderId, orders.GetValueOrDefault(e.OrderId, string.Empty), center, center is { } c ? centers.GetValueOrDefault(c) : null, minutes.GetValueOrDefault(e.Id));
        }).ToList();
    }

    public async Task<IReadOnlyList<LaborEntryWorkerInfo>> LaborWorkersAsync(IReadOnlyCollection<Guid> laborEntryIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(laborEntryIds);
        if (laborEntryIds.Count == 0)
        {
            return [];
        }

        var ids = laborEntryIds.Distinct().ToArray();
        var rows = await db.LaborEntries.AsNoTracking().Where(e => ids.Contains(e.Id)).Select(e => new { e.Id, e.EmployeeId, e.HourlyCostSnapshot }).ToListAsync(cancellationToken);
        var names = (await employees.FindAsync(rows.Select(r => r.EmployeeId).Distinct().ToList(), cancellationToken)).ToDictionary(e => e.Id, e => e.Name);
        return rows.Select(r => new LaborEntryWorkerInfo(r.Id, names.GetValueOrDefault(r.EmployeeId, string.Empty), r.HourlyCostSnapshot)).ToList();
    }

    public async Task<ProjectNonConformityCost> NonConformitiesAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var costs = await db.NonConformities.AsNoTracking()
            .Where(n => db.ProductionOrders.Any(o => o.Id == n.OrderId && o.ProjectId == projectId))
            .Select(n => n.Cost)
            .ToListAsync(cancellationToken);
        return new ProjectNonConformityCost(costs.Count, costs.Sum(c => c ?? 0m));
    }
}
