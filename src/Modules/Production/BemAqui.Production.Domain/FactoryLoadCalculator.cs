using System.Globalization;
using BemAqui.Production.Contracts;

namespace BemAqui.Production.Domain;

/// <summary>
/// Ocupação da fábrica (PLN-04 CA 1, 2, 4 e 6), pura: semanas ISO a partir da atual (na semana atual só os dias a partir de hoje),
/// comprometidas = Σ horas do plano do centro nos dias da semana, capacidade = Σ horas disponíveis do centro nos mesmos dias, ocupação %
/// com 1 casa (capacidade 0 → null) e a faixa; a linha "Fábrica" = Σ comprometidas ÷ Σ capacidades.
/// </summary>
public static class FactoryLoadCalculator
{
    public const int DefaultWeeks = 8;

    public const int MaxWeeks = 26;

    /// <summary>Abaixo disto, "Folga" (ponto 41).</summary>
    public const decimal IdleBelow = 60m;

    /// <summary>Daqui para cima, "Lotado" (ponto 41).</summary>
    public const decimal FullFrom = 95m;

    public const string FactoryName = "Fábrica";

    /// <summary>Segunda-feira da semana ISO do dia.</summary>
    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    /// <summary><paramref name="count"/> semanas a partir da atual; a primeira conta a partir de hoje.</summary>
    public static IReadOnlyList<FactoryLoadWeek> Weeks(DateOnly today, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, MaxWeeks);
        var first = WeekStart(today);
        return [.. Enumerable.Range(0, count).Select(i =>
        {
            var start = first.AddDays(7 * i);
            return new FactoryLoadWeek(start, start.AddDays(6), ISOWeek.GetWeekOfYear(start.ToDateTime(TimeOnly.MinValue)), i == 0 ? today : start);
        })];
    }

    /// <summary>Ocupação com 1 casa (meio para cima); null sem capacidade.</summary>
    public static decimal? Percent(decimal committed, decimal capacity) =>
        capacity <= 0m ? null : Math.Round(committed * 100m / capacity, 1, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Faixa pela razão sem arredondar: comprometidas acima da capacidade (inclusive capacidade 0 com horas) → <c>Over</c>; senão
    /// &lt; 60% <c>Idle</c>, ≥ 95% <c>Full</c>, no meio <c>Normal</c>; sem capacidade e sem horas → <c>NoCapacity</c>.
    /// </summary>
    public static FactoryLoadBand Band(decimal committed, decimal capacity)
    {
        if (committed > capacity)
        {
            return FactoryLoadBand.Over;
        }

        if (capacity <= 0m)
        {
            return FactoryLoadBand.NoCapacity;
        }

        var ratio = committed * 100m / capacity;
        return ratio < IdleBelow ? FactoryLoadBand.Idle : ratio >= FullFrom ? FactoryLoadBand.Full : FactoryLoadBand.Normal;
    }

    public static FactoryLoadCell Cell(DateOnly weekStart, decimal committed, decimal capacity) =>
        new(weekStart, committed, capacity, Percent(committed, capacity), Band(committed, capacity));

    /// <summary>Linha de um centro: soma a capacidade e as comprometidas de cada dia contado de cada semana.</summary>
    public static FactoryLoadRow Row(
        Guid? workCenterId, string name, string? line, IReadOnlyList<FactoryLoadWeek> weeks, IReadOnlyCollection<DailyCapacity> capacity,
        IReadOnlyDictionary<DateOnly, decimal> committed, decimal hoursAfter, DateOnly? busyUntil)
    {
        ArgumentNullException.ThrowIfNull(weeks);
        ArgumentNullException.ThrowIfNull(capacity);
        ArgumentNullException.ThrowIfNull(committed);
        var cells = weeks.Select(w => Cell(w.Start,
            committed.Where(c => c.Key >= w.From && c.Key <= w.End).Sum(c => c.Value),
            capacity.Where(d => d.Date >= w.From && d.Date <= w.End).Sum(d => d.Hours))).ToList();
        return new FactoryLoadRow(workCenterId, name, line, cells, hoursAfter, busyUntil);
    }

    /// <summary>Linha "Fábrica": Σ comprometidas ÷ Σ capacidades por semana, as horas depois somadas e o último dia planejado de todos.</summary>
    public static FactoryLoadRow Factory(IReadOnlyList<FactoryLoadWeek> weeks, IReadOnlyCollection<FactoryLoadRow> centers)
    {
        ArgumentNullException.ThrowIfNull(weeks);
        ArgumentNullException.ThrowIfNull(centers);
        var cells = weeks.Select((w, i) => Cell(w.Start, centers.Sum(c => c.Cells[i].CommittedHours), centers.Sum(c => c.Cells[i].CapacityHours))).ToList();
        return new FactoryLoadRow(null, FactoryName, null, cells, centers.Sum(c => c.HoursAfter), centers.Max(c => c.BusyUntil));
    }
}
