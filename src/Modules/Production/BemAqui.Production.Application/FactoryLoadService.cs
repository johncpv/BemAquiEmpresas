using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>Filtros da aba Ocupação (PLN-04 CA 9), por allowlist: de 1 a 26 semanas, até 50 centros, a linha (até 120).</summary>
public sealed record FactoryLoadQuery(int? Weeks, IReadOnlyCollection<Guid>? WorkCenterIds, string? Line);

/// <summary>
/// Tela Planejamento → Ocupação (PLN-04 CA 1 a 9): a ocupação (a mesma do <see cref="IFactoryLoad"/>), as linhas dos centros para o filtro e
/// quantos centros ativos existem (0 → "Cadastre os centros de trabalho para ver a ocupação.").
/// </summary>
public sealed record FactoryLoadView(FactoryLoadWeekly Load, IReadOnlyList<string> Lines, int ActiveCenters);

/// <summary>Etapa alocada num centro e numa semana (CA 5): OS, projeto, etapa, horas na semana e início e fim previstos.</summary>
public sealed record FactoryLoadCellStep(
    Guid StepId, string StepName, Guid OrderId, string OrderNumber, Guid ProjectId, string? ProjectNumber, string? ProjectName, Guid WorkCenterId, string WorkCenterName,
    decimal Hours, DateOnly? PlannedStart, DateOnly? PlannedEnd);

/// <summary>Drill-down da célula (CA 5): o centro (null = Fábrica), a semana, os dias contados, o total (igual ao da célula) e as etapas.</summary>
public sealed record FactoryLoadCellDetail(Guid? WorkCenterId, string Name, DateOnly WeekStart, DateOnly From, DateOnly To, decimal TotalHours, IReadOnlyList<FactoryLoadCellStep> Steps);

/// <summary>
/// Ocupação da fábrica (PLN-04): lê o plano gravado (<c>schedule_allocations</c>, agregado no banco por centro e dia) e a capacidade de cada
/// centro ativo agora (<see cref="IWorkCenterCapacity"/>, uma leitura por centro), sem recalcular nada; implementa o <see cref="IFactoryLoad"/>
/// para os painéis (CA 10) com a mesma conta da tela. Sem custo.
/// </summary>
public sealed class FactoryLoadService(IProductionDb db, IWorkCenters centers, IWorkCenterCapacity capacity, IProjects projects, TimeProvider clock) : IFactoryLoad
{
    public const int MaxCenters = 50;

    public const int MaxLineLength = 120;

    public const int MaxCellSteps = 500;

    public static Error InvalidWeeks() => Error.Validation("planning_load.weeks", $"Escolha de 1 a {FactoryLoadCalculator.MaxWeeks} semanas.");

    public static Error TooManyCenters() => Error.Validation("planning_load.centers", $"Escolha até {MaxCenters} centros de trabalho.");

    public static Error InvalidLine() => Error.Validation("planning_load.line", $"A linha tem até {MaxLineLength} caracteres.");

    public static Error InvalidWeek() =>
        Error.Validation("planning_load.week", $"A semana começa numa segunda-feira, da atual até {FactoryLoadCalculator.MaxWeeks - 1} semanas à frente.");

    public static Error CenterNotFound() => Error.NotFound("work_center_not_found", "Centro de trabalho não encontrado ou inativo.");

    public async Task<Result<FactoryLoadView>> ViewAsync(FactoryLoadQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (Validate(query) is { } error)
        {
            return error;
        }

        var active = await centers.ActiveAsync(cancellationToken);
        var lines = active.Where(c => !string.IsNullOrWhiteSpace(c.Line)).Select(c => c.Line!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var load = await BuildAsync(query.Weeks ?? FactoryLoadCalculator.DefaultWeeks, Filter(active, query.WorkCenterIds, query.Line), cancellationToken);
        return new FactoryLoadView(load, lines, active.Count);
    }

    public async Task<FactoryLoadWeekly> WeeklyAsync(int weeks = 5, IReadOnlyCollection<Guid>? workCenterIds = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(weeks, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(weeks, FactoryLoadCalculator.MaxWeeks);
        var active = await centers.ActiveAsync(cancellationToken);
        return await BuildAsync(weeks, Filter(active, workCenterIds, null), cancellationToken);
    }

    /// <summary>
    /// Drill-down (CA 5): as etapas alocadas no centro (ou em todos os centros filtrados, na linha "Fábrica") nos dias contados da semana,
    /// com as horas da semana; o total é a mesma soma da célula.
    /// </summary>
    public async Task<Result<FactoryLoadCellDetail>> CellAsync(Guid? workCenterId, DateOnly weekStart, FactoryLoadQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (Validate(query with { Weeks = null }) is { } error)
        {
            return error;
        }

        var today = BusinessTime.Today(clock);
        var first = FactoryLoadCalculator.WeekStart(today);
        if (weekStart.DayOfWeek != DayOfWeek.Monday || weekStart < first || weekStart > first.AddDays(7 * (FactoryLoadCalculator.MaxWeeks - 1)))
        {
            return InvalidWeek();
        }

        var active = await centers.ActiveAsync(cancellationToken);
        var scope = Filter(active, query.WorkCenterIds, query.Line);
        if (workCenterId is { } id)
        {
            scope = [.. active.Where(c => c.Id == id)];
            if (scope.Count == 0)
            {
                return CenterNotFound();
            }
        }

        var from = weekStart < today ? today : weekStart;
        var to = weekStart.AddDays(6);
        var ids = scope.Select(c => c.Id).ToList();
        var name = workCenterId is null ? FactoryLoadCalculator.FactoryName : scope[0].Name;
        if (ids.Count == 0)
        {
            return new FactoryLoadCellDetail(workCenterId, name, weekStart, from, to, 0m, []);
        }

        var hours = await db.ScheduleAllocationsView.Where(a => ids.Contains(a.WorkCenterId) && a.Date >= from && a.Date <= to)
            .GroupBy(a => new { a.StepId, a.ProductionOrderId, a.WorkCenterId })
            .Select(g => new { g.Key.StepId, g.Key.ProductionOrderId, g.Key.WorkCenterId, Hours = g.Sum(a => a.Hours) })
            .ToListAsync(cancellationToken);
        var total = hours.Sum(h => h.Hours);
        var stepIds = hours.Select(h => h.StepId).Distinct().ToList();
        var orderIds = hours.Select(h => h.ProductionOrderId).Distinct().ToList();
        var forecasts = await db.ScheduleStepForecastsView.Where(f => stepIds.Contains(f.StepId))
            .Select(f => new { f.StepId, f.PlannedStart, f.PlannedEnd }).ToDictionaryAsync(f => f.StepId, cancellationToken);
        var steps = await db.ProductionOrderSteps.AsNoTracking().Where(s => stepIds.Contains(s.Id)).Select(s => new { s.Id, s.Name }).ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        var orders = await db.ProductionOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).Select(o => new { o.Id, o.Number, o.ProjectId }).ToDictionaryAsync(o => o.Id, cancellationToken);
        var projectInfo = (await projects.FindManyAsync([.. orders.Values.Select(o => o.ProjectId).Distinct()], cancellationToken)).ToDictionary(p => p.Id);
        var centerNames = scope.ToDictionary(c => c.Id, c => c.Name);

        var list = hours.Select(h =>
        {
            var order = orders.GetValueOrDefault(h.ProductionOrderId);
            var project = order is null ? null : projectInfo.GetValueOrDefault(order.ProjectId);
            var forecast = forecasts.GetValueOrDefault(h.StepId);
            return new FactoryLoadCellStep(h.StepId, steps.GetValueOrDefault(h.StepId) ?? "Etapa", h.ProductionOrderId, order?.Number ?? "OS", order?.ProjectId ?? Guid.Empty,
                project?.Number, project?.Name, h.WorkCenterId, centerNames.GetValueOrDefault(h.WorkCenterId) ?? "Centro", h.Hours, forecast?.PlannedStart, forecast?.PlannedEnd);
        })
            .OrderBy(s => s.PlannedStart ?? DateOnly.MaxValue).ThenBy(s => s.OrderNumber, StringComparer.Ordinal).ThenBy(s => s.StepName, StringComparer.Ordinal).ThenBy(s => s.StepId)
            .Take(MaxCellSteps).ToList();
        return new FactoryLoadCellDetail(workCenterId, name, weekStart, from, to, total, list);
    }

    private static Error? Validate(FactoryLoadQuery query)
    {
        if (query.Weeks is { } weeks && (weeks < 1 || weeks > FactoryLoadCalculator.MaxWeeks))
        {
            return InvalidWeeks();
        }

        if (query.WorkCenterIds is { Count: > MaxCenters })
        {
            return TooManyCenters();
        }

        return query.Line is { Length: > MaxLineLength } ? InvalidLine() : null;
    }

    private static List<WorkCenterOption> Filter(IReadOnlyList<WorkCenterOption> active, IReadOnlyCollection<Guid>? ids, string? line) =>
        [.. active.Where(c => (ids is not { Count: > 0 } || ids.Contains(c.Id)) && (string.IsNullOrWhiteSpace(line) || string.Equals(c.Line, line.Trim(), StringComparison.Ordinal)))];

    /// <summary>
    /// A conta da tela e do contrato (CA 2, 3 e 10): agregação no banco por centro e dia de hoje ao fim da última semana, as horas depois e
    /// o último dia planejado por centro numa consulta, e a capacidade de cada centro lida uma vez (CA 11).
    /// </summary>
    private async Task<FactoryLoadWeekly> BuildAsync(int weekCount, List<WorkCenterOption> scope, CancellationToken ct)
    {
        var today = BusinessTime.Today(clock);
        var weeks = FactoryLoadCalculator.Weeks(today, weekCount);
        var end = weeks[^1].End;
        var computedAt = await db.ScheduleRunsView.Where(r => r.Status == ScheduleRunStatus.Succeeded && r.FinishedAt != null)
            .OrderByDescending(r => r.FinishedAt).ThenByDescending(r => r.Id).Select(r => r.FinishedAt).FirstOrDefaultAsync(ct);
        var ids = scope.Select(c => c.Id).ToList();
        if (ids.Count == 0)
        {
            return new FactoryLoadWeekly(today, computedAt, weeks, [], FactoryLoadCalculator.Factory(weeks, []));
        }

        var daily = (await db.ScheduleAllocationsView.Where(a => ids.Contains(a.WorkCenterId) && a.Date >= today && a.Date <= end)
                .GroupBy(a => new { a.WorkCenterId, a.Date })
                .Select(g => new { g.Key.WorkCenterId, g.Key.Date, Hours = g.Sum(a => a.Hours) })
                .ToListAsync(ct))
            .ToLookup(d => d.WorkCenterId);
        var beyond = await db.ScheduleAllocationsView.Where(a => ids.Contains(a.WorkCenterId) && a.Date >= today)
            .GroupBy(a => a.WorkCenterId)
            .Select(g => new { WorkCenterId = g.Key, After = g.Sum(a => a.Date > end ? a.Hours : 0m), Last = g.Max(a => a.Date) })
            .ToDictionaryAsync(g => g.WorkCenterId, ct);

        var rows = new List<FactoryLoadRow>(scope.Count);
        foreach (var center in scope)
        {
            var available = await capacity.GetAvailableHoursAsync(center.Id, today, end, ct);
            var tail = beyond.GetValueOrDefault(center.Id);
            rows.Add(FactoryLoadCalculator.Row(center.Id, center.Name, center.Line, weeks, available?.Days ?? [],
                daily[center.Id].ToDictionary(d => d.Date, d => d.Hours), tail?.After ?? 0m, tail?.Last));
        }

        return new FactoryLoadWeekly(today, computedAt, weeks, rows, FactoryLoadCalculator.Factory(weeks, rows));
    }
}
