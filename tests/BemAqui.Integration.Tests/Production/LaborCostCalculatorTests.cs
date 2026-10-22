using BemAqui.Production.Application;
using BemAqui.Production.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Production;

/// <summary>PRD-04 CA 1 a 6 sem banco: custo do apontamento, arredondamento, pausas fora, snapshot e versões.</summary>
public class LaborCostCalculatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 22, 11, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Duas_horas_e_meia_a_48_reais_dao_120()
    {
        LaborCostCalculator.Cost(150, 48.000000m).Should().Be(120.00m);
    }

    [Theory]
    [InlineData(7, "48.123456", "5.61")]
    [InlineData(1, "0.3", "0.01")]
    [InlineData(200, "35.5", "118.33")]
    [InlineData(61, "47.99", "48.79")]
    public void Arredonda_so_no_total_com_duas_casas_para_longe_do_zero(int minutes, string rate, string expected) =>
        LaborCostCalculator.Cost(minutes, decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture))
            .Should().Be(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture));

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    public void Sem_custo_hora_o_custo_e_zero(double? rate)
    {
        var hourly = rate is null ? (decimal?)null : (decimal)rate.Value;
        LaborCostCalculator.HasRate(hourly).Should().BeFalse();
        LaborCostCalculator.Cost(150, hourly).Should().Be(0m);
    }

    [Fact]
    public void Nenhuma_pausa_entra_no_custo()
    {
        PauseSpan P(int from, int minutes) => new(Start.AddMinutes(from), Start.AddMinutes(from + minutes));
        // 4 h com uma pausa de 10 min de cada motivo (falta de material, máquina, intervalo, fim do turno, outro): 190 min líquidos.
        var net = LaborRules.NetMinutes(Start, Start.AddHours(4), [P(10, 10), P(40, 10), P(80, 10), P(120, 10), P(200, 10)], Start.AddHours(4));
        net.Should().Be(190);
        LaborCostCalculator.Cost(net, 48m).Should().Be(152.00m);
    }

    [Fact]
    public void Aplicar_grava_snapshot_valor_versao_e_devolve_o_anterior()
    {
        var position = Guid.CreateVersion7();
        var entry = new LaborEntry();
        LaborCostCalculator.Apply(entry, 150, 48m, position).Should().Be(0m);
        (entry.HourlyCostSnapshot, entry.Cost, entry.CostMissing, entry.CostVersion, entry.CostPositionId).Should().Be((48m, 120m, false, 1, position));

        // Correção (CA 4): mesmo snapshot, horas novas; o custo-hora do cargo pode ter mudado (CA 3), o snapshot não.
        LaborCostCalculator.Apply(entry, 180, entry.HourlyCostSnapshot, entry.CostPositionId).Should().Be(120m);
        (entry.HourlyCostSnapshot, entry.Cost, entry.CostVersion).Should().Be((48m, 144m, 2));
    }

    [Fact]
    public void Sem_custo_hora_fica_marcado_e_o_recalculo_tira_a_marca()
    {
        var position = Guid.CreateVersion7();
        var entry = new LaborEntry();
        LaborCostCalculator.Apply(entry, 150, null, position);
        (entry.HourlyCostSnapshot, entry.Cost, entry.CostMissing, entry.CostVersion).Should().Be((null, 0m, true, 1));

        LaborCostCalculator.Apply(entry, 150, 40m, position).Should().Be(0m);
        (entry.HourlyCostSnapshot, entry.Cost, entry.CostMissing, entry.CostVersion).Should().Be((40m, 100m, false, 2));
    }
}