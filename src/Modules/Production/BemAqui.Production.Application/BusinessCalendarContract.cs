using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// <see cref="IBusinessCalendar"/> pelo calendário padrão (CAD-03). Só lê: sem calendário padrão gravado, vale a semana de
/// segunda a sexta do padrão (o <see cref="CalendarService"/> cria o calendário na primeira abertura da tela).
/// </summary>
public sealed class BusinessCalendarContract(IProductionDb db) : IBusinessCalendar
{
    public const int MaxDays = 3 * 366;

    public async Task<int> WorkingDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        (await WorkingDatesAsync(from, to, cancellationToken)).Count;

    public async Task<IReadOnlyList<DateOnly>> WorkingDatesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            return [];
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(to.DayNumber - from.DayNumber, MaxDays, nameof(to));
        var calendar = await db.Calendars.AsNoTracking().Where(c => c.IsDefault).Select(c => new { c.Id, c.WorkingDays }).FirstOrDefaultAsync(cancellationToken);
        var exceptions = calendar is null
            ? new Dictionary<DateOnly, decimal>()
            : await db.CalendarExceptions.AsNoTracking().Where(e => e.CalendarId == calendar.Id && e.Date >= from && e.Date <= to)
                .ToDictionaryAsync(e => e.Date, e => e.HoursAvailable, cancellationToken);
        return BusinessDays.Dates(calendar?.WorkingDays ?? [.. CalendarService.DefaultWorkingDays], exceptions, from, to);
    }
}
