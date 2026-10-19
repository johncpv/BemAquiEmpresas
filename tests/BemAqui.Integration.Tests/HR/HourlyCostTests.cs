using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.HR;

/// <summary>CAD-04a CA 2, 3, 4 e 15 sem banco: fórmula do custo-hora, média da faixa e horas da jornada.</summary>
public class HourlyCostTests
{
    [Theory]
    [InlineData(3300, 80, 220, 27)]
    [InlineData(3300, 0, 220, 15)]
    [InlineData(0, 80, 220, 0)]
    [InlineData(3300, 80, 0, 0)]
    [InlineData(2500, 80, 200, 22.5)]
    public void Formula(decimal salary, decimal pct, decimal hours, decimal expected) =>
        HourlyCostCalculator.Compute(salary, pct, hours).Should().Be(expected);

    [Fact]
    public void Arredonda_so_no_fim_com_6_casas() => HourlyCostCalculator.Compute(1000m, 80m, 220m).Should().Be(8.181818m);

    [Fact]
    public void Media_da_faixa() => HourlyCostCalculator.Average(3000m, 3600m).Should().Be(3300m);

    [Fact]
    public void Jornada_de_44_horas()
    {
        ScheduleDay[] days = [.. Enumerable.Range(1, 5).Select(d => new ScheduleDay(d, "08:00", "17:48", 60))];
        ScheduleMath.Validate(days).Should().BeNull();
        ScheduleMath.WeeklyHours(days).Should().Be(44.00m);
        ScheduleMath.MonthlyHours(44m).Should().Be(220m);
        ScheduleMath.Validate([new ScheduleDay(1, "17:00", "08:00", 0)]).Should().Be("A saída precisa ser depois da entrada.");
        ScheduleMath.Validate([new ScheduleDay(1, "08:00", "09:00", 60)]).Should().StartWith("O intervalo precisa ser menor");
    }
}
