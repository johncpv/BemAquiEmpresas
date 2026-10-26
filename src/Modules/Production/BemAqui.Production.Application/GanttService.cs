using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// Cronograma (PLN-03): por projeto, na ordem da fila (PLN-02), com as OS e as etapas do plano gravado (<c>schedule_step_forecasts</c>), as
/// datas reais das etapas já feitas, a previsão de entrega (<c>schedule_project_forecasts</c>, PLN-01b), o prometido e o risco pela função
/// única, e a OS de instalação só de leitura com as datas digitadas (CA 1 a 4); por centro de trabalho, com as etapas alocadas, as
/// sub-linhas, a ocupação de cada dia e os feriados e paradas (CA 5). Só lê: nada é recalculado nem gravado (ADR 0029). Sem custo.
/// </summary>
public sealed class GanttService(
    IProductionDb db, PlanningQueueStore queue, IProjects projects, IParties parties, IWorkCenters centers, IWorkCenterCapacity capacity, TimeProvider clock)
{
    public const int MaxLineLength = 120;

    /// <summary>Etapa ainda sem linha no plano (o plano não rodou depois de a OS nascer).</summary>
    public const string AwaitingPlanReason = "Aguardando o cálculo do plano";

    public static Error InvalidPeriod() =>
        Error.Validation("planning_gantt.period", $"Escolha um período de até 12 meses ({GanttRules.MaxDays} dias), com o fim depois do início.");

    public static Error InvalidLine() => Error.Validation("planning_gantt.line", $"A linha tem até {MaxLineLength} caracteres.");

    public static Error ProjectNotFound() => Error.NotFound("project_not_found", "Projeto não encontrado.");

    /// <summary>
    /// Por projeto (CA 1 a 4, 6, 7 e 9): os projetos da fila cujas datas caem no período (ou ainda sem datas, para mostrar o motivo), com os
    /// filtros de projeto, cliente, linha, centro e "Só em risco".
    /// </summary>
    public async Task<Result<GanttProjectsView>> ProjectsAsync(GanttQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Line is { Length: > MaxLineLength })
        {
            return InvalidLine();
        }

        var today = BusinessTime.Today(clock);
        if (GanttRules.Resolve(today, query.From, query.To) is not { } period)
        {
            return InvalidPeriod();
        }

        var order = (await queue.SnapshotAsync(cancellationToken)).Order;
        var active = await centers.ActiveAsync(cancellationToken);
        var built = await BuildAsync(order, today, cancellationToken);
        var scope = CenterScope(active, query.Line, query.WorkCenterId);

        var rows = built.Where(p => Visible(p, period)
                && (query.ProjectId is not { } projectId || p.ProjectId == projectId)
                && (query.PartyId is not { } partyId || p.PartyId == partyId)
                && (!query.AtRisk || p.Risk != DeliveryRisk.None)
                && (scope is null || p.Orders.SelectMany(o => o.Steps).Any(s => s.WorkCenterId is { } c && scope.Contains(c))))
            .ToList();
        var projectOptions = built.Select(p => new GanttOption(p.ProjectId, string.IsNullOrEmpty(p.ProjectName) ? p.ProjectNumber : $"{p.ProjectNumber} · {p.ProjectName}")).ToList();
        var partyOptions = built.Where(p => p.PartyId is not null).GroupBy(p => p.PartyId!.Value)
            .Select(g => new GanttOption(g.Key, g.First().PartyName ?? "Cliente")).OrderBy(o => o.Label, StringComparer.CurrentCulture).ToList();
        return new GanttProjectsView(today, period.From, period.To, await ComputedAtAsync(cancellationToken), rows, projectOptions, partyOptions, Lines(active),
            [.. active.Select(c => new GanttOption(c.Id, c.Name))]);
    }

    /// <summary>
    /// Cronograma de um projeto (Projeto 360, CA 11 e 14): o projeto validado pelo <see cref="IProjects"/> (404 se não existe), com todas as
    /// OS não canceladas, no período pedido ou, sem período, no padrão alargado para caber o projeto inteiro.
    /// </summary>
    public async Task<Result<GanttProjectsView>> ProjectAsync(Guid projectId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        if (await projects.FindAsync(projectId, cancellationToken) is null)
        {
            return ProjectNotFound();
        }

        var today = BusinessTime.Today(clock);
        var order = (await queue.SnapshotAsync(cancellationToken)).Order;
        var built = (await BuildAsync([projectId], today, cancellationToken)).Single();
        var position = order.ToList().IndexOf(projectId) + 1;
        var project = built with { Position = position };
        GanttPeriod period;
        if (from is null && to is null)
        {
            period = GanttRules.ProjectPeriod(today, [project.Start, project.ForecastDate, project.PromisedDate, .. project.Orders.Select(o => o.End)]);
        }
        else if (GanttRules.Resolve(today, from, to) is { } chosen)
        {
            period = chosen;
        }
        else
        {
            return InvalidPeriod();
        }

        var active = await centers.ActiveAsync(cancellationToken);
        return new GanttProjectsView(today, period.From, period.To, await ComputedAtAsync(cancellationToken), [project],
            [new GanttOption(project.ProjectId, project.ProjectNumber)], [], Lines(active), [.. active.Select(c => new GanttOption(c.Id, c.Name))]);
    }

    /// <summary>
    /// Por centro (CA 5): uma linha por centro ativo (filtrado pela linha ou pelo centro) com as etapas previstas no período, nas sub-linhas,
    /// e os dias com horas alocadas ÷ capacidade lida agora (faixas da PLN-04), os dias sem capacidade e os feriados e paradas com o nome.
    /// </summary>
    public async Task<Result<GanttCentersView>> CentersAsync(GanttQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Line is { Length: > MaxLineLength })
        {
            return InvalidLine();
        }

        var today = BusinessTime.Today(clock);
        if (GanttRules.Resolve(today, query.From, query.To) is not { } period)
        {
            return InvalidPeriod();
        }

        var active = await centers.ActiveAsync(cancellationToken);
        var scope = active.Where(c => (query.WorkCenterId is not { } id || c.Id == id)
            && (string.IsNullOrWhiteSpace(query.Line) || string.Equals(c.Line, query.Line.Trim(), StringComparison.Ordinal))).ToList();
        var computedAt = await ComputedAtAsync(cancellationToken);
        var ids = scope.Select(c => c.Id).ToList();
        if (ids.Count == 0)
        {
            return new GanttCentersView(today, period.From, period.To, computedAt, [], Lines(active), active.Count);
        }

        var (first, last) = (period.From, period.To);
        var bars = await (
                from f in db.ScheduleStepForecastsView
                where f.PlannedStart != null && f.PlannedEnd != null && f.PlannedStart <= last && f.PlannedEnd >= first
                join s in db.ProductionOrderSteps.AsNoTracking() on f.StepId equals s.Id
                where s.WorkCenterId != null && ids.Contains(s.WorkCenterId.Value)
                join o in db.ProductionOrders.AsNoTracking() on f.ProductionOrderId equals o.Id
                select new
                {
                    s.Id, s.Name, s.Code, CenterId = s.WorkCenterId!.Value, s.StandardHours, s.Status, s.ActualStart, f.Hours, Start = f.PlannedStart!.Value, End = f.PlannedEnd!.Value,
                    OrderId = o.Id, o.Number, o.ProjectId,
                })
            .ToListAsync(cancellationToken);
        var daily = (await db.ScheduleAllocationsView.Where(a => ids.Contains(a.WorkCenterId) && a.Date >= first && a.Date <= last)
                .GroupBy(a => new { a.WorkCenterId, a.Date })
                .Select(g => new { g.Key.WorkCenterId, g.Key.Date, Hours = g.Sum(a => a.Hours) })
                .ToListAsync(cancellationToken))
            .ToLookup(d => d.WorkCenterId);
        var calendars = await db.WorkCenters.AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.CalendarId }).ToDictionaryAsync(c => c.Id, c => c.CalendarId, cancellationToken);
        var calendarIds = calendars.Values.Distinct().ToList();
        var exceptions = (await db.CalendarExceptions.AsNoTracking().Where(e => calendarIds.Contains(e.CalendarId) && e.Date >= first && e.Date <= last)
                .Select(e => new { e.CalendarId, e.Date, e.Type, e.Description }).ToListAsync(cancellationToken))
            .ToLookup(e => e.CalendarId);
        var projectInfo = (await projects.FindManyAsync([.. bars.Select(b => b.ProjectId).Distinct()], cancellationToken)).ToDictionary(p => p.Id);
        var byCenter = bars.ToLookup(b => b.CenterId);

        var rows = new List<GanttCenterRow>(scope.Count);
        foreach (var center in scope)
        {
            var mine = byCenter[center.Id].OrderBy(b => b.Start).ThenBy(b => b.End).ThenBy(b => b.Number, StringComparer.Ordinal).ThenBy(b => b.Id).ToList();
            var (lanes, count) = GanttRules.Lanes(mine.Select(b => new GanttSpan(b.Id, b.Start, b.End)));
            var list = mine.Select(b =>
            {
                var standard = b.StandardHours ?? 0m;
                var running = b.Status is ProductionStepStatus.InProgress or ProductionStepStatus.Paused;
                var project = projectInfo.GetValueOrDefault(b.ProjectId);
                return new GanttCenterBar(b.Id, b.Name, b.Code, b.OrderId, b.Number, b.ProjectId, project?.Number, project?.Name,
                    running ? GanttStepState.InProgress : GanttStepState.Planned, standard, b.Hours, running ? GanttRules.Progress(standard, b.Hours) : 0m, b.Start, b.End,
                    DateOf(b.ActualStart), lanes[b.Id]);
            }).ToList();

            var available = (await capacity.GetAvailableHoursAsync(center.Id, first, last, cancellationToken))?.Days.ToDictionary(d => d.Date, d => d.Hours) ?? [];
            var allocated = daily[center.Id].ToDictionary(d => d.Date, d => d.Hours);
            var marks = calendars.TryGetValue(center.Id, out var calendarId) ? exceptions[calendarId].ToDictionary(e => e.Date) : [];
            var days = new List<GanttCenterDay>();
            for (var day = first; day <= last; day = day.AddDays(1))
            {
                var hours = allocated.GetValueOrDefault(day);
                var cap = available.GetValueOrDefault(day);
                var mark = marks.GetValueOrDefault(day);
                if (hours > 0m || cap <= 0m || mark is not null)
                {
                    days.Add(new GanttCenterDay(day, hours, cap, FactoryLoadCalculator.Percent(hours, cap), FactoryLoadCalculator.Band(hours, cap), mark?.Type, mark?.Description));
                }
            }

            rows.Add(new GanttCenterRow(center.Id, center.Name, center.Line, Math.Max(count, 1), list, days));
        }

        return new GanttCentersView(today, period.From, period.To, computedAt, rows, Lines(active), active.Count);
    }

    /// <summary>
    /// Monta os projetos pedidos, na ordem dada, numa leitura de cada: as OS não canceladas (fabricação e instalação), as etapas de fabricação
    /// com a previsão do plano (junção à esquerda), a previsão do projeto, o prometido e o cliente pelos contratos (sem N+1).
    /// </summary>
    private async Task<List<GanttProject>> BuildAsync(IReadOnlyList<Guid> projectIds, DateOnly today, CancellationToken ct)
    {
        if (projectIds.Count == 0)
        {
            return [];
        }

        var ids = projectIds.ToList();
        var orders = await db.ProductionOrders.AsNoTracking()
            .Where(o => ids.Contains(o.ProjectId) && o.Status != ProductionOrderStatus.Cancelled)
            .Select(o => new { o.Id, o.Number, o.Type, o.Status, o.ProjectId, o.PlannedStart, o.PlannedEnd })
            .ToListAsync(ct);
        var fabrication = orders.Where(o => o.Type == ProductionOrderType.Fabrication).Select(o => o.Id).ToList();
        var steps = (await (
                from s in db.ProductionOrderSteps.AsNoTracking()
                where fabrication.Contains(s.OrderId)
                join f in db.ScheduleStepForecastsView on s.Id equals f.StepId into forecasts
                from f in forecasts.DefaultIfEmpty()
                select new StepRow(
                    s.Id, s.OrderId, s.Sequence, s.Name, s.Code, s.WorkCenterId, s.StandardHours, s.Status, s.ActualStart, s.ActualEnd, s.CompletedAt,
                    f == null ? null : (decimal?)f.Hours, f == null ? null : f.PlannedStart, f == null ? null : f.PlannedEnd, f == null ? null : f.NoForecastReason, f != null))
            .ToListAsync(ct)).ToLookup(s => s.OrderId);
        var planned = await db.ScheduleProjectForecastsView.Where(f => ids.Contains(f.ProjectId))
            .Select(f => new { f.ProjectId, f.ForecastDate, f.Reason }).ToDictionaryAsync(f => f.ProjectId, ct);
        var summaries = (await projects.FindManyAsync(ids, ct)).ToDictionary(p => p.Id);
        var partyNames = (await parties.FindManyAsync([.. summaries.Values.Select(p => p.PartyId).Distinct()], ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var centerIds = steps.SelectMany(g => g).Where(s => s.WorkCenterId is not null).Select(s => s.WorkCenterId!.Value).Distinct().ToList();
        var centerNames = centerIds.Count == 0 ? [] : (await centers.FindAsync(centerIds, ct)).ToDictionary(c => c.Id, c => c.Name);
        var byProject = orders.ToLookup(o => o.ProjectId);

        return [.. ids.Select((projectId, index) =>
        {
            var summary = summaries.GetValueOrDefault(projectId);
            var forecast = planned.GetValueOrDefault(projectId);
            var promised = summary?.PromisedDate;
            var date = forecast?.ForecastDate;
            var risk = DeliveryRiskRules.Evaluate(promised, date, today, false);
            var mine = byProject[projectId]
                .OrderBy(o => o.Type == ProductionOrderType.Installation ? 1 : 0).ThenBy(o => o.Number, StringComparer.Ordinal).ToList();
            var driver = risk.Risk == DeliveryRisk.None ? null : GanttRules.DrivingStep(mine.SelectMany((o, i) => steps[o.Id]
                .Where(s => s.Status != ProductionStepStatus.Done).Select(s => new GanttDriverCandidate(s.Id, i, s.Sequence, s.PlannedEnd))));

            var list = mine.Select(o =>
            {
                if (o.Type == ProductionOrderType.Installation)
                {
                    return new GanttOrder(o.Id, o.Number, GanttOrderKind.Installation, o.Status.ToString(), o.PlannedStart, o.PlannedEnd, []);
                }

                var items = steps[o.Id].OrderBy(s => s.Sequence).Select(s => Step(s, s.WorkCenterId is { } c ? centerNames.GetValueOrDefault(c) : null, s.Id == driver)).ToList();
                return new GanttOrder(o.Id, o.Number, GanttOrderKind.Fabrication, o.Status.ToString(),
                    Min(items.Select(s => s.ActualStart ?? s.PlannedStart)), Max(items.Select(s => s.ActualEnd ?? s.PlannedEnd)), items);
            }).ToList();

            var daysLate = promised is { } p && date is { } d && d > p ? d.DayNumber - p.DayNumber : risk.DelayDays;
            return new GanttProject(index + 1, projectId, summary?.Number ?? "Projeto removido", summary?.Name ?? string.Empty, summary?.PartyId,
                summary is null ? null : partyNames.GetValueOrDefault(summary.PartyId), Min(list.Select(o => o.Start)), date, promised,
                DeliveryRiskRules.SlackDays(promised, date), risk.Risk, daysLate, date is null ? forecast?.Reason : null, list);
        })];
    }

    private sealed record StepRow(
        Guid Id, Guid OrderId, int Sequence, string Name, string? Code, Guid? WorkCenterId, decimal? StandardHours, ProductionStepStatus? Status, DateTimeOffset? ActualStart,
        DateTimeOffset? ActualEnd, DateTimeOffset? CompletedAt, decimal? Hours, DateOnly? PlannedStart, DateOnly? PlannedEnd, string? NoForecastReason, bool HasForecast);

    /// <summary>
    /// A barra da etapa (CA 2): concluída com o início e o fim reais; em andamento com a parte feita (padrão − restantes); prevista com as
    /// datas do plano; sem linha no plano ou com motivo, "sem previsão" com o motivo.
    /// </summary>
    private static GanttStep Step(StepRow s, string? centerName, bool drives)
    {
        var standard = s.StandardHours ?? 0m;
        var actualStart = DateOf(s.ActualStart);
        if (s.Status == ProductionStepStatus.Done)
        {
            var end = DateOf(s.ActualEnd ?? s.CompletedAt) ?? actualStart;
            return new GanttStep(s.Id, s.Sequence, s.Name, s.Code, s.WorkCenterId, centerName, GanttStepState.Done, standard, 0m, 1m, null, null, actualStart ?? end, end, null, false);
        }

        if (!s.HasForecast || s.NoForecastReason is not null || s.PlannedStart is null || s.PlannedEnd is null)
        {
            return new GanttStep(s.Id, s.Sequence, s.Name, s.Code, s.WorkCenterId, centerName, GanttStepState.NoForecast, standard, s.Hours ?? standard, 0m, null, null,
                actualStart, null, s.NoForecastReason ?? AwaitingPlanReason, false);
        }

        var running = s.Status is ProductionStepStatus.InProgress or ProductionStepStatus.Paused;
        var remaining = s.Hours ?? standard;
        return new GanttStep(s.Id, s.Sequence, s.Name, s.Code, s.WorkCenterId, centerName, running ? GanttStepState.InProgress : GanttStepState.Planned, standard, remaining,
            running ? GanttRules.Progress(standard, remaining) : 0m, s.PlannedStart, s.PlannedEnd, running ? actualStart : null, null, null, drives);
    }

    /// <summary>O projeto aparece se a barra (do início ao maior entre o fim e a previsão) cruza o período, ou se ainda não tem datas (o motivo aparece).</summary>
    private static bool Visible(GanttProject project, GanttPeriod period)
    {
        if (project.Start is not { } start)
        {
            return true;
        }

        var end = Max([project.ForecastDate, .. project.Orders.Select(o => o.End)]) ?? start;
        return GanttRules.Intersects(start, end < start ? start : end, period);
    }

    /// <summary>Os centros do filtro de linha ou de centro (null = sem filtro).</summary>
    private static HashSet<Guid>? CenterScope(IReadOnlyList<WorkCenterOption> active, string? line, Guid? workCenterId)
    {
        if (workCenterId is { } id)
        {
            return [id];
        }

        return string.IsNullOrWhiteSpace(line) ? null : [.. active.Where(c => string.Equals(c.Line, line.Trim(), StringComparison.Ordinal)).Select(c => c.Id)];
    }

    private static List<string> Lines(IReadOnlyList<WorkCenterOption> active) =>
        [.. active.Where(c => !string.IsNullOrWhiteSpace(c.Line)).Select(c => c.Line!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private async Task<DateTimeOffset?> ComputedAtAsync(CancellationToken ct) =>
        await db.ScheduleRunsView.Where(r => r.Status == ScheduleRunStatus.Succeeded && r.FinishedAt != null)
            .OrderByDescending(r => r.FinishedAt).ThenByDescending(r => r.Id).Select(r => r.FinishedAt).FirstOrDefaultAsync(ct);

    private static DateOnly? DateOf(DateTimeOffset? at) => at is { } value ? DateOnly.FromDateTime(BusinessTime.ToBusiness(value).DateTime) : null;

    private static DateOnly? Min(IEnumerable<DateOnly?> dates) => dates.Where(d => d is not null).DefaultIfEmpty().Min();

    private static DateOnly? Max(IEnumerable<DateOnly?> dates) => dates.Where(d => d is not null).DefaultIfEmpty().Max();
}
