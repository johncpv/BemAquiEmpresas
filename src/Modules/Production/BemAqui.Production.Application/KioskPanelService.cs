using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// Dados do Painel da TV (PNL-01b, ADR 0030 §5): as regras do quadro da PRD-02 (etapa atual, coluna pelo código, linha pelo centro da
/// etapa atual, <see cref="ProductionOrderRisk"/>) com o filtro de linha do escopo, numa consulta por tabela (sem N+1) e só com as OS da
/// linha a partir dos centros. O cliente, o produto, valores e custos nunca são lidos; o operador sai pelo primeiro nome e inicial.
/// </summary>
public sealed class KioskPanelService(
    IProductionDb db, IProjects projects, IWorkCenters workCenters, BemAqui.HR.Contracts.IEmployees employees, IPlanningQueue queue, TimeProvider clock) : IKioskPanel
{
    private static readonly ProductionOrderStatus[] OpenStatuses =
        [ProductionOrderStatus.Open, ProductionOrderStatus.InProgress, ProductionOrderStatus.Paused, ProductionOrderStatus.Quality, ProductionOrderStatus.ReadyToComplete];

    public async Task<KioskBoard> BoardAsync(string? line, CancellationToken cancellationToken = default)
    {
        var built = await BuildAsync(null, line, cancellationToken);
        var cards = KioskPanelRules.Ordered(built.Select(b => b.Card)).ToList();
        var columns = new List<KioskColumn>();
        var counts = cards.GroupBy(c => c.ColumnKey, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        if (counts.TryGetValue(BoardRules.NoRoutingKey, out var noRouting))
        {
            columns.Add(new KioskColumn(BoardRules.NoRoutingKey, null, BoardRules.NoRoutingName, noRouting));
        }

        columns.AddRange(BoardRules.StepColumns(built.Select(b => b.Steps)).Where(c => counts.ContainsKey(c.Code)).Select(c => new KioskColumn(c.Code, c.Code, c.Name, counts[c.Code])));
        if (counts.TryGetValue(BoardRules.ReadyKey, out var ready))
        {
            columns.Add(new KioskColumn(BoardRules.ReadyKey, null, BoardRules.ReadyName, ready));
        }

        return new KioskBoard(Line(line), cards.Count, cards.Count(c => c.Risk == DeliveryRisk.Late), cards.Count(c => c.Risk == DeliveryRisk.AtRisk), columns, cards, clock.GetUtcNow());
    }

    public async Task<KioskCard?> CardAsync(Guid orderId, string? line, CancellationToken cancellationToken = default) =>
        (await BuildAsync(orderId, line, cancellationToken)).SingleOrDefault()?.Card;

    public async Task<KioskDelays> DelaysAsync(string? line, CancellationToken cancellationToken = default)
    {
        var built = await BuildAsync(null, line, cancellationToken);
        var rows = KioskPanelRules.Ordered(built.Where(b => b.Card.Risk != DeliveryRisk.None).Select(b => new KioskDelayRow(
            b.Card.Id, b.Card.Number, b.Card.ProjectNumber, b.Card.ProjectName, b.Card.StepCode, b.Card.StepName, b.Card.Operator, b.Card.Risk,
            KioskPanelRules.Days(b.Card.Risk, b.Card.DelayDays, b.Promised, b.Forecast), b.Defining?.Name, b.Defining?.End))).ToList();
        return new KioskDelays(Line(line), [.. rows.Take(KioskPanelRules.ViewRows)], rows.Count);
    }

    public async Task<KioskQueueView> QueueAsync(string? line, CancellationToken cancellationToken = default)
    {
        var order = await queue.OrderAsync(cancellationToken);
        if (order.Count == 0)
        {
            return new KioskQueueView(Line(line), [], 0);
        }

        var ids = order.ToList();
        var steps = await db.ProductionOrderSteps.AsNoTracking()
            .Join(db.ProductionOrders.Where(o => o.Type == ProductionOrderType.Fabrication && OpenStatuses.Contains(o.Status) && ids.Contains(o.ProjectId)),
                s => s.OrderId, o => o.Id, (s, o) => new { o.ProjectId, s.Id, s.WorkCenterId, s.Status })
            .Where(s => s.Status != ProductionStepStatus.Done)
            .ToListAsync(cancellationToken);
        var wanted = Line(line);
        var lines = await LinesAsync(steps.Where(s => s.WorkCenterId is not null).Select(s => s.WorkCenterId!.Value), cancellationToken);
        var inLine = steps.Where(s => wanted is null || (s.WorkCenterId is { } c && Same(lines.GetValueOrDefault(c), wanted))).ToList();
        var stepIds = inLine.Select(s => s.Id).ToList();
        var starts = stepIds.Count == 0 ? [] : await db.ScheduleStepForecastsView.Where(f => stepIds.Contains(f.StepId) && f.PlannedStart != null)
            .Select(f => new { f.StepId, f.PlannedStart }).ToDictionaryAsync(f => f.StepId, f => f.PlannedStart, cancellationToken);
        var withOrders = wanted is null
            ? (await db.ProductionOrders.AsNoTracking().Where(o => o.Type == ProductionOrderType.Fabrication && OpenStatuses.Contains(o.Status) && ids.Contains(o.ProjectId))
                .Select(o => o.ProjectId).Distinct().ToListAsync(cancellationToken)).ToHashSet()
            : inLine.Select(s => s.ProjectId).ToHashSet();
        var positions = ids.Select((id, i) => (id, Position: i + 1)).Where(p => withOrders.Contains(p.id)).ToList();
        var shown = positions.Take(KioskPanelRules.ViewRows).ToList();
        var shownIds = shown.Select(p => p.id).ToList();
        var summaries = (await projects.FindManyAsync(shownIds, cancellationToken)).ToDictionary(p => p.Id);
        var forecasts = await ProductionBoardService.ForecastsAsync(db, shownIds, cancellationToken);
        var firstStart = inLine.GroupBy(s => s.ProjectId).ToDictionary(g => g.Key, g => g.Select(s => starts.GetValueOrDefault(s.Id)).Where(d => d is not null).Min());
        var today = BusinessTime.Today(clock);
        var rows = shown.Select(p =>
        {
            var summary = summaries.GetValueOrDefault(p.id);
            var promised = summary?.PromisedDate;
            var forecast = forecasts.GetValueOrDefault(p.id);
            return new KioskQueueRow(p.Position, p.id, summary?.Number ?? string.Empty, KioskPanelRules.ShortName(summary?.Name), firstStart.GetValueOrDefault(p.id), forecast,
                promised, DeliveryRiskRules.SlackDays(promised, forecast), DeliveryRiskRules.Evaluate(promised, forecast, today, false).Risk);
        }).ToList();
        return new KioskQueueView(wanted, rows, positions.Count);
    }

    private sealed record StepRow(Guid OrderId, Guid Id, int Sequence, string? Code, string Name, Guid? WorkCenterId, decimal? StandardHours, ProductionStepStatus? Status, DateTimeOffset? ActualStart);

    private sealed record Defining(string Name, DateOnly End);

    private sealed record Built(KioskCard Card, IReadOnlyList<BoardStep> Steps, DateOnly? Promised, DateOnly? Forecast, Defining? Defining);

    /// <summary>Cartões das OS abertas da linha (ou de uma OS só): primeiro as etapas e os centros, depois o resto só para as OS da linha.</summary>
    private async Task<List<Built>> BuildAsync(Guid? orderId, string? line, CancellationToken ct)
    {
        var query = db.ProductionOrders.AsNoTracking().Where(o => o.Type == ProductionOrderType.Fabrication && OpenStatuses.Contains(o.Status));
        if (orderId is { } only)
        {
            query = query.Where(o => o.Id == only);
        }

        var orders = await query.OrderBy(o => o.Priority).ThenBy(o => o.CreatedAt).ThenBy(o => o.Id)
            .Select(o => new { o.Id, o.Number, o.Status, o.ProjectId }).ToListAsync(ct);
        var ids = orders.Select(o => o.Id).ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var steps = (await db.ProductionOrderSteps.AsNoTracking().Where(s => ids.Contains(s.OrderId)).OrderBy(s => s.Sequence)
                .Select(s => new StepRow(s.OrderId, s.Id, s.Sequence, s.Code, s.Name, s.WorkCenterId, s.StandardHours, s.Status, s.ActualStart)).ToListAsync(ct))
            .ToLookup(s => s.OrderId);
        var lines = await LinesAsync(steps.SelectMany(g => g).Where(s => s.WorkCenterId is not null).Select(s => s.WorkCenterId!.Value), ct);
        var wanted = Line(line);

        var candidates = orders.Select(o =>
        {
            var mine = steps[o.Id].ToList();
            var board = mine.Select(s => new BoardStep(s.Code, s.Name, s.Sequence, s.Status)).ToList();
            var index = BoardRules.CurrentIndex(board);
            var current = index >= 0 ? mine[index] : null;
            // Linha: centro da etapa atual; na "Pronta para concluir", o da última etapa (a mesma regra do Kanban).
            var lineStep = current ?? (mine.Count > 0 ? mine[^1] : null);
            var orderLine = lineStep?.WorkCenterId is { } center ? lines.GetValueOrDefault(center) : null;
            return (Order: o, Steps: mine, Board: board, Current: current, Line: orderLine);
        }).Where(c => wanted is null || Same(c.Line, wanted)).ToList();
        if (candidates.Count == 0)
        {
            return [];
        }

        var inLine = candidates.Select(c => c.Order.Id).ToList();
        var currentIds = candidates.Where(c => c.Current is not null).Select(c => c.Current!.Id).ToList();
        var now = clock.GetUtcNow();
        var applied = await LaborReader.AppliedMinutesAsync(db, currentIds, now, ct);
        var open = currentIds.Count == 0 ? [] : await db.LaborEntries.AsNoTracking().Where(e => currentIds.Contains(e.OrderStepId) && e.EndedAt == null)
            .OrderBy(e => e.StartedAt).ThenBy(e => e.Id)
            .Select(e => new { e.OrderStepId, e.EmployeeId, Paused = db.StepPauses.Where(p => p.LaborEntryId == e.Id && p.EndedAt == null).Select(p => (PauseReason?)p.Reason).FirstOrDefault() })
            .ToListAsync(ct);
        var onStep = open.ToLookup(e => e.OrderStepId);
        var names = (await employees.FindAsync(open.Select(e => e.EmployeeId).Distinct().ToList(), ct)).ToDictionary(e => e.Id, e => e.Name);
        var plan = (await db.ScheduleStepForecastsView.Where(f => inLine.Contains(f.ProductionOrderId))
                .Select(f => new { f.ProductionOrderId, f.StepId, f.Sequence, f.PlannedEnd }).ToListAsync(ct))
            .ToLookup(f => f.ProductionOrderId);
        var projectIds = candidates.Select(c => c.Order.ProjectId).Distinct().ToList();
        var byProject = (await projects.FindManyAsync(projectIds, ct)).ToDictionary(p => p.Id);
        var forecasts = await ProductionBoardService.ForecastsAsync(db, projectIds, ct);
        var today = BusinessTime.Today(clock);

        return candidates.Select(c =>
        {
            var project = byProject.GetValueOrDefault(c.Order.ProjectId);
            var forecast = forecasts.GetValueOrDefault(c.Order.ProjectId);
            var risk = ProductionOrderRisk.Evaluate(project?.PromisedDate, forecast, today, c.Order.Status);
            var current = c.Current;
            var people = current is null ? [] : onStep[current.Id].ToList();
            var working = people.Where(e => e.Paused is null).ToList();
            var pausedReason = people.Count > 0 && working.Count == 0 ? ShopfloorService.PauseLabel(people[0].Paused!.Value) : null;
            var who = KioskPanelRules.Operators([.. working.Select(e => names.GetValueOrDefault(e.EmployeeId, string.Empty))]);
            var started = current?.Status is ProductionStepStatus.InProgress or ProductionStepStatus.Paused ? current.ActualStart : null;
            var orderPlan = plan[c.Order.Id].ToList();
            var plannedEnd = current is null ? null : orderPlan.FirstOrDefault(f => f.StepId == current.Id)?.PlannedEnd;
            var last = orderPlan.Where(f => f.PlannedEnd is not null).OrderByDescending(f => f.PlannedEnd).ThenByDescending(f => f.Sequence).FirstOrDefault();
            var definingStep = last is null ? null : c.Steps.FirstOrDefault(s => s.Id == last.StepId);
            var card = new KioskCard(
                c.Order.Id, c.Order.Number, project?.Number ?? string.Empty, KioskPanelRules.ShortName(project?.Name), BoardRules.ColumnKey(c.Board),
                current?.Code?.Trim().ToUpperInvariant(), current?.Name, who, pausedReason, started,
                current is null ? 0m : LaborRules.Hours(applied.GetValueOrDefault(current.Id)), current?.StandardHours ?? 0m, plannedEnd, risk.Risk, risk.DelayDays);
            return new Built(card, c.Board, project?.PromisedDate, forecast, definingStep is null ? null : new Defining(definingStep.Name, last!.PlannedEnd!.Value));
        }).ToList();
    }

    private async Task<Dictionary<Guid, string?>> LinesAsync(IEnumerable<Guid> centerIds, CancellationToken ct)
    {
        var ids = centerIds.Distinct().ToList();
        return ids.Count == 0 ? [] : (await workCenters.FindAsync(ids, ct)).ToDictionary(c => c.Id, c => c.Line);
    }

    private static string? Line(string? line) => string.IsNullOrWhiteSpace(line) ? null : line.Trim();

    private static bool Same(string? a, string b) => string.Equals(a?.Trim(), b, StringComparison.OrdinalIgnoreCase);
}
