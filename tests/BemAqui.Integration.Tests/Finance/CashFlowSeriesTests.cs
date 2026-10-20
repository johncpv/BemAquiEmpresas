using BemAqui.Finance.Application;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>FIN-05: série do fluxo de caixa sem banco (CA 4, 7 e 8).</summary>
public class CashFlowSeriesTests
{
    private static readonly DateOnly Today = new(2026, 10, 21); // quarta-feira

    private static Dictionary<DateOnly, CashFlowDayTotals> Moves() => new()
    {
        [Today.AddDays(-2)] = new(100m, 30m),
        [Today] = new(50m, 20m, OverdueInflow: 50m),
        [Today.AddDays(3)] = new(0m, 400m),
        [Today.AddDays(12)] = new(1000m, 0m),
    };

    [Fact]
    public void Diaria_acumula_o_saldo_e_separa_realizado_de_projetado()
    {
        var days = CashFlowSeries.Days(Today.AddDays(-2), Today.AddDays(20), Today, 200m, Moves());
        days.Should().HaveCount(23);
        days[0].Balance.Should().Be(270m);
        days[0].Projected.Should().BeFalse();
        days[2].Should().Match<CashFlowPeriod>(d => d.ContainsToday && d.Projected && d.Balance == 300m && d.OverdueInflow == 50m);
        days[5].Balance.Should().Be(-100m);
        days[^1].Balance.Should().Be(900m);
    }

    [Fact]
    public void Semanal_e_mensal_somam_os_dias_e_terminam_no_saldo_do_ultimo_dia()
    {
        var days = CashFlowSeries.Days(Today.AddDays(-2), Today.AddDays(40), Today, 200m, Moves());
        foreach (var view in new[] { CashFlowSeries.Weekly, CashFlowSeries.Monthly })
        {
            var periods = CashFlowSeries.Aggregate(days, view);
            periods.Sum(p => p.Inflow).Should().Be(days.Sum(d => d.Inflow), view);
            periods.Sum(p => p.Outflow).Should().Be(days.Sum(d => d.Outflow), view);
            periods[^1].Balance.Should().Be(days[^1].Balance, view);
        }

        var weeks = CashFlowSeries.Aggregate(days, CashFlowSeries.Weekly);
        weeks[0].Start.Should().Be(Today.AddDays(-2));
        weeks[0].End.DayOfWeek.Should().Be(DayOfWeek.Sunday, "semana de segunda a domingo");
        weeks[1].Start.DayOfWeek.Should().Be(DayOfWeek.Monday);
        CashFlowSeries.Aggregate(days, CashFlowSeries.Monthly).Select(m => m.Start.Month).Should().Equal(10, 11);
    }

    [Fact]
    public void Destaca_o_menor_saldo_e_o_primeiro_dia_negativo_so_no_projetado()
    {
        var days = CashFlowSeries.Days(Today.AddDays(-2), Today.AddDays(20), Today, -500m, Moves());
        var (lowest, negative) = CashFlowSeries.Highlights(days);
        lowest.Should().Be(new CashFlowPoint(Today.AddDays(3), -800m));
        negative.Should().Be(new CashFlowPoint(Today, -400m), "o realizado negativo de antes de hoje não conta");
        CashFlowSeries.Highlights(CashFlowSeries.Days(Today.AddDays(-2), Today, Today, 0, Moves(), project: false)).Lowest.Should().BeNull();
    }

    [Fact]
    public void Recorrente_projeta_so_as_competencias_sem_titulo()
    {
        static DateOnly Due(DateOnly c) => new(c.Year, c.Month, Math.Min(31, DateTime.DaysInMonth(c.Year, c.Month)));
        var generated = new HashSet<DateOnly> { new(2026, 10, 1) };
        var occurrences = CashFlowSeries.Occurrences(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), Due, Today, Today.AddDays(120), generated).ToList();
        occurrences.Select(o => o.Due).Should().Equal(new DateOnly(2026, 11, 30), new DateOnly(2026, 12, 31), new DateOnly(2027, 1, 31));
    }
}
