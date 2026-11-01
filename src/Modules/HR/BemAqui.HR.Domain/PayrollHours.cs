using System.Globalization;
using System.Text.Json.Serialization;

namespace BemAqui.HR.Domain;

/// <summary>Classe do dia para as horas extras (FLH-02b CA 1): dia útil, domingo ou folga, feriado (pelo calendário do espelho fechado).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PayrollDayKind>))]
public enum PayrollDayKind
{
    Workday,
    RestDay,
    Holiday,
}

/// <summary>Fórmula do DSR sobre as variáveis (<c>hr.dsr_formula</c>, FLH-02b CA 4).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DsrFormula>))]
public enum DsrFormula
{
    /// <summary>Dias úteis = dias do mês − domingos − feriados (o sábado conta como útil, o usual); descanso = domingos + feriados.</summary>
    SundaysAndHolidays = 1,

    /// <summary>Dias úteis pelo calendário padrão da empresa (<c>IBusinessCalendar</c>); descanso = os demais dias do mês.</summary>
    CompanyCalendar = 2,
}

/// <summary>
/// Um dia do espelho fechado que interessa à folha (FLH-02b, retrato): a classe do dia, os minutos extras, os minutos no horário noturno e
/// se foi falta injustificada (sem abono aprovado). Sem marcação nem motivo: só o necessário para o cálculo.
/// </summary>
public sealed record PayrollCalcHourDay(DateOnly Day, PayrollDayKind Kind, int OvertimeMinutes, int NightMinutes, bool UnjustifiedAbsence);

/// <summary>
/// Banco de horas no retrato (FLH-02b CA 2): o saldo acumulado do espelho fechado da competência e a decisão do RH na folha ("Pagar" com
/// minutos positivos, "Descontar" com negativos, zero sem decisão) e o motivo.
/// </summary>
public sealed record PayrollCalcHourBank(int BalanceMinutes, int DecisionMinutes, string? DecisionNote);

/// <summary>O calendário da competência (FLH-02b CA 4): os feriados e paradas (dia sem horas) e os dias úteis do calendário padrão.</summary>
public sealed record PayrollCalcMonthCalendar(IReadOnlyList<DateOnly> Holidays, int CompanyWorkingDays);

/// <summary>
/// Parâmetros das horas (FLH-02b, "A confirmar com a contadora"): percentuais das extras por classe do dia, se o valor da hora das extras
/// leva os adicionais (Súmulas 132 e 139 do TST), o adicional noturno (desligado), a fórmula do DSR e a perda do DSR por falta.
/// </summary>
public sealed record PayrollHourParameters(
    decimal OvertimeWorkdayPct,
    decimal OvertimeRestDayPct,
    decimal OvertimeHolidayPct,
    bool OvertimeIncludesAllowances,
    bool NightEnabled,
    decimal NightPct,
    int NightStartHour,
    int NightEndHour,
    bool NightReducedHour,
    DsrFormula DsrFormula,
    bool DsrLossOnAbsence)
{
    /// <summary>Os pré-configurados (PO 21/10/2026, tabela 3.4 pontos 13 a 15): mínimo legal da CLT, a convenção coletiva confirma.</summary>
    public static PayrollHourParameters Default { get; } = new(50m, 100m, 100m, true, false, 20m, 22, 5, true, DsrFormula.SundaysAndHolidays, true);
}

/// <summary>
/// Leitura das horas do espelho fechado para a folha (FLH-02b), sem banco: classifica o dia e soma os minutos no horário noturno pelos
/// pares de marcação congelados no fechamento (nada recalcula o espelho).
/// </summary>
public static class PayrollHours
{
    /// <summary>
    /// Os dias do espelho fechado que entram no retrato: com extra, com minuto noturno ou com falta injustificada. Feriado vem do calendário
    /// gravado no espelho; domingo ou dia sem jornada é "domingo ou folga"; o resto é dia útil.
    /// </summary>
    public static IReadOnlyList<PayrollCalcHourDay> FromTimesheet(IEnumerable<TimesheetDayResult> days, PayrollHourParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(parameters);
        var result = new List<PayrollCalcHourDay>();
        foreach (var d in days.OrderBy(d => d.Day))
        {
            var night = NightMinutes(d.Entries, parameters.NightStartHour, parameters.NightEndHour);
            var absent = d.Status == TimesheetDayStatus.Absent && d.AbsenceMinutes > 0;
            if (d.OvertimeMinutes <= 0 && night <= 0 && !absent)
            {
                continue;
            }

            result.Add(new PayrollCalcHourDay(d.Day, Classify(d.Day, d.Holiday is not null, d.ExpectedMinutes), Math.Max(d.OvertimeMinutes, 0), night, absent));
        }

        return result;
    }

    /// <summary>CA 1: feriado (pelo calendário) vence; domingo ou dia sem jornada é "domingo ou folga"; o resto é dia útil.</summary>
    public static PayrollDayKind Classify(DateOnly day, bool holiday, int expectedMinutes) =>
        holiday ? PayrollDayKind.Holiday : day.DayOfWeek == DayOfWeek.Sunday || expectedMinutes <= 0 ? PayrollDayKind.RestDay : PayrollDayKind.Workday;

    /// <summary>
    /// Minutos trabalhados dentro do horário noturno (CA 3): pares fechados (entrada ou volta do intervalo até a saída para o intervalo ou a
    /// saída), cruzados com a janela [início, fim) do dia; a janela que passa da meia-noite (22 h às 5 h) vale nas duas pontas do dia.
    /// </summary>
    public static int NightMinutes(IReadOnlyList<TimesheetEntryInput> entries, int startHour, int endHour)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (startHour == endHour)
        {
            return 0;
        }

        (int From, int To)[] windows = startHour < endHour ? [(startHour * 60, endHour * 60)] : [(0, endHour * 60), (startHour * 60, 24 * 60)];
        var total = 0;
        int? open = null;
        foreach (var e in entries.OrderBy(e => e.Time))
        {
            var minute = (e.Time.Hour * 60) + e.Time.Minute;
            if (e.Type is TimeEntryType.In or TimeEntryType.BreakEnd)
            {
                open = minute;
            }
            else if (open is { } from)
            {
                foreach (var (a, b) in windows)
                {
                    total += Math.Max(0, Math.Min(minute, b) - Math.Max(from, a));
                }

                open = null;
            }
        }

        return total;
    }

    /// <summary>Segunda-feira da semana do dia (a semana da perda do DSR vai de segunda a domingo).</summary>
    public static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>
    /// CA 4: dias úteis e de descanso (domingos + feriados) do mês pela fórmula. Sem calendário, só os domingos descansam.
    /// </summary>
    public static (int Useful, int RestDays) DsrDays(DateOnly month, PayrollCalcMonthCalendar? calendar, DsrFormula formula)
    {
        var start = new DateOnly(month.Year, month.Month, 1);
        var days = DateTime.DaysInMonth(month.Year, month.Month);
        if (formula == DsrFormula.CompanyCalendar && calendar is { CompanyWorkingDays: > 0 } c)
        {
            return (c.CompanyWorkingDays, days - c.CompanyWorkingDays);
        }

        var holidays = (calendar?.Holidays ?? []).Where(h => h.Year == start.Year && h.Month == start.Month).ToHashSet();
        var rest = Enumerable.Range(0, days).Select(i => start.AddDays(i)).Count(d => d.DayOfWeek == DayOfWeek.Sunday || holidays.Contains(d));
        return (days - rest, rest);
    }

    public static string KindLabel(PayrollDayKind kind) => kind switch
    {
        PayrollDayKind.Holiday => "feriado",
        PayrollDayKind.RestDay => "domingo ou folga",
        _ => "dia útil",
    };

    public static string FormulaLabel(DsrFormula formula) => formula == DsrFormula.CompanyCalendar
        ? "dias úteis pelo calendário padrão da empresa"
        : "dias úteis = dias do mês − domingos − feriados";

    /// <summary>"03/03 (2:00 h)", para a memória.</summary>
    public static string DayText(DateOnly day, int minutes) => $"{day.ToString("dd/MM", CultureInfo.InvariantCulture)} ({PayrollCalculator.Hours(minutes)})";
}
