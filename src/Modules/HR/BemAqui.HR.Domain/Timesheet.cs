using System.Globalization;
using System.Text.Json.Serialization;
using BemAqui.HR.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.HR.Domain;

/// <summary>Situação do espelho do período (RH-01c): aberto (recalculado a cada leitura) ou fechado (totais congelados).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TimesheetStatus>))]
public enum TimesheetStatus
{
    Open,
    Closed,
}

/// <summary>Situação do dia no espelho (RH-01c CA 3, 6, 7 e 10).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TimesheetDayStatus>))]
public enum TimesheetDayStatus
{
    Normal,
    Overtime,
    Late,
    Absent,
    Justified,
    Compensated,
    OnLeave,
    Holiday,
    DayOff,
    Inconsistent,
    NotEmployed,
    Upcoming,

    /// <summary>Férias programadas (FLH-03 CA 5): sem jornada esperada, também nos dias que ainda não chegaram.</summary>
    Vacation,
}

/// <summary>Rubrica ligada a um total do espelho (RH-01c CA 16, "Origem no ponto"): extras, faltas ou atrasos.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PayrollTimesheetSource>))]
public enum PayrollTimesheetSource
{
    Overtime,
    Absence,
    Late,
}

/// <summary>
/// Espelho do período por funcionário (<c>hr.timesheets</c>, RH-01c): nasce no fechamento com os totais congelados em minutos e
/// <c>Closed</c>; a reabertura volta a <c>Open</c> e guarda quem, quando e o motivo (nunca apaga). Único por (funcionário, período), o
/// período é o mês de referência (o do fim do intervalo). "Li e confirmo" do funcionário é ciência, não assinatura legal.
/// </summary>
[DocumentName("O espelho de ponto")]
public sealed class Timesheet : Entity, IAuditable, IConcurrencyVersioned
{
    public Guid EmployeeId { get; init; }

    /// <summary>Mês de referência (primeiro dia do mês do fim do intervalo).</summary>
    public DateOnly Period { get; init; }

    /// <summary>Primeiro e último dia apurados (o corte vale como estava no fechamento).</summary>
    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public TimesheetStatus Status { get; set; } = TimesheetStatus.Open;

    public int ExpectedMinutes { get; set; }

    public int WorkedMinutes { get; set; }

    public int OvertimeMinutes { get; set; }

    public int AbsenceMinutes { get; set; }

    public int LateMinutes { get; set; }

    public int JustifiedMinutes { get; set; }

    /// <summary>Saldo do banco no período (null sem banco ou sem acordo).</summary>
    public int? BankMonthMinutes { get; set; }

    /// <summary>Saldo acumulado: o do fechamento anterior + o do período (null sem banco).</summary>
    public int? BankBalanceMinutes { get; set; }

    /// <summary>
    /// Baixa do banco pela folha fechada da competência (FLH-02b CA 2), com o sinal: positivo = horas pagas (saem do saldo positivo),
    /// negativo = horas descontadas (zeram o saldo negativo). O saldo depois da folha é <see cref="BankBalanceMinutes"/> − este valor.
    /// </summary>
    public int BankPayoutMinutes { get; set; }

    public int WorkedDays { get; set; }

    public int AbsenceDays { get; set; }

    /// <summary>Horas abonadas por motivo, congeladas ("JustifiedAbsence=240;AttendanceNote=60"), para o <c>ponto.csv</c>.</summary>
    [PersonalData]
    public string? JustifiedByReason { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public Guid? ClosedBy { get; set; }

    public DateTimeOffset? ReopenedAt { get; set; }

    public Guid? ReopenedBy { get; set; }

    public string? ReopenReason { get; set; }

    /// <summary>"Li e confirmo" do funcionário (CA 15): quando, quem e o resumo dos totais que ele viu.</summary>
    public DateTimeOffset? EmployeeAckAt { get; set; }

    public Guid? EmployeeAckBy { get; set; }

    public string? EmployeeAckSummary { get; set; }

    /// <summary>
    /// Os dias como estavam no fechamento (JSON), para o espelho fechado, o PDF e o drill-down baterem com os totais congelados mesmo que a
    /// jornada ou os parâmetros mudem depois. Leva o tipo do afastamento e o motivo do abono (dado de saúde): <c>[PersonalData]</c>.
    /// </summary>
    [PersonalData]
    public string? DaysSnapshot { get; set; }

    /// <summary>PDF guardado do espelho fechado (CA 18); volta a nulo na reabertura.</summary>
    public Guid? PdfAttachmentId { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public uint Version { get; set; }
}

/// <summary>Dia inconsistente marcado como revisado pelo RH (<c>hr.timesheet_day_reviews</c>, CA 11), com comentário obrigatório.</summary>
public sealed class TimesheetDayReview : Entity, IAuditable
{
    public Guid EmployeeId { get; init; }

    public DateOnly Day { get; init; }

    [PersonalData]
    public string Comment { get; init; } = string.Empty;

    public Guid? ReviewedBy { get; init; }

    public DateTimeOffset ReviewedAt { get; init; }
}

/// <summary>Marcação válida do dia, com a hora local já truncada no minuto.</summary>
public sealed record TimesheetEntryInput(Guid Id, DateOnly Day, TimeOnly Time, TimeEntryType Type, TimeEntrySource Source);

/// <summary>Abono aprovado (RH-01b): o dia, os minutos (null = o dia inteiro) e o motivo da lista única.</summary>
public sealed record TimesheetAbsenceInput(DateOnly Day, int? Minutes, AbsenceReason Reason);

/// <summary>Afastamento (CAD-04c1): início e fim, inclusive, e o tipo (só exibido com <c>hr.view_health</c>).</summary>
public sealed record TimesheetLeaveInput(DateOnly Start, DateOnly End, AbsenceReason? Reason);

/// <summary>Parâmetros do espelho (CA 1, "A confirmar com a contadora"): tolerâncias, banco ligado e o acordo do funcionário.</summary>
public sealed record TimesheetSettings(int TolerancePerEntryMinutes, int TolerancePerDayMinutes, bool BankEnabled, DateOnly? BankAgreementOn)
{
    public static TimesheetSettings Default { get; } = new(5, 10, true, null);
}

/// <summary>Tudo o que o cálculo precisa, sem banco (CA 4): o intervalo, o vínculo, a jornada, o calendário, as marcações e os abonos.</summary>
public sealed record TimesheetInput(
    DateOnly From,
    DateOnly To,
    DateOnly Today,
    DateOnly Admission,
    DateOnly? Termination,
    IReadOnlyList<ScheduleDay>? Schedule,
    IReadOnlyDictionary<DateOnly, string> Holidays,
    IReadOnlyList<TimesheetEntryInput> Entries,
    IReadOnlyList<TimesheetAbsenceInput> Absences,
    IReadOnlyList<TimesheetLeaveInput> Leaves,
    IReadOnlySet<DateOnly> Reviewed,
    TimesheetSettings Settings,
    int PreviousBankBalanceMinutes)
{
    /// <summary>Férias programadas (FLH-03 CA 5): início e fim, inclusive; os dias saem como "Férias".</summary>
    public IReadOnlyList<(DateOnly Start, DateOnly End)> Vacations { get; init; } = [];
}

/// <summary>Um dia do espelho: os minutos, a situação, os motivos da inconsistência e o que veio da jornada e do calendário.</summary>
public sealed record TimesheetDayResult(
    DateOnly Day,
    TimesheetDayStatus Status,
    int ExpectedMinutes,
    int WorkedMinutes,
    int OvertimeMinutes,
    int LateMinutes,
    int AbsenceMinutes,
    int JustifiedMinutes,
    int CompensatedMinutes,
    int? BankMinutes,
    IReadOnlyList<string> Issues,
    bool Reviewed,
    IReadOnlyList<TimesheetEntryInput> Entries,
    IReadOnlyList<TimesheetAbsenceInput> Absences,
    string? Holiday,
    TimesheetLeaveInput? Leave);

/// <summary>Totais do período (CA 4, 9 e 10).</summary>
public sealed record TimesheetTotals(
    int ExpectedMinutes,
    int WorkedMinutes,
    int OvertimeMinutes,
    int AbsenceMinutes,
    int LateMinutes,
    int JustifiedMinutes,
    int CompensatedMinutes,
    int WorkedDays,
    int AbsenceDays,
    int InconsistentDays,
    bool BankApplies,
    int? BankMonthMinutes,
    int? BankBalanceMinutes,
    IReadOnlyDictionary<AbsenceReason, int> JustifiedByReason);

public sealed record TimesheetResult(IReadOnlyList<TimesheetDayResult> Days, TimesheetTotals Totals);

/// <summary>
/// Cálculo do espelho (RH-01c, serviço puro do Domain, sem banco, para o teste de tabela). Minutos inteiros; a marcação conta no minuto
/// (os segundos são desprezados).
/// <list type="bullet">
/// <item>Esperado: entrada a saída da jornada no dia da semana menos o intervalo; zero em feriado ou parada do calendário, folga, afastamento
/// e fora do vínculo. Dia de hoje em diante: "Ainda não chegou", fora dos totais.</item>
/// <item>Trabalhado: soma dos pares fechados (entrada ou volta do intervalo seguida de saída para o intervalo ou saída). O dia com marcação
/// ímpar, fora de sequência ou intervalo menor que o da jornada é "Inconsistente" e conta só os pares fechados.</item>
/// <item>Tolerância (CA 5): a diferença do dia (trabalhado + abonado − esperado) dentro da tolerância por dia, com a entrada e a saída
/// dentro da tolerância por marcação, não conta; acima, conta a diferença inteira.</item>
/// <item>Sem banco: a sobra é extra; a falta com marcação é atraso (inclui a saída antecipada); o dia sem marcação e sem abono é falta.</item>
/// <item>Banco (CA 9 e 10, só com o banco ligado e a partir da data do acordo): a diferença do dia vai inteira para o banco (extras, faltas e
/// atrasos ficam zerados nesses dias); a "Folga compensada pelo banco de horas" não é hora abonada e é descontada do banco. Nenhum saldo vence.</item>
/// </list>
/// </summary>
public static class TimesheetCalculator
{
    public static TimesheetResult Calculate(TimesheetInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var entriesByDay = input.Entries.GroupBy(e => e.Day).ToDictionary(g => g.Key, g => g.OrderBy(e => e.Time).ToList());
        var absencesByDay = input.Absences.GroupBy(a => a.Day).ToDictionary(g => g.Key, g => g.ToList());
        var days = new List<TimesheetDayResult>();
        for (var day = input.From; day <= input.To; day = day.AddDays(1))
        {
            days.Add(Day(input, day, entriesByDay.GetValueOrDefault(day) ?? [], absencesByDay.GetValueOrDefault(day) ?? []));
        }

        var counted = days.Where(d => d.Status != TimesheetDayStatus.Upcoming).ToList();
        var bankApplies = input.Settings.BankEnabled && input.Settings.BankAgreementOn is { } agreement && agreement <= input.To;
        int? bankMonth = bankApplies ? counted.Sum(d => d.BankMinutes ?? 0) : null;
        var byReason = counted.SelectMany(d => JustifiedSplit(d)).GroupBy(x => x.Reason).ToDictionary(g => g.Key, g => g.Sum(x => x.Minutes));
        var totals = new TimesheetTotals(
            counted.Sum(d => d.ExpectedMinutes), counted.Sum(d => d.WorkedMinutes), counted.Sum(d => d.OvertimeMinutes), counted.Sum(d => d.AbsenceMinutes),
            counted.Sum(d => d.LateMinutes), counted.Sum(d => d.JustifiedMinutes), counted.Sum(d => d.CompensatedMinutes), counted.Count(d => d.WorkedMinutes > 0),
            counted.Count(d => d.Status == TimesheetDayStatus.Absent), counted.Count(d => d.Status == TimesheetDayStatus.Inconsistent && !d.Reviewed),
            bankApplies, bankMonth, bankApplies ? input.PreviousBankBalanceMinutes + bankMonth : null, byReason);
        return new TimesheetResult(days, totals);
    }

    /// <summary>Minutos esperados pela jornada no dia da semana (sem calendário).</summary>
    public static int ScheduledMinutes(IReadOnlyList<ScheduleDay>? schedule, DateOnly day) =>
        ScheduleOf(schedule, day) is { } s ? (int)(s.End - s.Start).TotalMinutes - s.Break : 0;

    private static TimesheetDayResult Day(TimesheetInput input, DateOnly day, List<TimesheetEntryInput> entries, List<TimesheetAbsenceInput> absences)
    {
        var holiday = input.Holidays.TryGetValue(day, out var description) ? description : null;
        var leave = input.Leaves.FirstOrDefault(l => l.Start <= day && day <= l.End);
        var schedule = ScheduleOf(input.Schedule, day);
        var reviewed = input.Reviewed.Contains(day);
        TimesheetDayResult Empty(TimesheetDayStatus status, int expected = 0) =>
            new(day, status, expected, 0, 0, 0, 0, 0, 0, null, [], reviewed, entries, absences, holiday, leave);

        if (day < input.Admission || (input.Termination is { } end && day > end))
        {
            return Empty(TimesheetDayStatus.NotEmployed);
        }

        if (input.Vacations.Any(v => v.Start <= day && day <= v.End))
        {
            return Empty(TimesheetDayStatus.Vacation);
        }

        var scheduled = schedule is { } sc ? (int)(sc.End - sc.Start).TotalMinutes - sc.Break : 0;
        if (day >= input.Today)
        {
            return Empty(TimesheetDayStatus.Upcoming, holiday is null && leave is null ? scheduled : 0);
        }

        if (leave is not null)
        {
            return Empty(TimesheetDayStatus.OnLeave);
        }

        var (worked, issues) = Worked(entries, schedule?.Break ?? 0);
        var expected = holiday is null ? scheduled : 0;
        var bank = input.Settings.BankEnabled && input.Settings.BankAgreementOn is { } agreement && day >= agreement;

        // Abonos (CA 9 e 10): o que justifica conta como abonado; a folga pelo banco compensa do banco (com banco) e não é abono.
        var shortfall = Math.Max(0, expected - worked);
        var justified = 0;
        var compensated = 0;
        foreach (var a in absences)
        {
            var info = AbsenceReasons.Info(a.Reason);
            var minutes = a.Minutes ?? expected;
            if (info.Justifies)
            {
                var take = Math.Min(minutes, shortfall - justified - compensated);
                justified += Math.Max(0, take);
            }
            else if (a.Reason == AbsenceReason.TimeBankOff && bank)
            {
                var take = Math.Min(minutes, shortfall - justified - compensated);
                compensated += Math.Max(0, take);
            }
        }

        var diff = worked + justified - expected;
        if (diff != 0 && WithinTolerance(input.Settings, schedule, entries, diff))
        {
            diff = 0;
        }

        int overtime = 0, late = 0, absence = 0;
        int? bankMinutes = null;
        if (bank)
        {
            bankMinutes = diff;
        }
        else if (diff > 0)
        {
            overtime = diff;
        }
        else if (diff < 0)
        {
            if (worked > 0)
            {
                late = -diff;
            }
            else
            {
                absence = -diff;
            }
        }

        var status = issues.Count > 0 ? TimesheetDayStatus.Inconsistent
            : expected == 0 && worked == 0 ? (holiday is not null ? TimesheetDayStatus.Holiday : TimesheetDayStatus.DayOff)
            : compensated > 0 && worked == 0 ? TimesheetDayStatus.Compensated
            : justified > 0 && worked == 0 ? TimesheetDayStatus.Justified
            : worked == 0 && expected > 0 ? TimesheetDayStatus.Absent
            : diff > 0 ? TimesheetDayStatus.Overtime
            : diff < 0 ? TimesheetDayStatus.Late
            : TimesheetDayStatus.Normal;
        return new TimesheetDayResult(day, status, expected, worked, overtime, late, absence, justified, compensated, bankMinutes, issues, reviewed, entries, absences, holiday, leave);
    }

    /// <summary>Pares fechados e os motivos da inconsistência (CA 6).</summary>
    private static (int Worked, List<string> Issues) Worked(List<TimesheetEntryInput> entries, int scheduledBreak)
    {
        var issues = new List<string>();
        var worked = 0;
        TimeOnly? open = null;
        TimeOnly? lastClose = null;
        var breakTotal = 0;
        var pairs = 0;
        TimeEntryType? last = null;
        foreach (var e in entries)
        {
            if (TimeClockRules.IsOutOfSequence(last, e.Type) && issues.Count == 0)
            {
                issues.Add($"Marcação fora de sequência: {Lower(TimeClockRules.Label(e.Type))} às {Time(e.Time)}");
            }

            last = e.Type;
            if (e.Type is TimeEntryType.In or TimeEntryType.BreakEnd)
            {
                if (open is not null && !issues.Contains("Falta a saída"))
                {
                    issues.Add("Falta a saída");
                }

                if (lastClose is { } closed && open is null)
                {
                    breakTotal += (int)(e.Time - closed).TotalMinutes;
                }

                open = e.Time;
            }
            else if (open is { } started)
            {
                worked += (int)(e.Time - started).TotalMinutes;
                pairs++;
                open = null;
                lastClose = e.Time;
            }
            else if (!issues.Contains("Falta a entrada"))
            {
                issues.Add("Falta a entrada");
            }
        }

        if (open is not null && !issues.Contains("Falta a saída"))
        {
            issues.Add("Falta a saída");
        }

        if (entries.Count % 2 == 1 && issues.Count == 0)
        {
            issues.Add("Número ímpar de marcações");
        }

        if (scheduledBreak > 0 && pairs >= 2 && breakTotal < scheduledBreak)
        {
            issues.Add($"Intervalo de {breakTotal} min, a jornada prevê {scheduledBreak}");
        }

        return (worked, issues);
    }

    /// <summary>
    /// CLT art. 58 §1, como parâmetro (CA 5): a diferença do dia até a tolerância por dia, com a primeira entrada e a última saída até a
    /// tolerância por marcação contra a jornada, não conta.
    /// </summary>
    private static bool WithinTolerance(TimesheetSettings settings, (TimeOnly Start, TimeOnly End, int Break)? schedule, List<TimesheetEntryInput> entries, int diff)
    {
        if (Math.Abs(diff) > settings.TolerancePerDayMinutes || schedule is not { } s)
        {
            return false;
        }

        var first = entries.FirstOrDefault(e => e.Type == TimeEntryType.In);
        var lastOut = entries.LastOrDefault(e => e.Type == TimeEntryType.Out);
        if (first is null || lastOut is null)
        {
            return false;
        }

        return Math.Abs((first.Time.ToTimeSpan() - s.Start.ToTimeSpan()).TotalMinutes) <= settings.TolerancePerEntryMinutes
            && Math.Abs((lastOut.Time.ToTimeSpan() - s.End.ToTimeSpan()).TotalMinutes) <= settings.TolerancePerEntryMinutes;
    }

    private static (TimeOnly Start, TimeOnly End, int Break)? ScheduleOf(IReadOnlyList<ScheduleDay>? schedule, DateOnly day)
    {
        var weekday = day.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)day.DayOfWeek;
        var d = schedule?.FirstOrDefault(s => s.Weekday == weekday);
        if (d is null || !TimeOnly.TryParseExact(d.Start, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || !TimeOnly.TryParseExact(d.End, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) || end <= start)
        {
            return null;
        }

        return (start, end, Math.Max(0, d.BreakMinutes));
    }

    /// <summary>O abonado do dia dividido pelos motivos que justificam, na ordem em que vieram.</summary>
    private static IEnumerable<(AbsenceReason Reason, int Minutes)> JustifiedSplit(TimesheetDayResult day)
    {
        var left = day.JustifiedMinutes;
        foreach (var a in day.Absences.Where(a => AbsenceReasons.Info(a.Reason).Justifies))
        {
            if (left <= 0)
            {
                yield break;
            }

            var take = Math.Min(left, a.Minutes ?? left);
            left -= take;
            yield return (a.Reason, take);
        }
    }

    private static string Time(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string Lower(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];
}

/// <summary>Regras do espelho sem banco (RH-01c): o intervalo do período, os textos, o formato "h min" e o resumo da ciência.</summary>
public static class TimesheetRules
{
    public const string ClosedCode = "timesheet_period_closed";
    public const string NotClosedCode = "timesheet_period_not_closed";
    public const string PreconditionsCode = "timesheet_close_preconditions";
    public const string ConcurrencyCode = "concurrency_conflict";
    public const string PdfFooter = "Controle interno de jornada; não substitui o registro legal de ponto.";
    public const int CommentMin = 5;
    public const int CommentMax = 200;
    public const int ReasonMin = 5;
    public const int ReasonMax = 200;

    /// <summary>
    /// Intervalo do período de referência <paramref name="period"/> (primeiro dia do mês) pelo dia de início (CA 1): 1 é o mês civil; 21
    /// faz de 21 do mês anterior a 20 do mês de referência. Dia fora de 1 a 28 vale 1.
    /// </summary>
    public static (DateOnly Start, DateOnly End) Range(DateOnly period, int startDay)
    {
        var month = new DateOnly(period.Year, period.Month, 1);
        if (startDay is <= 1 or > 28)
        {
            return (month, month.AddMonths(1).AddDays(-1));
        }

        var previous = month.AddMonths(-1);
        return (new DateOnly(previous.Year, previous.Month, startDay), new DateOnly(month.Year, month.Month, startDay - 1));
    }

    /// <summary>Período de referência do dia (o mês do fim do intervalo que contém o dia).</summary>
    public static DateOnly PeriodOf(DateOnly day, int startDay)
    {
        var month = new DateOnly(day.Year, day.Month, 1);
        return startDay is > 1 and <= 28 && day.Day >= startDay ? month.AddMonths(1) : month;
    }

    /// <summary>"8 h 48 min", "45 min", "0 min"; negativo com "−".</summary>
    public static string Duration(int minutes)
    {
        var sign = minutes < 0 ? "−" : string.Empty;
        var m = Math.Abs(minutes);
        var hours = m / 60;
        var rest = m % 60;
        return sign + (hours > 0 && rest == 0 ? $"{hours} h" : hours > 0 ? $"{hours} h {rest} min" : $"{rest} min");
    }

    /// <summary>"+3 h 20 min" (saldo do banco com sinal).</summary>
    public static string WithSign(int minutes) => minutes > 0 ? "+" + Duration(minutes) : Duration(minutes);

    /// <summary>Horas no arquivo e na referência da rubrica: "176:00", "-3:20".</summary>
    public static string Clock(int minutes) => (minutes < 0 ? "-" : string.Empty) + $"{Math.Abs(minutes) / 60}:{Math.Abs(minutes) % 60:00}";

    /// <summary>CA 9: "Banco de horas: +3 h 20 min no período, +5 h 10 min acumulado".</summary>
    public static string BankText(int month, int balance) => $"Banco de horas: {WithSign(month)} no período, {WithSign(balance)} acumulado";

    public static string StatusLabel(TimesheetDayStatus status) => status switch
    {
        TimesheetDayStatus.Normal => "Normal",
        TimesheetDayStatus.Overtime => "Extra",
        TimesheetDayStatus.Late => "Atraso",
        TimesheetDayStatus.Absent => "Falta",
        TimesheetDayStatus.Justified => "Abonado",
        TimesheetDayStatus.Compensated => "Compensado do banco",
        TimesheetDayStatus.OnLeave => "Afastado",
        TimesheetDayStatus.Vacation => "Férias",
        TimesheetDayStatus.Holiday => "Feriado",
        TimesheetDayStatus.DayOff => "Folga",
        TimesheetDayStatus.Inconsistent => "Inconsistente",
        TimesheetDayStatus.NotEmployed => "Fora do vínculo",
        _ => "Ainda não chegou",
    };

    /// <summary>Resumo gravado no "Li e confirmo" (CA 15).</summary>
    public static string AckSummary(TimesheetTotals t)
    {
        ArgumentNullException.ThrowIfNull(t);
        var text = $"Esperado {Duration(t.ExpectedMinutes)}; trabalhado {Duration(t.WorkedMinutes)}; extras {Duration(t.OvertimeMinutes)}; faltas {Duration(t.AbsenceMinutes)}; " +
            $"atrasos {Duration(t.LateMinutes)}; abonado {Duration(t.JustifiedMinutes)}";
        return t.BankMonthMinutes is { } month && t.BankBalanceMinutes is { } balance ? text + "; " + BankText(month, balance) : text;
    }

    /// <summary>"JustifiedAbsence=240;AttendanceNote=60", na ordem da lista única.</summary>
    public static string? FormatByReason(IReadOnlyDictionary<AbsenceReason, int> byReason)
    {
        ArgumentNullException.ThrowIfNull(byReason);
        var parts = AbsenceReasons.All.Where(r => byReason.GetValueOrDefault(r.Code) > 0).Select(r => $"{r.Code}={byReason[r.Code]}").ToList();
        return parts.Count == 0 ? null : string.Join(';', parts);
    }

    public static IReadOnlyDictionary<AbsenceReason, int> ParseByReason(string? text)
    {
        var result = new Dictionary<AbsenceReason, int>();
        foreach (var part in (text ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=');
            if (pair.Length == 2 && Enum.TryParse<AbsenceReason>(pair[0], out var reason) && int.TryParse(pair[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
            {
                result[reason] = minutes;
            }
        }

        return result;
    }

    /// <summary>"10/2026" do período de referência.</summary>
    public static string MonthYear(DateOnly period) => period.ToString("MM'/'yyyy", CultureInfo.InvariantCulture);

    public static string Date(DateOnly day) => day.ToString("dd'/'MM'/'yyyy", CultureInfo.InvariantCulture);

    public static string FileName(DateOnly period, string? registration, string name) =>
        $"espelho-{period:yyyy-MM}-{Slug(string.IsNullOrWhiteSpace(registration) ? name : registration + "-" + name)}.pdf";

    public static string BatchFileName(DateOnly period) => $"espelhos-{period:yyyy-MM}.zip";

    private static string Slug(string text)
    {
        var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length > 60 ? slug[..60] : slug;
    }
}
