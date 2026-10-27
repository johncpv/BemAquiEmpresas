using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// Implementação de <see cref="IProductionSchedule"/> (PLN-01a CA 15): lê as tabelas do plano pelas vistas sem rastreamento do
/// <see cref="IProductionDb"/> e simula rodando o <see cref="FiniteScheduler"/> em memória, sem gravar nada.
/// </summary>
public sealed class ProductionScheduleContract(
    IProductionDb db, ScheduleInputBuilder builder, IPlanningQueue queue, ProjectForecastBuilder forecasts, BemAqui.Commercial.Contracts.IProjects projects) : IProductionSchedule
{
    public async Task<IReadOnlyList<OrderForecastInfo>> ForOrdersAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderIds);
        if (orderIds.Count == 0)
        {
            return [];
        }

        var rows = await db.ScheduleStepForecastsView.Where(f => orderIds.Contains(f.ProductionOrderId))
            .Select(f => new ScheduleViews.Row(f.StepId, f.ProductionOrderId, f.Sequence, f.Hours, f.PlannedStart, f.PlannedEnd, f.NoForecastReason)).ToListAsync(cancellationToken);
        return [.. ScheduleViews.ByOrder(rows).Values];
    }

    public async Task<IReadOnlyList<ProjectFabricationForecast>> FabricationEndByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectIds);
        if (projectIds.Count == 0)
        {
            return [];
        }

        var rows = await db.ScheduleStepForecastsView
            .Join(db.ProductionOrders.Where(o => projectIds.Contains(o.ProjectId)), f => f.ProductionOrderId, o => o.Id, (f, o) => new { f, o.ProjectId })
            .Select(x => new { x.ProjectId, Row = new ScheduleViews.Row(x.f.StepId, x.f.ProductionOrderId, x.f.Sequence, x.f.Hours, x.f.PlannedStart, x.f.PlannedEnd, x.f.NoForecastReason) })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.ProjectId).Select(g => Project(g.Key, ScheduleViews.ByOrder(g.Select(r => r.Row)).Values)).ToList();
    }

    public async Task<IReadOnlyList<ScheduleAllocationInfo>> AllocationsAsync(DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? workCenterIds = null, CancellationToken cancellationToken = default)
    {
        var query = db.ScheduleAllocationsView.Where(a => a.Date >= from && a.Date <= to);
        if (workCenterIds is { Count: > 0 })
        {
            query = query.Where(a => workCenterIds.Contains(a.WorkCenterId));
        }

        return await query.OrderBy(a => a.Date).ThenBy(a => a.WorkCenterId).ThenBy(a => a.Id)
            .Select(a => new ScheduleAllocationInfo(a.StepId, a.ProductionOrderId, a.WorkCenterId, a.Date, a.Hours)).ToListAsync(cancellationToken);
    }

    public async Task<ScheduleRunInfo?> LastRunAsync(CancellationToken cancellationToken = default)
    {
        var run = await db.ScheduleRunsView.Where(r => r.Status != ScheduleRunStatus.Pending && r.FinishedAt != null)
            .OrderByDescending(r => r.FinishedAt).ThenByDescending(r => r.Id).FirstOrDefaultAsync(cancellationToken);
        return run is null ? null : ScheduleService.Info(run);
    }

    public async Task<ScheduleSimulation> SimulateAsync(IReadOnlyList<Guid> queueOrder, SimulatedProject? extraProject = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queueOrder);
        var input = await builder.BuildAsync(queueOrder, cancellationToken);
        if (extraProject is not null)
        {
            input = await WithVirtualAsync(input, extraProject, cancellationToken);
        }

        var result = FiniteScheduler.Run(input);
        var steps = result.Steps.ToLookup(s => s.OrderId);
        var orders = result.Orders.Select(o => new OrderForecastInfo(o.OrderId, o.Start, o.End, o.NoForecastReason,
            [.. steps[o.OrderId].Select(s => new StepForecastInfo(s.StepId, s.Sequence, s.Hours, s.Start, s.End, s.NoForecastReason))])).ToList();
        return new ScheduleSimulation(orders, [.. result.Projects.Select(p => new ProjectFabricationForecast(p.ProjectId, p.FabricationEnd, p.NoForecastReason))]);
    }

    /// <summary>
    /// PLN-05 CA 3 a 5: uma leitura da entrada do motor e duas execuções em memória (a fila de hoje e a fila com o virtual na posição), como a
    /// prévia da PLN-02; a entrega de cada projeto real sai do mesmo cálculo do recálculo (fim da fabricação + instalação) e a do virtual do
    /// prazo de instalação quando o orçamento tem custo de instalação. Só lê a fila (<see cref="IPlanningQueue"/>, sem o advisory lock).
    /// </summary>
    public async Task<DeliverySimulation> SimulateDeliveryAsync(SimulatedProject project, bool hasInstallationBudget, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var order = await queue.OrderAsync(cancellationToken);
        var position = DeliverySimulationRules.Position(project.QueuePosition, order.Count);
        var baseline = await builder.BuildAsync(order, cancellationToken);
        var ranks = DeliverySimulationRules.ShiftedPositions(order, position);
        var shifted = baseline with
        {
            Projects = [.. baseline.Projects.Select(p => p with { QueuePosition = ranks.TryGetValue(p.ProjectId, out var at) ? at : null })],
        };
        var withVirtual = await WithVirtualAsync(shifted, project with { QueuePosition = position }, cancellationToken);
        var after = FiniteScheduler.Run(withVirtual);
        var results = await forecasts.BuildManyAsync([FiniteScheduler.Run(baseline), after], cancellationToken);
        var simulated = await forecasts.VirtualAsync(after, project.ProjectId, hasInstallationBudget, cancellationToken);

        var before = results[0].ToDictionary(f => f.ProjectId, f => f.ForecastDate);
        var promised = (await projects.FindManyAsync([.. results[1].Select(f => f.ProjectId)], cancellationToken)).ToDictionary(p => p.Id, p => p.PromisedDate);
        var late = results[1]
            .Where(f => PlanningQueueService.Worsened(promised.GetValueOrDefault(f.ProjectId), before.GetValueOrDefault(f.ProjectId), f.ForecastDate))
            .OrderBy(f => ranks.TryGetValue(f.ProjectId, out var at) ? at : int.MaxValue).ThenBy(f => f.ProjectId)
            .Select(f => f.ProjectId).ToList();
        return new DeliverySimulation(order.Count, position, baseline.Today, simulated.FabricationEnd, simulated.ForecastDate, simulated.Reason, hasInstallationBudget, late);
    }

    public async Task<IReadOnlyList<ProjectDeliveryForecast>> ProjectForecastsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectIds);
        if (projectIds.Count == 0)
        {
            return [];
        }

        return await db.ScheduleProjectForecastsView.Where(f => projectIds.Contains(f.ProjectId))
            .Select(f => new ProjectDeliveryForecast(f.ProjectId, f.ForecastDate, f.FabricationEnd, f.Reason, f.ReasonOrderId, f.ComputedAt)).ToListAsync(cancellationToken);
    }

    /// <summary>O projeto virtual entra na fila com as OS e etapas dele (ids novos); os centros que ainda não estavam na entrada são lidos.</summary>
    private async Task<ScheduleInput> WithVirtualAsync(ScheduleInput input, SimulatedProject extra, CancellationToken ct)
    {
        var orders = extra.Orders.Select((steps, i) => new ScheduleOrderInput(Guid.CreateVersion7(), $"{extra.Number}-{i + 1}", extra.ProjectId, i + 1,
            [.. steps.Select((s, k) => new ScheduleStepInput(Guid.CreateVersion7(), k + 1, s.Name, s.WorkCenterId, Math.Max(s.StandardHours, 0m), 0m,
                k == 0 ? ProductionStepStatus.Ready : ProductionStepStatus.Waiting))])).ToList();
        var known = input.Centers.Select(c => c.WorkCenterId).ToHashSet();
        var missing = orders.SelectMany(o => o.Steps).Where(s => s.WorkCenterId is { } id && !known.Contains(id)).Select(s => s.WorkCenterId!.Value).Distinct().Order().ToList();
        var centers = missing.Count == 0 ? input.Centers : [.. input.Centers, .. await builder.CentersAsync(missing, input.Today, ct)];
        return input with
        {
            Projects = [.. input.Projects, new ScheduleProjectInput(extra.ProjectId, extra.Number, extra.PromisedDate, input.Today, extra.QueuePosition)],
            Orders = [.. input.Orders, .. orders],
            Centers = centers,
        };
    }

    private static ProjectFabricationForecast Project(Guid projectId, IEnumerable<OrderForecastInfo> orders)
    {
        var list = orders.ToList();
        return list.FirstOrDefault(o => o.NoForecastReason is not null) is { } blocked
            ? new ProjectFabricationForecast(projectId, null, blocked.NoForecastReason)
            : new ProjectFabricationForecast(projectId, list.Max(o => o.PlannedEnd), null);
    }
}
