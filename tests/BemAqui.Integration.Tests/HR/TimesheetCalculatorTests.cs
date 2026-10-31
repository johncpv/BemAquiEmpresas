using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// RH-01c: o cálculo do espelho sem banco (tabela de casos, CA 4 a 7, 9 e 10). Dataset fixo: jornada 44 h (segunda a sexta 08:00–17:48, 1 h
/// de intervalo), feriado em 12/10/2026, um dia com 2 h extras, um com 30 min de atraso, um sem marcação, um com marcação ímpar, um abono de
/// 4 h e um dia dentro da tolerância. A planilha de referência está no comentário de cada total, minuto a minuto.
/// </summary>
public class TimesheetCalculatorTests
{
    private static readonly IReadOnlyList<ScheduleDay> Schedule = Enumerable.Range(1, 5).Select(d => new ScheduleDay(d, "08:00", "17:48", 60)).ToList();

    private static readonly Dictionary<DateOnly, string> Holidays = new() { [new DateOnly(2026, 10, 12)] = "Nossa Senhora Aparecida" };

    private static DateOnly D(int month, int day) => new(2026, month, day);

    private static List<TimesheetEntryInput> Day(DateOnly day, params string[] times)
    {
        TimeEntryType[] cycle = [TimeEntryType.In, TimeEntryType.BreakStart, TimeEntryType.BreakEnd, TimeEntryType.Out];
        return times.Select((t, i) => new TimesheetEntryInput(Guid.NewGuid(), day, TimeOnly.Parse(t, System.Globalization.CultureInfo.InvariantCulture), cycle[i % 4], TimeEntrySource.App)).ToList();
    }

    /// <summary>De 21/09 a 31/10/2026: dia útil normal 08:00 12:00 13:00 17:48, menos os dias especiais.</summary>
    private static List<TimesheetEntryInput> Dataset()
    {
        var special = new Dictionary<DateOnly, string[]>
        {
            [D(9, 23)] = ["08:00", "12:00", "13:00", "19:48"], // 2 h extras (só no corte 21 a 20)
            [D(10, 5)] = ["08:00", "12:00", "13:00", "19:48"], // 2 h extras
            [D(10, 6)] = ["08:30", "12:00", "13:00", "17:48"], // 30 min de atraso
            [D(10, 7)] = [], // sem marcação: falta
            [D(10, 8)] = ["08:00", "12:00", "13:00"], // ímpar: falta a saída
            [D(10, 9)] = ["08:00", "12:00", "13:00", "13:48"], // 4 h 48 min + abono de 4 h
            [D(10, 12)] = [], // feriado
            [D(10, 13)] = ["08:04", "12:00", "13:00", "17:50"], // −2 min, dentro da tolerância
            [D(10, 27)] = ["08:15", "12:00", "13:00", "17:48"], // 15 min de atraso (só no mês civil)
        };
        var entries = new List<TimesheetEntryInput>();
        for (var day = D(9, 21); day <= D(10, 31); day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            entries.AddRange(Day(day, special.TryGetValue(day, out var times) ? times : ["08:00", "12:00", "13:00", "17:48"]));
        }

        return entries;
    }

    private static TimesheetInput Input(DateOnly from, DateOnly to, TimesheetSettings? settings = null, IReadOnlyList<TimesheetAbsenceInput>? absences = null,
        IReadOnlyList<TimesheetLeaveInput>? leaves = null, DateOnly? termination = null, IReadOnlyList<TimesheetEntryInput>? entries = null, int previous = 0) =>
        new(from, to, D(11, 30), D(1, 1), termination, Schedule, Holidays, entries ?? Dataset(),
            absences ?? [new(D(10, 9), 240, AbsenceReason.AttendanceNote)], leaves ?? [], new HashSet<DateOnly>(), settings ?? new TimesheetSettings(5, 10, false, null), previous);

    [Fact]
    public void Dataset_no_mes_civil_bate_minuto_a_minuto()
    {
        var result = TimesheetCalculator.Calculate(Input(D(10, 1), D(10, 31)));
        var t = result.Totals;
        t.ExpectedMinutes.Should().Be(11088, "21 dias úteis (22 menos o feriado) × 528");
        t.WorkedMinutes.Should().Be(10105, "14 normais × 528 + 648 + 498 + 0 + 240 + 288 + 526 + 513");
        t.OvertimeMinutes.Should().Be(120);
        t.LateMinutes.Should().Be(333, "30 do atraso + 288 do dia ímpar (só os pares fechados) + 15");
        t.AbsenceMinutes.Should().Be(528);
        t.JustifiedMinutes.Should().Be(240);
        t.WorkedDays.Should().Be(20);
        t.AbsenceDays.Should().Be(1);
        t.InconsistentDays.Should().Be(1);
        t.JustifiedByReason.Should().Equal(new Dictionary<AbsenceReason, int> { [AbsenceReason.AttendanceNote] = 240 });
        t.BankApplies.Should().BeFalse();
        t.BankMonthMinutes.Should().BeNull();

        var days = result.Days.ToDictionary(d => d.Day);
        days[D(10, 12)].Status.Should().Be(TimesheetDayStatus.Holiday);
        days[D(10, 12)].ExpectedMinutes.Should().Be(0);
        days[D(10, 12)].Holiday.Should().Be("Nossa Senhora Aparecida");
        days[D(10, 3)].Status.Should().Be(TimesheetDayStatus.DayOff);
        days[D(10, 5)].Status.Should().Be(TimesheetDayStatus.Overtime);
        days[D(10, 6)].Status.Should().Be(TimesheetDayStatus.Late);
        days[D(10, 7)].Status.Should().Be(TimesheetDayStatus.Absent);
        days[D(10, 8)].Status.Should().Be(TimesheetDayStatus.Inconsistent);
        days[D(10, 8)].Issues.Should().Equal("Falta a saída");
        days[D(10, 9)].Status.Should().Be(TimesheetDayStatus.Normal);
        days[D(10, 13)].Status.Should().Be(TimesheetDayStatus.Normal);
    }

    [Fact]
    public void Dataset_no_corte_de_21_a_20_bate_minuto_a_minuto()
    {
        TimesheetRules.Range(D(10, 1), 21).Should().Be((D(9, 21), D(10, 20)));
        TimesheetRules.Range(D(10, 1), 1).Should().Be((D(10, 1), D(10, 31)));
        TimesheetRules.PeriodOf(D(10, 21), 21).Should().Be(D(11, 1));
        TimesheetRules.PeriodOf(D(10, 20), 21).Should().Be(D(10, 1));
        TimesheetRules.PeriodOf(D(10, 21), 1).Should().Be(D(10, 1));

        var t = TimesheetCalculator.Calculate(Input(D(9, 21), D(10, 20))).Totals;
        t.ExpectedMinutes.Should().Be(11088, "8 dias de setembro + 14 de outubro − o feriado = 21 × 528");
        t.WorkedMinutes.Should().Be(10240, "14 normais × 528 + 648 + 648 + 498 + 0 + 240 + 288 + 526");
        t.OvertimeMinutes.Should().Be(240);
        t.LateMinutes.Should().Be(318);
        t.AbsenceMinutes.Should().Be(528);
        t.JustifiedMinutes.Should().Be(240);
        t.WorkedDays.Should().Be(20);
    }

    [Theory]
    [InlineData("08:04", "17:50", 0, 0, TimesheetDayStatus.Normal)] // −2 min: dentro das duas tolerâncias
    [InlineData("07:56", "17:52", 0, 0, TimesheetDayStatus.Normal)] // +8 min: dentro de 10 no dia e 4 por marcação
    [InlineData("08:00", "17:59", 11, 0, TimesheetDayStatus.Overtime)] // +11: acima da tolerância do dia, conta inteira
    [InlineData("07:54", "17:48", 6, 0, TimesheetDayStatus.Overtime)] // +6 no dia, mas 6 na entrada (acima de 5): conta inteira
    [InlineData("08:07", "17:48", 0, 7, TimesheetDayStatus.Late)] // −7: entrada 7 min depois, conta inteira como atraso
    public void Tolerancia_por_marcacao_e_por_dia(string start, string end, int overtime, int late, TimesheetDayStatus status)
    {
        var day = D(10, 14);
        var result = TimesheetCalculator.Calculate(Input(day, day, entries: Day(day, start, "12:00", "13:00", end), absences: []));
        var d = result.Days.Single();
        d.OvertimeMinutes.Should().Be(overtime);
        d.LateMinutes.Should().Be(late);
        d.Status.Should().Be(status);
    }

    [Theory]
    [InlineData(new[] { "08:00", "12:00", "12:40", "17:48" }, "Intervalo de 40 min, a jornada prevê 60", 548)]
    [InlineData(new[] { "08:00", "12:00", "13:00" }, "Falta a saída", 240)]
    [InlineData(new[] { "12:00", "13:00", "17:48" }, "Marcação fora de sequência: saída para o intervalo às 12:00", 288)]
    public void Dia_inconsistente_com_o_motivo_e_so_os_pares_fechados(string[] times, string issue, int worked)
    {
        var day = D(10, 14);
        var types = times.Length == 3 && times[0] == "12:00"
            ? new[] { TimeEntryType.BreakStart, TimeEntryType.BreakEnd, TimeEntryType.Out }.Select((t, i) => new TimesheetEntryInput(Guid.NewGuid(), day, TimeOnly.Parse(times[i], System.Globalization.CultureInfo.InvariantCulture), t, TimeEntrySource.App)).ToList()
            : Day(day, times);
        var d = TimesheetCalculator.Calculate(Input(day, day, entries: types, absences: [])).Days.Single();
        d.Status.Should().Be(TimesheetDayStatus.Inconsistent);
        d.Issues.Should().Contain(issue);
        d.WorkedMinutes.Should().Be(worked);
    }

    [Fact]
    public void Afastamento_e_desligamento_nao_viram_falta()
    {
        var result = TimesheetCalculator.Calculate(Input(D(10, 1), D(10, 31), leaves: [new(D(10, 19), D(10, 23), AbsenceReason.MedicalCertificate)], termination: D(10, 28)));
        var days = result.Days.ToDictionary(d => d.Day);
        days[D(10, 21)].Status.Should().Be(TimesheetDayStatus.OnLeave);
        days[D(10, 21)].ExpectedMinutes.Should().Be(0);
        days[D(10, 21)].Leave!.Reason.Should().Be(AbsenceReason.MedicalCertificate);
        days[D(10, 29)].Status.Should().Be(TimesheetDayStatus.NotEmployed);
        days[D(10, 29)].ExpectedMinutes.Should().Be(0);
        days.Values.Where(d => d.Day >= D(10, 19) && d.Day <= D(10, 23)).Should().OnlyContain(d => d.AbsenceMinutes == 0 && d.WorkedMinutes == 0);
        // Esperado: 21 dias − 5 afastados − 2 depois do desligamento (29 e 30) = 14 × 528.
        result.Totals.ExpectedMinutes.Should().Be(14 * 528);
        result.Totals.AbsenceDays.Should().Be(1);
    }

    [Fact]
    public void Banco_com_acordo_leva_a_diferenca_do_dia_e_nao_vence()
    {
        var settings = new TimesheetSettings(5, 10, true, D(10, 1));
        var t = TimesheetCalculator.Calculate(Input(D(10, 1), D(10, 31), settings, previous: 200)).Totals;
        // +120 (dia 5) −30 (dia 6) −528 (dia 7) −288 (dia 8, ímpar) +0 (abono do dia 9) +0 (tolerância do dia 13) −15 (dia 27).
        t.BankApplies.Should().BeTrue();
        t.BankMonthMinutes.Should().Be(-741);
        t.BankBalanceMinutes.Should().Be(-541, "o acumulado é o do fechamento anterior (+200) + o do período");
        t.OvertimeMinutes.Should().Be(0);
        t.LateMinutes.Should().Be(0);
        t.AbsenceMinutes.Should().Be(0);
        t.JustifiedMinutes.Should().Be(240);
        TimesheetRules.BankText(200, 310).Should().Be("Banco de horas: +3 h 20 min no período, +5 h 10 min acumulado");
    }

    [Fact]
    public void Banco_vale_so_a_partir_da_data_do_acordo()
    {
        var t = TimesheetCalculator.Calculate(Input(D(10, 1), D(10, 31), new TimesheetSettings(5, 10, true, D(10, 7)))).Totals;
        // Antes do acordo (dias 5 e 6): extra e atraso; do dia 7 em diante, para o banco.
        t.OvertimeMinutes.Should().Be(120);
        t.LateMinutes.Should().Be(30);
        t.BankMonthMinutes.Should().Be(-528 - 288 - 15);
    }

    [Fact]
    public void Folga_compensada_desconta_do_banco_e_nao_e_abono()
    {
        IReadOnlyList<TimesheetAbsenceInput> absences = [new(D(10, 7), null, AbsenceReason.TimeBankOff)];
        var withBank = TimesheetCalculator.Calculate(Input(D(10, 7), D(10, 7), new TimesheetSettings(5, 10, true, D(1, 1)), absences, previous: 600));
        var day = withBank.Days.Single();
        day.Status.Should().Be(TimesheetDayStatus.Compensated);
        TimesheetRules.StatusLabel(day.Status).Should().Be("Compensado do banco");
        day.JustifiedMinutes.Should().Be(0);
        day.CompensatedMinutes.Should().Be(528);
        day.AbsenceMinutes.Should().Be(0);
        withBank.Totals.BankMonthMinutes.Should().Be(-528);
        withBank.Totals.BankBalanceMinutes.Should().Be(72);

        // Sem acordo (ou com o banco desligado): a folga é falta, sem saldo.
        foreach (var settings in new[] { new TimesheetSettings(5, 10, true, null), new TimesheetSettings(5, 10, false, D(1, 1)) })
        {
            var without = TimesheetCalculator.Calculate(Input(D(10, 7), D(10, 7), settings, absences));
            without.Days.Single().Status.Should().Be(TimesheetDayStatus.Absent);
            without.Totals.AbsenceMinutes.Should().Be(528);
            without.Totals.BankMonthMinutes.Should().BeNull();
            without.Totals.BankBalanceMinutes.Should().BeNull();
        }
    }

    [Fact]
    public void Abono_do_dia_inteiro_e_falta_nao_justificada()
    {
        IReadOnlyList<TimesheetAbsenceInput> absences = [new(D(10, 7), null, AbsenceReason.MedicalCertificate)];
        var day = TimesheetCalculator.Calculate(Input(D(10, 7), D(10, 7), absences: absences)).Days.Single();
        day.Status.Should().Be(TimesheetDayStatus.Justified);
        day.JustifiedMinutes.Should().Be(528);
        day.AbsenceMinutes.Should().Be(0);

        var registered = TimesheetCalculator.Calculate(Input(D(10, 7), D(10, 7), absences: [new(D(10, 7), null, AbsenceReason.UnjustifiedAbsence)])).Days.Single();
        registered.Status.Should().Be(TimesheetDayStatus.Absent);
        registered.AbsenceMinutes.Should().Be(528);
    }

    [Fact]
    public void Dia_de_hoje_em_diante_fica_fora_dos_totais()
    {
        var input = Input(D(10, 1), D(10, 31)) with { Today = D(10, 15) };
        var result = TimesheetCalculator.Calculate(input);
        result.Days.Where(d => d.Day >= D(10, 15)).Should().OnlyContain(d => d.Status == TimesheetDayStatus.Upcoming);
        result.Totals.ExpectedMinutes.Should().Be(9 * 528, "1, 2, 5 a 9, 13 e 14 (o feriado fica de fora)");
    }

    [Fact]
    public void Textos_e_formatos()
    {
        TimesheetRules.Duration(528).Should().Be("8 h 48 min");
        TimesheetRules.Duration(-45).Should().Be("−45 min");
        TimesheetRules.Clock(10105).Should().Be("168:25");
        TimesheetRules.Clock(-200).Should().Be("-3:20");
        TimesheetRules.FormatByReason(new Dictionary<AbsenceReason, int> { [AbsenceReason.JustifiedAbsence] = 60, [AbsenceReason.MedicalCertificate] = 528 })
            .Should().Be("MedicalCertificate=528;JustifiedAbsence=60");
        AccountantLayoutV2.ByReason(TimesheetRules.ParseByReason("MedicalCertificate=528;JustifiedAbsence=60;WorkAccident=60"))
            .Should().Be("Falta justificada 1:00 | Ausência justificada 9:48", "o motivo de saúde nunca sai pelo nome");
        TimesheetRules.FileName(D(10, 1), "007", "João Conceição").Should().Be("espelho-2026-10-007-joao-conceicao.pdf");
    }
}
