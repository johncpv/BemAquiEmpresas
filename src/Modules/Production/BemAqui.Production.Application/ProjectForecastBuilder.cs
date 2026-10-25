using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// Monta e calcula a data prevista de cada projeto a partir do resultado do motor (PLN-01b CA 5 e 6), só lendo: os projetos <c>Planned</c> e
/// <c>InProgress</c> (pelo <c>IProjects</c>; a partir de <c>Fabricated</c> a previsão fica como estava), as OS de fabricação não canceladas
/// (concluída conta pela data da conclusão; aberta sem etapas é "sem roteiro"; aberta com todas as etapas concluídas termina hoje), o fim
/// digitado das OS de instalação não canceladas, o custo de instalação do orçamento (de uma vez), o prazo <c>planning.installation_days</c>
/// e os dias úteis do calendário padrão (dias da semana e exceções de 0 h).
/// </summary>
public sealed class ProjectForecastBuilder(IProductionDb db, IProjects projects, ProductionParametersService parameters, TimeProvider clock)
{
    /// <summary>Horizonte dos feriados lidos para o prazo de instalação.</summary>
    private const int HolidayHorizonDays = 3 * 366;

    public async Task<IReadOnlyList<ProjectForecastResult>> BuildAsync(ScheduleResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        var candidates = await projects.PlanningCandidatesAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return [];
        }

        var ids = candidates.Select(p => p.Id).Distinct().ToList();
        var today = BusinessTime.Today(clock);
        var orders = await db.ProductionOrders.AsNoTracking().Where(o => ids.Contains(o.ProjectId) && o.Status != ProductionOrderStatus.Cancelled)
            .Select(o => new { o.Id, o.Number, o.ProjectId, o.Type, o.Status, o.ActualEnd, o.PlannedEnd }).ToListAsync(cancellationToken);
        var fabricationIds = orders.Where(o => o.Type == ProductionOrderType.Fabrication).Select(o => o.Id).ToList();
        var withSteps = (await db.ProductionOrderSteps.AsNoTracking().Where(s => fabricationIds.Contains(s.OrderId)).Select(s => s.OrderId).Distinct().ToListAsync(cancellationToken))
            .ToHashSet();
        var planned = result.Orders.ToDictionary(o => o.OrderId);
        var budgets = await projects.BudgetedManyAsync(ids, CostType.Installation, cancellationToken);
        var days = await parameters.InstallationDaysAsync(cancellationToken);
        var isWorkingDay = await WorkingDaysAsync(today, cancellationToken);

        var byProject = orders.ToLookup(o => o.ProjectId);
        return ids.Select(id =>
        {
            var mine = byProject[id].ToList();
            var fabrication = mine.Where(o => o.Type == ProductionOrderType.Fabrication).Select(o =>
            {
                if (o.Status == ProductionOrderStatus.Completed)
                {
                    return new ProjectForecastOrder(o.Id, o.Number, true, o.ActualEnd is { } end ? DateOnly.FromDateTime(BusinessTime.ToBusiness(end).DateTime) : today, null);
                }

                if (!withSteps.Contains(o.Id))
                {
                    return new ProjectForecastOrder(o.Id, o.Number, false, null, ProjectForecastReasons.NoRouting);
                }

                return planned.TryGetValue(o.Id, out var plan)
                    ? new ProjectForecastOrder(o.Id, o.Number, false, plan.End, plan.End is null ? ProjectForecastCalculator.ReasonCode(plan.NoForecastReason) : null)
                    : new ProjectForecastOrder(o.Id, o.Number, false, today, null);
            }).ToList();
            var installations = mine.Where(o => o.Type == ProductionOrderType.Installation && o.PlannedEnd is not null).Select(o => o.PlannedEnd!.Value).ToList();
            var input = new ProjectForecastInput(id, fabrication, installations, budgets.GetValueOrDefault(id) > 0m);
            return ProjectForecastCalculator.Compute(input, days, isWorkingDay);
        }).ToList();
    }

    /// <summary>Dia útil do calendário padrão: dia da semana útil e sem exceção de 0 h (feriado ou parada do dia todo).</summary>
    private async Task<Func<DateOnly, bool>> WorkingDaysAsync(DateOnly today, CancellationToken ct)
    {
        var calendar = await db.Calendars.AsNoTracking().Where(c => c.IsDefault).Select(c => new { c.Id, c.WorkingDays }).FirstOrDefaultAsync(ct);
        var weekdays = calendar is null ? CalendarService.DefaultWorkingDays.ToHashSet() : calendar.WorkingDays.ToHashSet();
        var until = today.AddDays(HolidayHorizonDays);
        HashSet<DateOnly> closed = calendar is null ? [] : (await db.CalendarExceptions.AsNoTracking()
            .Where(e => e.CalendarId == calendar.Id && e.Date >= today && e.Date <= until && e.HoursAvailable <= 0m).Select(e => e.Date).ToListAsync(ct)).ToHashSet();
        return date => weekdays.Contains((Weekday)date.DayOfWeek) && !closed.Contains(date);
    }
}
