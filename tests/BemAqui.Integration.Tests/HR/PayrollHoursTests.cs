using System.Globalization;
using System.Text.Json;
using BemAqui.HR.Application;
using BemAqui.HR.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// FLH-02b: horas extras, adicional noturno, DSR, perda do DSR e banco de horas no motor 1.1.0, sem banco. <b>Dataset sintético</b> (ADR
/// 0039 §4): funcionários fictícios com o resultado calculado à mão (salário 2.200,00 ÷ 220 h = hora de 10,00, salvo o caso da
/// periculosidade), as tabelas do pacote de 2026 e os parâmetros pré-configurados (50% no dia útil, 100% em domingo, folga e feriado,
/// noturno 20% desligado, DSR = variáveis ÷ dias úteis × (domingos + feriados), perda do DSR ligada). Março/2026: 31 dias, 5 domingos e
/// nenhum feriado no calendário (26 úteis, 5 de descanso). Abril/2026: 30 dias, 4 domingos e os feriados de 03/04 e 21/04 (24 úteis, 6 de
/// descanso). Nenhum holerite real.
/// </summary>
public class PayrollHoursTests
{
    private static readonly DateOnly March = new(2026, 3, 1);
    private static readonly DateOnly April = new(2026, 4, 1);
    private static readonly PayrollCalcMonthCalendar MarchCalendar = new([], 22);
    private static readonly PayrollCalcMonthCalendar AprilCalendar = new([new DateOnly(2026, 4, 3), new DateOnly(2026, 4, 21)], 20);

    private static readonly IReadOnlyList<PayrollCalcItem> Items =
        PayrollItemService.Standards.Select((s, i) => new PayrollCalcItem(new Guid(i + 1, 0, 0, new byte[8]), s.Code, s.Name, s.Kind, s.Calc, s.Inss, s.Fgts, s.Irrf, s.DeductsIrrf)).ToList();

    private static IReadOnlyList<PayrollCalcTable> Tables2026 { get; } = PayrollTaxPackages.All.Single(p => p.Package == 2026).Tables
        .Select(t => new PayrollCalcTable(Guid.NewGuid(), t.Kind, t.ValidFrom!.Value, PayrollTaxTableStatus.Unconfirmed, t.Rounding ?? PayrollRounding.AwayFromZero, t.Content!)).ToList();

    private static PayrollCalcParameters Params(Func<PayrollHourParameters, PayrollHourParameters>? change = null) =>
        PayrollCalcParameters.Default with { Hours = change is null ? PayrollHourParameters.Default : change(PayrollHourParameters.Default) };

    private static PayrollCalcSnapshot Employee(IReadOnlyList<PayrollCalcHourDay> days, PayrollCalcMonthCalendar calendar, decimal salary = 2200m, bool hazard = false, int absence = 0,
        PayrollCalcHourBank? bank = null, PayrollCalcParameters? parameters = null) =>
        new(salary, new DateOnly(2020, 1, 2), null, true, 220m, null, hazard, false, [], [], true, absence, 0, [], parameters ?? Params(), days, calendar, bank);

    private static PayrollCalcResult Calc(PayrollCalcSnapshot snapshot, DateOnly period, IReadOnlyList<PayrollCalcItem>? items = null) =>
        PayrollCalculator.Calculate(new PayrollCalcInput(period, snapshot, items ?? Items, Tables2026));

    private static PayrollCalcHourDay Extra(string day, PayrollDayKind kind, int minutes) => new(D(day), kind, minutes, 0, false);

    private static PayrollCalcHourDay Absent(string day) => new(D(day), PayrollDayKind.Workday, 0, 0, true);

    private static DateOnly D(string day) => DateOnly.Parse(day, CultureInfo.InvariantCulture);

    private static decimal Line(PayrollCalcResult r, string code) => r.Lines.Where(l => l.Code == code).Sum(l => l.Amount);

    public sealed record Case(string Name, PayrollCalcSnapshot Input, DateOnly Period, IReadOnlyDictionary<string, decimal> Lines, decimal Net);

    private static Dictionary<string, decimal> L(params (string Code, decimal Amount)[] lines) => lines.ToDictionary(l => l.Code, l => l.Amount);

    public static TheoryData<string> Names => new(Dataset.Select(c => c.Name));

    /// <summary>O dataset das horas (CA 6), cada valor à mão; INSS e FGTS pelas tabelas de 2026 (soma das faixas arredondada no total).</summary>
    private static readonly IReadOnlyList<Case> Dataset =
    [
        // Abril: 3 h em dia útil × 10 × 1,5 = 45; 4 h no domingo 12/04 + 3 h no feriado 21/04 = 7 h × 10 × 2 = 140 (o feriado usa a HE100);
        // DSR (45 + 140) ÷ 24 × 6 = 46,25. Base 2.431,25: INSS 121,575 + 810,25 × 9% = 194,4975 → 194,50; IRRF zero; FGTS 194,50.
        new("15 extras em dia útil, domingo e feriado com DSR", Employee([Extra("2026-04-07", PayrollDayKind.Workday, 120), Extra("2026-04-08", PayrollDayKind.Workday, 60),
                Extra("2026-04-12", PayrollDayKind.RestDay, 240), Extra("2026-04-21", PayrollDayKind.Holiday, 180)], AprilCalendar), April,
            L(("SAL", 2200m), ("HE50", 45m), ("HE100", 140m), ("DSR", 46.25m), ("INSS", 194.50m), ("FGTS", 194.50m)), 2236.75m),
        // Faltas em 04 e 05/03 (semana de 02/03) e em 17/03 (semana de 16/03): 3 × 8:48 h = 264; perda do DSR 2 × 2.200 ÷ 30 = 2 × 73,33.
        // Base 1.789,34: INSS 121,575 + 168,34 × 9% = 136,7256 → 136,73; FGTS 143,15.
        new("16 falta injustificada perde o DSR da semana", Employee([Absent("2026-03-04"), Absent("2026-03-05"), Absent("2026-03-17")], MarchCalendar, absence: 3 * 528), March,
            L(("SAL", 2200m), ("FALTAS", 264m), ("DSRFALTA", 146.66m), ("INSS", 136.73m), ("FGTS", 143.15m)), 1652.61m),
        // Banco ligado (as extras foram para o saldo de +50 h): o RH paga 10 h como extra de dia útil = 150; DSR 150 ÷ 26 × 5 = 28,85.
        // Base 2.378,85: INSS 121,575 + 757,85 × 9% = 189,7815 → 189,78; FGTS 190,31.
        new("17 banco ligado, pagar 10 h do banco", Employee([], MarchCalendar, bank: new(3000, 600, "Saldo acima de 40 h")), March,
            L(("SAL", 2200m), ("BHPAGO", 150m), ("DSR", 28.85m), ("INSS", 189.78m), ("FGTS", 190.31m)), 2189.07m),
        // Saldo de −10 h: o RH desconta 5 h pelo valor da hora = 50; base 2.150: INSS 121,575 + 529 × 9% = 169,185 → 169,19.
        new("18 banco negativo, descontar 5 h", Employee([], MarchCalendar, bank: new(-600, -300, "Acerto do saldo negativo")), March,
            L(("SAL", 2200m), ("BHDESC", 50m), ("INSS", 169.19m), ("FGTS", 172m)), 1980.81m),
        // Noturno ligado: 210 min das 22 h às 5 h × 60 ÷ 52,5 = 4 h × 10 × 20% = 8; DSR 8 ÷ 26 × 5 = 1,54. Base 2.209,54: INSS 174,54.
        new("19 adicional noturno ligado com a hora reduzida", Employee([new(D("2026-03-10"), PayrollDayKind.Workday, 0, 105, false), new(D("2026-03-11"), PayrollDayKind.Workday, 0, 105, false)],
                MarchCalendar, parameters: Params(h => h with { NightEnabled = true })), March,
            L(("SAL", 2200m), ("ADNOT", 8m), ("DSR", 1.54m), ("INSS", 174.54m), ("FGTS", 176.76m)), 2035m),
        // Periculosidade no valor da hora: (2.200 + 660) ÷ 220 = 13; 2 h × 13 × 1,5 = 39; DSR 39 ÷ 26 × 5 = 7,50. Base 2.906,50:
        // INSS 121,575 + 1.281,84 × 9% + 3,66 × 12% = 237,3798 → 237,38; IRRF zero pelo simplificado.
        new("20 periculosidade entra no valor da hora extra", Employee([Extra("2026-03-03", PayrollDayKind.Workday, 120)], MarchCalendar, hazard: true), March,
            L(("SAL", 2200m), ("PERIC", 660m), ("HE50", 39m), ("DSR", 7.50m), ("INSS", 237.38m), ("FGTS", 232.52m)), 2669.12m),
        // Banco desligado e sem horas: nenhuma linha nova (os 14 casos da FLH-02a seguem iguais).
        new("21 sem horas, nenhuma linha nova", Employee([], MarchCalendar), March, L(("SAL", 2200m), ("INSS", 173.69m), ("FGTS", 176m)), 2026.31m),
    ];

    /// <summary>CA 6: linhas e líquido do dataset das horas batem centavo a centavo; toda linha calculada tem a memória.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void Dataset_das_horas_bate_centavo_a_centavo(string name)
    {
        var c = Dataset.Single(x => x.Name == name);
        var r = Calc(c.Input, c.Period);
        r.Issues.Should().BeEmpty(name);
        r.Lines.GroupBy(l => l.Code).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount)).Should().Equal(c.Lines, name);
        r.Totals.Net.Should().Be(c.Net, name);
        r.Lines.Where(l => l.Calculated).Should().OnlyContain(l => l.Memory.Count > 0, "toda linha calculada tem a memória (CA 7)");
    }

    /// <summary>CA 1: cada percentual numa linha "Horas extras {percentual}%", com as horas na referência e os dias na memória.</summary>
    [Fact]
    public void Extras_por_percentual_com_os_dias_na_memoria()
    {
        var r = Calc(Dataset[0].Input, April);
        var he50 = r.Lines.Single(l => l.Code == "HE50");
        he50.Name.Should().Be("Horas extras 50%");
        he50.Reference.Should().Be("3:00 h");
        he50.Memory.Should().Contain(m => m.Contains("07/04 (2:00 h) dia útil", StringComparison.Ordinal) && m.Contains("08/04 (1:00 h)", StringComparison.Ordinal));
        he50.Memory.Should().Contain(m => m.Contains("hr.overtime_rates.weekday", StringComparison.Ordinal));
        var he100 = r.Lines.Single(l => l.Code == "HE100");
        he100.Name.Should().Be("Horas extras 100%");
        he100.Reference.Should().Be("7:00 h");
        he100.Memory.Should().Contain(m => m.Contains("domingo ou folga e feriado", StringComparison.Ordinal));
        var dsr = r.Lines.Single(l => l.Code == "DSR");
        dsr.Name.Should().Be("DSR sobre variáveis");
        dsr.Memory.Should().Contain("Feriados e paradas do calendário padrão no mês: 03/04/2026, 21/04/2026.");
    }

    /// <summary>CA 1: o percentual do parâmetro (convenção) e a rubrica própria do feriado, quando existe, numa linha separada.</summary>
    [Fact]
    public void Percentual_da_convencao_e_rubrica_do_feriado()
    {
        var days = new[] { Extra("2026-04-12", PayrollDayKind.RestDay, 60), Extra("2026-04-21", PayrollDayKind.Holiday, 60) };
        var custom = Calc(Employee(days, AprilCalendar, parameters: Params(h => h with { OvertimeHolidayPct = 150m })), April);
        custom.Lines.Where(l => l.Code == "HE100").Select(l => (l.Name, l.Amount)).Should().Equal(("Horas extras 100%", 20m), ("Horas extras 150%", 25m));
        var holidayItem = new PayrollCalcItem(Guid.NewGuid(), "HEFER", "Horas extras em feriado", PayrollItemKind.Earning, PayrollCalculation.OvertimeHoliday, true, true, true, false);
        var own = Calc(Employee(days, AprilCalendar), April, [.. Items, holidayItem]);
        Line(own, "HEFER").Should().Be(20m, "1 h no feriado × 10 × 2");
        Line(own, "HE100").Should().Be(20m);
        Line(Calc(Employee([Extra("2026-04-07", PayrollDayKind.Workday, 60)], AprilCalendar, parameters: Params(h => h with { OvertimeWorkdayPct = 70m })), April), "HE50").Should().Be(17m);
    }

    /// <summary>CA 3: noturno desligado (pré-configurado) não gera linha; ligado sem a hora reduzida conta os minutos do relógio.</summary>
    [Fact]
    public void Adicional_noturno_desligado_e_sem_hora_reduzida()
    {
        var days = new[] { new PayrollCalcHourDay(D("2026-03-10"), PayrollDayKind.Workday, 0, 210, false) };
        Calc(Employee(days, MarchCalendar), March).Lines.Should().NotContain(l => l.Code == "ADNOT" || l.Code == "DSR", "desligado, nenhuma linha");
        Line(Calc(Employee(days, MarchCalendar, parameters: Params(h => h with { NightEnabled = true, NightReducedHour = false })), March), "ADNOT").Should().Be(7m, "3:30 h × 10 × 20%");
        Line(Calc(Employee(days, MarchCalendar, parameters: Params(h => h with { NightEnabled = true, NightPct = 25m })), March), "ADNOT").Should().Be(10m, "4 h × 10 × 25%");
    }

    /// <summary>CA 4: a fórmula pelo calendário da empresa (20 úteis em abril, 10 de descanso) e os adicionais fora do valor da hora.</summary>
    [Fact]
    public void Dsr_pelo_calendario_da_empresa_e_hora_sem_adicionais()
    {
        var days = new[] { Extra("2026-04-07", PayrollDayKind.Workday, 180) };
        Line(Calc(Employee(days, AprilCalendar, parameters: Params(h => h with { DsrFormula = DsrFormula.CompanyCalendar })), April), "DSR").Should().Be(22.50m, "45 ÷ 20 × 10");
        Line(Calc(Employee(days, MarchCalendar), March), "DSR").Should().Be(8.65m, "45 ÷ 26 × 5 = 8,6538");
        var noAllowance = Calc(Employee([Extra("2026-03-03", PayrollDayKind.Workday, 120)], MarchCalendar, hazard: true, parameters: Params(h => h with { OvertimeIncludesAllowances = false })), March);
        Line(noAllowance, "HE50").Should().Be(30m, "2 h × 10 × 1,5 sem a periculosidade");
    }

    /// <summary>CA 5: perda do DSR desligada não desconta; a semana de segunda a domingo dá a referência "DSR da semana de {data}".</summary>
    [Fact]
    public void Perda_do_dsr_pelo_parametro_e_pela_semana()
    {
        var days = new[] { Absent("2026-03-04"), Absent("2026-03-17") };
        var on = Calc(Employee(days, MarchCalendar, absence: 2 * 528), March);
        on.Lines.Where(l => l.Code == "DSRFALTA").Select(l => l.Name).Should().Equal("DSR da semana de 02/03/2026", "DSR da semana de 16/03/2026");
        on.Lines.First(l => l.Code == "DSRFALTA").Memory.Should().Contain(m => m.Contains("Lei 605/1949", StringComparison.Ordinal));
        Calc(Employee(days, MarchCalendar, absence: 2 * 528, parameters: Params(h => h with { DsrLossOnAbsence = false })), March).Lines.Should().NotContain(l => l.Code == "DSRFALTA");
        PayrollHours.WeekStart(D("2026-03-08")).Should().Be(D("2026-03-02"), "o domingo fecha a semana de segunda");
    }

    /// <summary>CA 1: a classe do dia (feriado vence; domingo ou dia sem jornada é folga).</summary>
    [Theory]
    [InlineData("2026-04-21", true, 528, PayrollDayKind.Holiday)]
    [InlineData("2026-04-12", false, 0, PayrollDayKind.RestDay)]
    [InlineData("2026-04-12", false, 528, PayrollDayKind.RestDay)]
    [InlineData("2026-04-11", false, 0, PayrollDayKind.RestDay)]
    [InlineData("2026-04-07", false, 528, PayrollDayKind.Workday)]
    [InlineData("2026-04-05", true, 0, PayrollDayKind.Holiday)]
    public void Classificacao_do_dia(string day, bool holiday, int expected, PayrollDayKind kind) => PayrollHours.Classify(D(day), holiday, expected).Should().Be(kind);

    /// <summary>CA 3: os minutos no horário noturno pelos pares de marcação (a janela de 22 h às 5 h vale nas duas pontas do dia).</summary>
    [Theory]
    [InlineData("In 21:00|Out 23:30", 22, 5, 90)]
    [InlineData("In 04:00|Out 08:00", 22, 5, 60)]
    [InlineData("In 08:00|BreakStart 12:00|BreakEnd 13:00|Out 17:48", 22, 5, 0)]
    [InlineData("In 03:30|BreakStart 04:30|BreakEnd 05:30|Out 23:00", 22, 5, 120)]
    [InlineData("In 18:00|Out 23:00", 20, 23, 180)]
    [InlineData("In 21:00|Out 23:30", 22, 22, 0)]
    public void Minutos_no_horario_noturno(string marks, int start, int end, int expected)
    {
        var entries = marks.Split('|').Select(m => m.Split(' ')).Select(p => new TimesheetEntryInput(Guid.NewGuid(), April, TimeOnly.Parse(p[1], CultureInfo.InvariantCulture), Enum.Parse<TimeEntryType>(p[0]), TimeEntrySource.App)).ToList();
        PayrollHours.NightMinutes(entries, start, end).Should().Be(expected);
    }

    /// <summary>CA 4: dias úteis e de descanso do mês pela fórmula (feriado no domingo conta uma vez).</summary>
    [Theory]
    [InlineData("2026-04-01", "2026-04-03,2026-04-21", DsrFormula.SundaysAndHolidays, 24, 6)]
    [InlineData("2026-03-01", "", DsrFormula.SundaysAndHolidays, 26, 5)]
    [InlineData("2026-03-01", "2026-03-08", DsrFormula.SundaysAndHolidays, 26, 5)]
    [InlineData("2026-04-01", "2026-04-03,2026-04-21", DsrFormula.CompanyCalendar, 20, 10)]
    public void Dias_uteis_e_de_descanso(string month, string holidays, DsrFormula formula, int useful, int rest)
    {
        var list = holidays.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(D).ToList();
        PayrollHours.DsrDays(D(month), new PayrollCalcMonthCalendar(list, 20), formula).Should().Be((useful, rest));
    }

    /// <summary>
    /// CA 1, 3 e 5: os dias do espelho fechado que entram no retrato (extra, noturno ou falta injustificada), com a classe; o dia normal, o
    /// abonado e o do banco ficam fora.
    /// </summary>
    [Fact]
    public void Dias_do_espelho_fechado_no_retrato()
    {
        TimesheetDayResult Day(string day, TimesheetDayStatus status, int expected, int worked, int overtime, int absence, string? holiday = null, params (string Type, string Time)[] marks) =>
            new(D(day), status, expected, worked, overtime, 0, absence, 0, 0, null, [], false,
                marks.Select(m => new TimesheetEntryInput(Guid.NewGuid(), D(day), TimeOnly.Parse(m.Time, CultureInfo.InvariantCulture), Enum.Parse<TimeEntryType>(m.Type), TimeEntrySource.App)).ToList(), [], holiday, null);
        var days = new[]
        {
            Day("2026-04-06", TimesheetDayStatus.Normal, 528, 528, 0, 0, null, ("In", "08:00"), ("Out", "17:48")),
            Day("2026-04-07", TimesheetDayStatus.Overtime, 528, 648, 120, 0, null, ("In", "08:00"), ("Out", "19:48")),
            Day("2026-04-08", TimesheetDayStatus.Absent, 528, 0, 0, 528),
            Day("2026-04-09", TimesheetDayStatus.Justified, 528, 0, 0, 0),
            Day("2026-04-12", TimesheetDayStatus.Overtime, 0, 240, 240, 0, null, ("In", "20:00"), ("Out", "23:59")),
            Day("2026-04-21", TimesheetDayStatus.Overtime, 0, 180, 180, 0, "Tiradentes", ("In", "08:00"), ("Out", "11:00")),
        };
        PayrollHours.FromTimesheet(days, PayrollHourParameters.Default).Should().Equal(
            new PayrollCalcHourDay(D("2026-04-07"), PayrollDayKind.Workday, 120, 0, false),
            new PayrollCalcHourDay(D("2026-04-08"), PayrollDayKind.Workday, 0, 0, true),
            new PayrollCalcHourDay(D("2026-04-12"), PayrollDayKind.RestDay, 240, 119, false),
            new PayrollCalcHourDay(D("2026-04-21"), PayrollDayKind.Holiday, 180, 0, false));
    }

    /// <summary>CA 7 e ADR 0039 §2: o retrato com as horas, o calendário e o banco volta igual do JSON e dá o mesmo resultado.</summary>
    [Fact]
    public void Retrato_das_horas_e_deterministico()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var c in Dataset)
        {
            var stored = PayrollCalculator.ReadSnapshot(PayrollCalculator.WriteSnapshot(c.Input))!;
            PayrollCalculator.WriteSnapshot(stored).Should().Be(PayrollCalculator.WriteSnapshot(c.Input), c.Name);
            JsonSerializer.Serialize(Calc(stored, c.Period), json).Should().Be(JsonSerializer.Serialize(Calc(c.Input, c.Period), json), c.Name);
        }

        // O retrato da 1.0.0 (sem as horas nem as regras das horas) ainda é lido e vale o pré-configurado.
        var old = PayrollCalculator.ReadSnapshot(PayrollCalculator.WriteSnapshot(Employee([], MarchCalendar) with { HourDays = null, Calendar = null, Parameters = PayrollCalcParameters.Default with { Hours = null } }))!;
        old.Parameters.HourRules.Should().Be(PayrollHourParameters.Default);
        Calc(old, March).Lines.Should().HaveCount(3);
    }
}
