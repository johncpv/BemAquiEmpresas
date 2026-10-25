using BemAqui.Production.Contracts;

namespace BemAqui.Production.Domain;

/// <summary>
/// Dias úteis pelo calendário (DSH-01a CA 9), com a mesma regra da capacidade (<see cref="CapacityCalculator"/>): dia de
/// trabalho do calendário, sem exceção de 0 h. Exceção com horas disponíveis conta como útil; exceção num dia sem trabalho não
/// o torna útil.
/// </summary>
public static class BusinessDays
{
    public static int Count(IReadOnlyCollection<Weekday> workingDays, IReadOnlyDictionary<DateOnly, decimal> exceptions, DateOnly from, DateOnly to)
    {
        ArgumentNullException.ThrowIfNull(workingDays);
        ArgumentNullException.ThrowIfNull(exceptions);
        var count = 0;
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (workingDays.Contains((Weekday)date.DayOfWeek) && (!exceptions.TryGetValue(date, out var hours) || hours > 0))
            {
                count++;
            }
        }

        return count;
    }
}
