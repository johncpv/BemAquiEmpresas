using System.Globalization;
using BemAqui.Production.Contracts;

namespace BemAqui.Production.Domain;

/// <summary>Regras dos turnos (CAD-03 CA 12 e 13): horas por dia derivadas dos turnos, sem sobreposição e sem virar o dia.</summary>
public static class ShiftMath
{
    public const int MaxShifts = 6;

    /// <summary>Mensagem PT-BR do primeiro problema, ou null se os turnos valem.</summary>
    public static string? Validate(IReadOnlyList<Shift>? shifts)
    {
        if (shifts is null || shifts.Count == 0)
        {
            return "Informe ao menos um turno.";
        }

        if (shifts.Count > MaxShifts)
        {
            return $"Use no máximo {MaxShifts} turnos.";
        }

        var parsed = new List<(TimeOnly Start, TimeOnly End)>();
        foreach (var shift in shifts)
        {
            if (!TryParse(shift?.Start, out var start) || !TryParse(shift?.End, out var end))
            {
                return "Informe início e fim de cada turno no formato 00:00.";
            }

            if (end <= start)
            {
                return "Turno que vira o dia não é aceito.";
            }

            parsed.Add((start, end));
        }

        var ordered = parsed.OrderBy(s => s.Start).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Start < ordered[i - 1].End)
            {
                return "Os turnos não podem se sobrepor.";
            }
        }

        return Minutes(shifts) > 24 * 60 ? "O total dos turnos passa de 24 horas." : null;
    }

    /// <summary>Horas por dia com 2 casas (ex.: 07:00–11:00 + 12:00–16:48 = 8,80). Os turnos precisam ter passado por <see cref="Validate"/>.</summary>
    public static decimal HoursPerDay(IReadOnlyList<Shift> shifts) => Math.Round(Minutes(shifts) / 60m, 2, MidpointRounding.AwayFromZero);

    /// <summary>"07:00" e "7:00" valem; grava sempre "07:00".</summary>
    public static Shift Normalize(Shift shift)
    {
        ArgumentNullException.ThrowIfNull(shift);
        return TryParse(shift.Start, out var start) && TryParse(shift.End, out var end)
            ? new Shift(start.ToString("HH:mm", CultureInfo.InvariantCulture), end.ToString("HH:mm", CultureInfo.InvariantCulture))
            : shift;
    }

    private static int Minutes(IReadOnlyList<Shift> shifts) => shifts.Sum(s => TryParse(s.Start, out var a) && TryParse(s.End, out var b) ? (int)(b - a).TotalMinutes : 0);

    private static bool TryParse(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value?.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
