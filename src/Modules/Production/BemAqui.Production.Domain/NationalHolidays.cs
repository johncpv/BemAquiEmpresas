namespace BemAqui.Production.Domain;

/// <summary>Feriado nacional sugerido (CA 10). <c>Optional</c> = ponto facultativo, que vem desmarcado.</summary>
public sealed record NationalHoliday(DateOnly Date, string Name, bool Optional);

/// <summary>
/// Feriados nacionais de um ano (CAD-03 CA 10), a lista num só lugar: os fixos da lei federal, a Sexta-feira Santa (móvel, a
/// partir da Páscoa) e os pontos facultativos Carnaval e Corpus Christi. Estaduais e municipais são cadastrados à mão.
/// </summary>
public static class NationalHolidays
{
    private static readonly (int Month, int Day, string Name)[] Fixed =
    [
        (1, 1, "Confraternização Universal"),
        (4, 21, "Tiradentes"),
        (5, 1, "Dia do Trabalho"),
        (9, 7, "Independência do Brasil"),
        (10, 12, "Nossa Senhora Aparecida"),
        (11, 2, "Finados"),
        (11, 15, "Proclamação da República"),
        (11, 20, "Dia Nacional de Zumbi e da Consciência Negra"),
        (12, 25, "Natal"),
    ];

    public static IReadOnlyList<NationalHoliday> For(int year)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 1900);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(year, 2200);
        var easter = Easter(year);
        var holidays = Fixed.Select(f => new NationalHoliday(new DateOnly(year, f.Month, f.Day), f.Name, false)).ToList();
        holidays.Add(new NationalHoliday(easter.AddDays(-48), "Carnaval (segunda-feira)", true));
        holidays.Add(new NationalHoliday(easter.AddDays(-47), "Carnaval (terça-feira)", true));
        holidays.Add(new NationalHoliday(easter.AddDays(-2), "Sexta-feira Santa", false));
        holidays.Add(new NationalHoliday(easter.AddDays(60), "Corpus Christi", true));
        return holidays.OrderBy(h => h.Date).ToList();
    }

    /// <summary>Domingo de Páscoa pelo algoritmo de Meeus/Jones/Butcher (calendário gregoriano).</summary>
    public static DateOnly Easter(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var month = (h + l - (7 * m) + 114) / 31;
        var day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }
}
