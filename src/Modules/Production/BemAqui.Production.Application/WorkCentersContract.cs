using BemAqui.BuildingBlocks.Time;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// Implementação de <see cref="IWorkCenters"/> e <see cref="IWorkCenterCapacity"/> (CA 15 a 18). PAT-03b (ADR 0038 §4): a capacidade já vem
/// descontada das paradas do centro (<c>production.downtimes</c>, horas paradas por dia em cada dia da parada); o motor do plano só lê.
/// </summary>
public sealed class WorkCentersContract(IProductionDb db, TimeProvider clock) : IWorkCenters, IWorkCenterCapacity
{
    public async Task<IReadOnlyList<WorkCenterOption>> ActiveAsync(CancellationToken cancellationToken = default) =>
        await db.WorkCenters.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.Name)
            .Select(w => new WorkCenterOption(w.Id, w.Name, w.Line, w.HoursPerDay, w.IsActive)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WorkCenterOption>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return await db.WorkCenters.AsNoTracking().Where(w => ids.Contains(w.Id))
            .Select(w => new WorkCenterOption(w.Id, w.Name, w.Line, w.HoursPerDay, w.IsActive)).ToListAsync(cancellationToken);
    }

    public async Task<WorkCenterCapacity?> GetAvailableHoursAsync(Guid workCenterId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var center = await db.WorkCenters.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workCenterId, cancellationToken);
        if (center is null)
        {
            return null;
        }

        var calendar = await db.Calendars.AsNoTracking().FirstOrDefaultAsync(c => c.Id == center.CalendarId, cancellationToken);
        var exceptions = await db.CalendarExceptions.AsNoTracking()
            .Where(e => e.CalendarId == center.CalendarId && e.Date >= from && e.Date <= to)
            .ToDictionaryAsync(e => e.Date, e => e.HoursAvailable, cancellationToken);
        var createdOn = DateOnly.FromDateTime(BusinessTime.ToBusiness(center.CreatedAt).DateTime);
        var days = CapacityCalculator.Days(center.IsActive, createdOn, center.HoursPerDay, calendar?.WorkingDays ?? [], exceptions, from, to);
        var downtimes = await DowntimeService.OverlappingQuery(db, workCenterId, from, to).AsNoTracking()
            .Select(d => new { d.StartsOn, d.PlannedEndOn, d.EndedOn, d.OpenEnded, d.HoursPerDay }).ToListAsync(cancellationToken);
        if (downtimes.Count > 0)
        {
            var today = BusinessTime.Today(clock);
            days = days.Select(d => new DailyCapacity(d.Date, DowntimeRules.Discount(d.Hours,
                downtimes.Where(x => DowntimeRules.Covers(x.StartsOn, x.PlannedEndOn, x.EndedOn, x.OpenEnded, d.Date, today)).Sum(x => x.HoursPerDay)))).ToList();
        }

        return new WorkCenterCapacity(center.Id, days, days.Sum(d => d.Hours));
    }
}
