using System.Globalization;
using BemAqui.HR.Contracts;

namespace BemAqui.HR.Domain;

/// <summary>
/// Custo-hora do cargo (04-fluxos §3.2, CAD-04a CA 3): <c>salário × (1 + %encargos) ÷ horas mensais</c>, em decimal em todas as
/// etapas, arredondado só no fim, com 6 casas. Sem salário ou sem horas, o custo é 0.
/// </summary>
public static class HourlyCostCalculator
{
    public static decimal Compute(decimal? salary, decimal chargesPct, decimal monthlyHours) =>
        salary is not { } s || s <= 0 || monthlyHours <= 0 ? 0m : Math.Round(s * (1m + (chargesPct / 100m)) / monthlyHours, 6, MidpointRounding.AwayFromZero);

    /// <summary>Média da faixa, sugerida como salário de referência (CA 2).</summary>
    public static decimal? Average(decimal? min, decimal? max) =>
        min is { } a && max is { } b ? Math.Round((a + b) / 2m, 2, MidpointRounding.AwayFromZero) : min ?? max;
}

/// <summary>Horas da jornada (CA 15): soma de (saída − entrada − intervalo) dos dias, com 2 casas; horas mensais = semanais × 5.</summary>
public static class ScheduleMath
{
    public static string? Validate(IReadOnlyList<ScheduleDay>? days)
    {
        if (days is null || days.Count == 0)
        {
            return "Informe ao menos um dia de trabalho.";
        }

        if (days.Select(d => d.Weekday).Distinct().Count() != days.Count || days.Any(d => d.Weekday is < 1 or > 7))
        {
            return "Cada dia da semana aparece uma vez só.";
        }

        foreach (var day in days)
        {
            if (!TryParse(day.Start, out var start) || !TryParse(day.End, out var end))
            {
                return "Informe entrada e saída no formato 00:00.";
            }

            if (end <= start)
            {
                return "A saída precisa ser depois da entrada.";
            }

            if (day.BreakMinutes < 0 || day.BreakMinutes >= (end - start).TotalMinutes)
            {
                return "O intervalo precisa ser menor que o tempo entre a entrada e a saída.";
            }
        }

        return null;
    }

    public static decimal WeeklyHours(IReadOnlyList<ScheduleDay> days)
    {
        ArgumentNullException.ThrowIfNull(days);
        var minutes = days.Sum(d => TryParse(d.Start, out var a) && TryParse(d.End, out var b) ? (decimal)(b - a).TotalMinutes - d.BreakMinutes : 0m);
        return Math.Round(minutes / 60m, 2, MidpointRounding.AwayFromZero);
    }

    public static decimal MonthlyHours(decimal weeklyHours) => weeklyHours * 5m;

    private static bool TryParse(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value?.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
