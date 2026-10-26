using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// PLN-04 sem banco: semanas ISO a partir de hoje, soma por semana, percentual com 1 casa, faixas e o "Acima da capacidade" (CA 2, 4 e 6),
/// com o cenário fixo da PLN-01a CA 7 (hoje sexta 30/10/2026, feriado 02/11, Corte 8 h/dia e Montagem 16 h/dia) rodado pelo motor.
/// </summary>
public class FactoryLoadCalculatorTests
{
    private static readonly Weekday[] MonToFri = [Weekday.Monday, Weekday.Tuesday, Weekday.Wednesday, Weekday.Thursday, Weekday.Friday];
    private static readonly DateOnly Today = new(2026, 10, 30);
    private static readonly Guid Cut = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid Assembly = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Dictionary<DateOnly, decimal> Holiday = new() { [D(11, 2)] = 0m };

    private static DateOnly D(int month, int day) => new(2026, month, day);

    private static IReadOnlyList<DailyCapacity> Days(decimal hours, IReadOnlyDictionary<DateOnly, decimal>? exceptions = null, int days = FiniteScheduler.HorizonDays) =>
        CapacityCalculator.Days(true, new DateOnly(2026, 1, 1), hours, MonToFri, exceptions ?? Holiday, Today, Today.AddDays(days - 1));

    private static ScheduleStepInput Step(string name, Guid center, decimal standard, ProductionStepStatus status = ProductionStepStatus.Waiting, decimal logged = 0m) =>
        new(Guid.NewGuid(), 0, name, center, standard, logged, status);

    private static ScheduleOrderInput Order(string number, Guid projectId, params ScheduleStepInput[] steps) =>
        new(Guid.NewGuid(), number, projectId, 1, [.. steps.Select((s, i) => s with { Sequence = i + 1 })]);

    /// <summary>O cenário da PLN-01a CA 7 rodado pelo motor: as alocações por centro e dia.</summary>
    public static ScheduleResult FixedPlan()
    {
        var p1 = new ScheduleProjectInput(Guid.NewGuid(), "P1", D(12, 1), D(1, 10), null);
        var p2 = new ScheduleProjectInput(Guid.NewGuid(), "P2", D(12, 10), D(1, 10), null);
        var p3 = new ScheduleProjectInput(Guid.NewGuid(), "P3", D(12, 20), D(1, 10), null);
        var orders = new[]
        {
            Order("OS-1", p1.ProjectId, Step("Corte", Cut, 12m, ProductionStepStatus.Ready), Step("Montagem", Assembly, 20m)),
            Order("OS-2", p2.ProjectId, Step("Corte", Cut, 6m, ProductionStepStatus.InProgress, 4m), Step("Montagem", Assembly, 10m)),
            Order("OS-3", p3.ProjectId, Step("Corte", Cut, 4m, ProductionStepStatus.Ready), Step("Montagem", Assembly, 4m)),
        };
        return FiniteScheduler.Run(new ScheduleInput(Today, [p1, p2, p3], orders, [new ScheduleCenterInput(Cut, "Corte", Days(8m)), new ScheduleCenterInput(Assembly, "Montagem", Days(16m))]));
    }

    private static Dictionary<DateOnly, decimal> Committed(ScheduleResult plan, Guid center) =>
        plan.Allocations.Where(a => a.WorkCenterId == center).GroupBy(a => a.Date).ToDictionary(g => g.Key, g => g.Sum(a => a.Hours));

    [Fact]
    public void Semanas_ISO_a_partir_da_atual_contando_so_de_hoje()
    {
        var weeks = FactoryLoadCalculator.Weeks(Today, 3);
        weeks.Select(w => (w.Start, w.End, w.IsoWeek, w.From)).Should().Equal(
            (D(10, 26), D(11, 1), 44, Today), (D(11, 2), D(11, 8), 45, D(11, 2)), (D(11, 9), D(11, 15), 46, D(11, 9)));
        FactoryLoadCalculator.WeekStart(D(11, 1)).Should().Be(D(10, 26), "domingo fecha a semana");
        FactoryLoadCalculator.WeekStart(D(11, 2)).Should().Be(D(11, 2));
        FluentActions.Invoking(() => FactoryLoadCalculator.Weeks(Today, 27)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => FactoryLoadCalculator.Weeks(Today, 0)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Cenario_fixo_da_PLN_01a_da_os_numeros_do_CA_2()
    {
        var plan = FixedPlan();
        var weeks = FactoryLoadCalculator.Weeks(Today, 8);
        var cut = FactoryLoadCalculator.Row(Cut, "Corte", "Cabines", weeks, Days(8m, days: 60), Committed(plan, Cut), 0m, null);
        var assembly = FactoryLoadCalculator.Row(Assembly, "Montagem", "Cabines", weeks, Days(16m, days: 60), Committed(plan, Assembly), 0m, null);

        (cut.Cells[0].CommittedHours, cut.Cells[0].CapacityHours, cut.Cells[0].Percent).Should().Be((8m, 8m, 100.0m), "26/10 a 01/11: só a sexta 30/10 conta");
        cut.Cells[0].Band.Should().Be(FactoryLoadBand.Full);
        (assembly.Cells[0].CommittedHours, assembly.Cells[0].CapacityHours, assembly.Cells[0].Percent).Should().Be((0m, 16m, 0.0m));
        assembly.Cells[0].Band.Should().Be(FactoryLoadBand.Idle);
        (cut.Cells[1].CommittedHours, cut.Cells[1].CapacityHours, cut.Cells[1].Percent).Should().Be((10m, 32m, 31.3m), "02/11 é feriado");
        (assembly.Cells[1].CommittedHours, assembly.Cells[1].CapacityHours, assembly.Cells[1].Percent).Should().Be((34m, 64m, 53.1m));
        cut.Cells.Skip(2).Should().OnlyContain(c => c.CommittedHours == 0m && c.CapacityHours == 40m);

        var factory = FactoryLoadCalculator.Factory(weeks, [cut, assembly]);
        factory.Name.Should().Be("Fábrica");
        (factory.Cells[0].CommittedHours, factory.Cells[0].CapacityHours, factory.Cells[0].Percent).Should().Be((8m, 24m, 33.3m), "Σ comprometidas ÷ Σ capacidades");
        (factory.Cells[1].CommittedHours, factory.Cells[1].CapacityHours, factory.Cells[1].Percent).Should().Be((44m, 96m, 45.8m));
        plan.Allocations.Sum(a => a.Hours).Should().Be(factory.Cells.Sum(c => c.CommittedHours), "nada do plano fica fora das 8 semanas");
    }

    [Theory]
    [InlineData(0, 40, FactoryLoadBand.Idle, 0.0)]
    [InlineData(23.96, 40, FactoryLoadBand.Idle, 59.9)]
    [InlineData(24, 40, FactoryLoadBand.Normal, 60.0)]
    [InlineData(37.98, 40, FactoryLoadBand.Normal, 95.0)]
    [InlineData(38, 40, FactoryLoadBand.Full, 95.0)]
    [InlineData(40, 40, FactoryLoadBand.Full, 100.0)]
    [InlineData(40.0001, 40, FactoryLoadBand.Over, 100.0)]
    [InlineData(41.6, 40, FactoryLoadBand.Over, 104.0)]
    public void Faixas_pela_razao_sem_arredondar(double committed, double capacity, FactoryLoadBand band, double percent)
    {
        var cell = FactoryLoadCalculator.Cell(Today, (decimal)committed, (decimal)capacity);
        cell.Band.Should().Be(band);
        cell.Percent.Should().Be((decimal)percent);
    }

    [Fact]
    public void Capacidade_zero_da_traco_ou_acima_da_capacidade()
    {
        var empty = FactoryLoadCalculator.Cell(Today, 0m, 0m);
        (empty.Percent, empty.Band).Should().Be(((decimal?)null, FactoryLoadBand.NoCapacity), "capacidade 0 → \"—\"");
        var dropped = FactoryLoadCalculator.Cell(Today, 8m, 0m);
        (dropped.Percent, dropped.Band).Should().Be(((decimal?)null, FactoryLoadBand.Over), "feriado novo num dia já planejado");
    }

    [Fact]
    public void Excecao_nova_depois_do_plano_deixa_a_celula_acima_da_capacidade()
    {
        // O plano foi gravado com a sexta 30/10 inteira; depois entrou uma parada de 4 h nesse dia (a capacidade lida agora caiu).
        var plan = FixedPlan();
        var weeks = FactoryLoadCalculator.Weeks(Today, 2);
        var stopped = new Dictionary<DateOnly, decimal>(Holiday) { [D(10, 30)] = 4m };
        var cut = FactoryLoadCalculator.Row(Cut, "Corte", null, weeks, Days(8m, stopped, 14), Committed(plan, Cut), 0m, null);
        (cut.Cells[0].CommittedHours, cut.Cells[0].CapacityHours, cut.Cells[0].Percent, cut.Cells[0].Band).Should().Be((8m, 4m, 200.0m, FactoryLoadBand.Over));
        cut.Cells[1].Band.Should().Be(FactoryLoadBand.Idle);
        FactoryLoadCalculator.Factory(weeks, [cut]).Cells[0].Band.Should().Be(FactoryLoadBand.Over);
    }

    [Fact]
    public void Fabrica_soma_as_horas_depois_e_pega_o_ultimo_dia()
    {
        var weeks = FactoryLoadCalculator.Weeks(Today, 1);
        var a = FactoryLoadCalculator.Row(Cut, "Corte", null, weeks, Days(8m, days: 3), new Dictionary<DateOnly, decimal>(), 30m, D(2, 19).AddYears(1));
        var b = FactoryLoadCalculator.Row(Assembly, "Montagem", null, weeks, Days(16m, days: 3), new Dictionary<DateOnly, decimal>(), 12m, D(12, 4));
        var factory = FactoryLoadCalculator.Factory(weeks, [a, b]);
        (factory.HoursAfter, factory.BusyUntil).Should().Be((42m, new DateOnly(2027, 2, 19)));
        var none = FactoryLoadCalculator.Factory(weeks, []);
        (none.HoursAfter, none.BusyUntil, none.Cells[0].Band).Should().Be((0m, (DateOnly?)null, FactoryLoadBand.NoCapacity));
    }
}
