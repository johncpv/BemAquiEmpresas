using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed partial class ProjectService
{
    /// <summary>
    /// Previsão de entrega dos projetos dados (PLN-01b CA 8 e 9): a linha de <c>project_forecasts</c>, o fim da instalação digitado para quem já
    /// está em <c>Fabricated</c> ou <c>Installing</c> (só esses leem as OS de instalação) e o número da OS do motivo, de uma vez.
    /// </summary>
    internal async Task<Dictionary<Guid, ProjectForecastView>> ForecastViewsAsync(IReadOnlyCollection<(Guid Id, ProjectStatus Status, DateOnly? Promised)> list, CancellationToken ct)
    {
        var ids = list.Select(p => p.Id).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await db.ProjectForecasts.AsNoTracking().Where(f => ids.Contains(f.ProjectId)).ToDictionaryAsync(f => f.ProjectId, ct);
        var installationEnds = new Dictionary<Guid, DateOnly>();
        foreach (var project in list.Where(p => ProjectDelivery.InstallationPhase(p.Status)))
        {
            var ends = (await installations.ByProjectAsync(project.Id, ct)).Where(o => o.Status != BemAqui.Production.Contracts.InstallationState.Cancelled).Select(o => o.PlannedEnd).ToList();
            if (ends.Count > 0)
            {
                installationEnds[project.Id] = ends.Max();
            }
        }

        var reasonOrders = rows.Values.Where(r => r.ForecastDate is null && r.ReasonOrderId is not null).Select(r => r.ReasonOrderId!.Value).Distinct().ToList();
        var numbers = reasonOrders.Count == 0 ? [] : (await productionOrders.FindManyAsync(reasonOrders, ct)).ToDictionary(o => o.Id, o => o.Number);
        var today = BusinessTime.Today(clock);
        var result = new Dictionary<Guid, ProjectForecastView>();
        foreach (var project in list)
        {
            var row = rows.GetValueOrDefault(project.Id);
            DateOnly? installationEnd = installationEnds.TryGetValue(project.Id, out var end) ? end : null;
            var number = row?.ReasonOrderId is { } orderId ? numbers.GetValueOrDefault(orderId) : null;
            result[project.Id] = ProjectDelivery.Assess(project.Status, project.Promised, row, installationEnd, today, number);
        }

        return result;
    }

    async Task<IReadOnlyList<ProjectSummary>> IProjects.PlanningCandidatesAsync(CancellationToken cancellationToken) =>
        await Summaries(db.Projects.Where(p => (p.Status == ProjectStatus.Planned || p.Status == ProjectStatus.InProgress)
            && db.Contracts.Any(c => c.Id == p.ContractId && c.Status != ContractStatus.Cancelled))).ToListAsync(cancellationToken);

    async Task<IReadOnlyDictionary<Guid, decimal>> IProjects.BudgetedManyAsync(IReadOnlyCollection<Guid> projectIds, CostType costType, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projectIds);
        if (projectIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        return await db.ProjectBudgets.AsNoTracking().Where(b => projectIds.Contains(b.ProjectId) && b.CostType == costType).GroupBy(b => b.ProjectId)
            .Select(g => new { g.Key, Total = g.Sum(b => b.BudgetedAmount) }).ToDictionaryAsync(g => g.Key, g => g.Total, cancellationToken);
    }

    async Task<IReadOnlyList<ProjectForecastSummary>> IProjects.ForecastsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projectIds);
        if (projectIds.Count == 0)
        {
            return [];
        }

        var projects = await db.Projects.AsNoTracking().Where(p => projectIds.Contains(p.Id)).Select(p => new { p.Id, p.Status, p.PromisedDate }).ToListAsync(cancellationToken);
        var views = await ForecastViewsAsync([.. projects.Select(p => (p.Id, p.Status, p.PromisedDate))], cancellationToken);
        return projects.Select(p =>
        {
            var v = views[p.Id];
            return new ProjectForecastSummary(p.Id, p.PromisedDate, v.ForecastDate, v.FabricationEnd, v.Reason, v.SlackDays, v.Risk, v.DelayDays, v.ComputedAt);
        }).ToList();
    }
}
