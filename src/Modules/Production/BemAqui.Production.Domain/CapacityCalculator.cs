using BemAqui.Production.Contracts;

namespace BemAqui.Production.Domain;

/// <summary>
/// Capacidade por dia (CAD-03 CA 15 e 16): 0 se o centro está inativo, ainda não existia ou o dia não é útil; senão o menor
/// entre as horas da exceção e as horas por dia, se houver exceção; senão as horas por dia.
/// </summary>
public static class CapacityCalculator
{
    public const int MaxDays = 731;

    public static IReadOnlyList<DailyCapacity> Days(
        bool active,
        DateOnly createdOn,
        decimal hoursPerDay,
        IReadOnlyCollection<Weekday> workingDays,
        IReadOnlyDictionary<DateOnly, decimal> exceptions,
        DateOnly from,
        DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(workingDays);
        ArgumentNullException.ThrowIfNull(exceptions);
        if (to < from)
        {
            return [];
        }

        var count = to.DayNumber - from.DayNumber + 1;
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, MaxDays);
        var days = new List<DailyCapacity>(count);
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var hours = !active || date < createdOn || !workingDays.Contains((Weekday)date.DayOfWeek) ? 0m
                : exceptions.TryGetValue(date, out var available) ? Math.Min(available, hoursPerDay)
                : hoursPerDay;
            days.Add(new DailyCapacity(date, hours));
        }

        return days;
    }
}
