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

    /// <summary>Quantos dias para trás o "dia não útil antecipa" procura.</summary>
    public const int LookBackDays = 31;

    public async Task<int> WorkingDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
        (await WorkingDatesAsync(from, to, cancellationToken)).Count;

    public async Task<IReadOnlyList<DateOnly>> WorkingDatesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            return [];
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(to.DayNumber - from.DayNumber, MaxDays, nameof(to));
        var calendar = await DefaultAsync(cancellationToken);
        var exceptions = calendar is null
            ? new Dictionary<DateOnly, decimal>()
            : await db.CalendarExceptions.AsNoTracking().Where(e => e.CalendarId == calendar.Value.Id && e.Date >= from && e.Date <= to)
                .ToDictionaryAsync(e => e.Date, e => e.HoursAvailable, cancellationToken);
        return BusinessDays.Dates(calendar?.WorkingDays ?? [.. CalendarService.DefaultWorkingDays], exceptions, from, to);
    }

    public async Task<bool> IsBusinessDayAsync(DateOnly date, CancellationToken cancellationToken = default) =>
        (await WorkingDatesAsync(date, date, cancellationToken)).Count == 1;

    public async Task<DateOnly?> NthBusinessDayOfMonthAsync(int year, int month, int n, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(n, 1);
        var first = new DateOnly(year, month, 1);
        var dates = await WorkingDatesAsync(first, first.AddMonths(1).AddDays(-1), cancellationToken);
        return dates.Count >= n ? dates[n - 1] : null;
    }

    public async Task<DateOnly> PreviousBusinessDayAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var dates = await WorkingDatesAsync(date.AddDays(-LookBackDays), date, cancellationToken);
        return dates.Count > 0 ? dates[^1] : date;
    }

    public async Task<IReadOnlyList<BusinessCalendarExceptionDay>> ExceptionsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            return [];
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(to.DayNumber - from.DayNumber, MaxDays, nameof(to));
        var calendar = await DefaultAsync(cancellationToken);
        if (calendar is null)
        {
            return [];
        }

        var rows = await db.CalendarExceptions.AsNoTracking().Where(e => e.CalendarId == calendar.Value.Id && e.Date >= from && e.Date <= to)
            .OrderBy(e => e.Date).Select(e => new { e.Date, e.HoursAvailable, e.Description }).ToListAsync(cancellationToken);
        return rows.Select(e => new BusinessCalendarExceptionDay(e.Date, e.HoursAvailable, e.Description)).ToList();
    }

    private async Task<(Guid Id, List<Weekday> WorkingDays)?> DefaultAsync(CancellationToken ct)
    {
        var calendar = await db.Calendars.AsNoTracking().Where(c => c.IsDefault).Select(c => new { c.Id, c.WorkingDays }).FirstOrDefaultAsync(ct);
        return calendar is null ? null : (calendar.Id, [.. calendar.WorkingDays]);
    }
}
