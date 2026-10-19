using BemAqui.BuildingBlocks.Time;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>Implementação de <see cref="IWorkCenters"/> e <see cref="IWorkCenterCapacity"/> (CA 15 a 18).</summary>
public sealed class WorkCentersContract(IProductionDb db) : IWorkCenters, IWorkCenterCapacity
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
        return new WorkCenterCapacity(center.Id, days, days.Sum(d => d.Hours));
    }
}
