using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// Monta a entrada do <see cref="FiniteScheduler"/> (PLN-01a CA 1 a 3, 9 e 17), só lendo: OS de fabricação não concluídas e não
/// canceladas com etapas (a instalação nunca entra), etapas, horas apontadas das etapas em andamento (da tentativa atual da qualidade: o
/// retrabalho começa do zero), projetos (data prometida, assinatura e número, pelo <see cref="IProjects"/>), a sequência dos itens e a
/// capacidade de cada centro, lida uma vez por centro na execução (sem N+1). Usado pelo escritor e pela simulação.
/// </summary>
public sealed class ScheduleInputBuilder(IProductionDb db, IProjects projects, IWorkCenters centers, IWorkCenterCapacity capacity, TimeProvider clock)
{
    private static readonly ProductionOrderStatus[] OpenStatuses =
        [ProductionOrderStatus.Open, ProductionOrderStatus.InProgress, ProductionOrderStatus.Paused, ProductionOrderStatus.Quality, ProductionOrderStatus.ReadyToComplete];

    public async Task<ScheduleInput> BuildAsync(IReadOnlyList<Guid>? queueOrder = null, CancellationToken cancellationToken = default)
    {
        var today = BusinessTime.Today(clock);
        var now = clock.GetUtcNow();
        var orders = await db.ProductionOrders.AsNoTracking()
            .Where(o => o.Type == ProductionOrderType.Fabrication && OpenStatuses.Contains(o.Status))
            .Select(o => new { o.Id, o.Number, o.ProjectId, o.ProjectItemId, o.QualityAttemptStartedAt })
            .ToListAsync(cancellationToken);
        var steps = await db.ProductionOrderSteps.AsNoTracking()
            .Where(s => db.ProductionOrders.Any(o => o.Id == s.OrderId && o.Type == ProductionOrderType.Fabrication && OpenStatuses.Contains(o.Status)))
            .Select(s => new { s.Id, s.OrderId, s.Sequence, s.Name, s.WorkCenterId, s.StandardHours, s.Status })
            .ToListAsync(cancellationToken);
        var byOrder = steps.ToLookup(s => s.OrderId);
        orders = [.. orders.Where(o => byOrder[o.Id].Any())];

        var attemptStart = orders.ToDictionary(o => o.Id, o => o.QualityAttemptStartedAt);
        var running = steps.Where(s => s.Status is ProductionStepStatus.InProgress or ProductionStepStatus.Paused && attemptStart.ContainsKey(s.OrderId)).ToList();
        var logged = await LoggedHoursAsync(running.Select(s => (s.Id, attemptStart[s.OrderId])).ToList(), now, cancellationToken);

        var projectIds = orders.Select(o => o.ProjectId).Distinct().ToList();
        var summaries = await projects.FindManyAsync(projectIds, cancellationToken);
        var items = (await projects.FindItemsAsync(orders.Where(o => o.ProjectItemId != null).Select(o => o.ProjectItemId!.Value).Distinct().ToList(), cancellationToken))
            .ToDictionary(i => i.Id, i => i.Sequence);
        var positions = (queueOrder ?? []).Select((id, i) => (id, i)).GroupBy(x => x.id).ToDictionary(g => g.Key, g => g.First().i + 1);
        var queue = summaries.Select(p => new ScheduleProjectInput(p.Id, p.Number, p.PromisedDate, p.SignedAt, positions.TryGetValue(p.Id, out var at) ? at : null)).ToList();

        var input = orders.Select(o => new ScheduleOrderInput(o.Id, o.Number, o.ProjectId, o.ProjectItemId is { } item ? items.GetValueOrDefault(item) : 0,
            [.. byOrder[o.Id].Select(s => new ScheduleStepInput(s.Id, s.Sequence, s.Name, s.WorkCenterId, s.StandardHours ?? 0m, logged.GetValueOrDefault(s.Id),
                s.Status ?? ProductionStepStatus.Waiting))])).ToList();
        var centerIds = steps.Where(s => s.WorkCenterId != null && s.Status != ProductionStepStatus.Done).Select(s => s.WorkCenterId!.Value).Distinct().Order().ToList();
        return new ScheduleInput(today, queue, input, await CentersAsync(centerIds, today, cancellationToken));
    }

    /// <summary>A capacidade de cada centro usado, uma leitura por centro (CA 17); o centro excluído entra sem horas.</summary>
    public async Task<IReadOnlyList<ScheduleCenterInput>> CentersAsync(IReadOnlyList<Guid> centerIds, DateOnly today, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(centerIds);
        var names = (await centers.FindAsync(centerIds, cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
        var to = today.AddDays(FiniteScheduler.HorizonDays - 1);
        var result = new List<ScheduleCenterInput>(centerIds.Count);
        foreach (var id in centerIds)
        {
            var days = names.ContainsKey(id) ? await capacity.GetAvailableHoursAsync(id, today, to, cancellationToken) : null;
            result.Add(new ScheduleCenterInput(id, names.GetValueOrDefault(id) ?? "excluído", days?.Days ?? []));
        }

        return result;
    }

    /// <summary>Horas apontadas (CA 3): Σ minutos líquidos ÷ 60 dos apontamentos fechados e do aberto até agora, desde o início da tentativa.</summary>
    private async Task<Dictionary<Guid, decimal>> LoggedHoursAsync(List<(Guid StepId, DateTimeOffset? Since)> running, DateTimeOffset now, CancellationToken ct)
    {
        if (running.Count == 0)
        {
            return [];
        }

        var ids = running.Select(r => r.StepId).ToList();
        var since = running.ToDictionary(r => r.StepId, r => r.Since);
        var entries = (await db.LaborEntries.AsNoTracking().Where(e => ids.Contains(e.OrderStepId)).Select(e => new { e.Id, e.OrderStepId, e.StartedAt, e.EndedAt }).ToListAsync(ct))
            .Where(e => since[e.OrderStepId] is not { } start || e.StartedAt >= start)
            .ToList();
        var entryIds = entries.Select(e => e.Id).ToList();
        var pauses = (await db.StepPauses.AsNoTracking().Where(p => entryIds.Contains(p.LaborEntryId)).Select(p => new { p.LaborEntryId, p.StartedAt, p.EndedAt }).ToListAsync(ct))
            .ToLookup(p => p.LaborEntryId, p => new PauseSpan(p.StartedAt, p.EndedAt));
        return entries.GroupBy(e => e.OrderStepId)
            .ToDictionary(g => g.Key, g => Math.Round(g.Sum(e => LaborRules.NetMinutes(e.StartedAt, e.EndedAt, pauses[e.Id], now)) / 60m, 4, MidpointRounding.AwayFromZero));
    }
}
