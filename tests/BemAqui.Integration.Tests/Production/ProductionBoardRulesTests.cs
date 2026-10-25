using BemAqui.BuildingBlocks.Time;
using BemAqui.Production.Application;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Production;

/// <summary>PRD-02, regras sem banco: etapa atual e "Pronta para concluir" (CA 1), colunas pelo código (CA 2) e atraso e risco (CA 4).</summary>
public class ProductionBoardRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 22);

    private static BoardStep S(string code, string name, int sequence, ProductionStepStatus status = ProductionStepStatus.Waiting) => new(code, name, sequence, status);

    [Fact]
    public void Etapa_atual_e_a_primeira_nao_concluida()
    {
        var steps = new[] { S("P", "Projeto", 1, ProductionStepStatus.Done), S("C", "Corte", 2, ProductionStepStatus.Ready), S("M", "Montagem", 3) };

        BoardRules.CurrentIndex(steps).Should().Be(1);
        BoardRules.ColumnKey(steps).Should().Be("C");
    }

    [Fact]
    public void Todas_concluidas_vai_para_pronta_para_concluir_e_sem_etapa_para_sem_roteiro()
    {
        BoardRules.ColumnKey([S("P", "Projeto", 1, ProductionStepStatus.Done), S("E", "Expedição", 2, ProductionStepStatus.Done)]).Should().Be(BoardRules.ReadyKey);
        BoardRules.ColumnKey([]).Should().Be(BoardRules.NoRoutingKey);
        BoardRules.CurrentIndex([]).Should().Be(-1);
    }

    [Fact]
    public void Colunas_do_P04_na_ordem_do_roteiro()
    {
        IReadOnlyList<BoardStep> p04 = [S("P", "Projeto", 1), S("C", "Corte", 2), S("M", "Montagem", 3), S("Q", "Qualidade", 4), S("E", "Expedição", 5)];

        BoardRules.StepColumns([p04, p04]).Select(c => c.Code).Should().Equal("P", "C", "M", "Q", "E");
    }

    [Fact]
    public void Roteiros_diferentes_ordem_pela_menor_sequencia_empate_pela_letra_e_nome_mais_usado()
    {
        IReadOnlyList<BoardStep> a = [S("P", "Projeto", 1), S("C", "Corte", 2), S("M", "Montagem", 3)];
        IReadOnlyList<BoardStep> b = [S("C", "Corte a laser", 1), S("S", "Solda", 2), S("M", "Montagem", 3)];
        IReadOnlyList<BoardStep> c = [S("C", "Corte", 1), S("Q", "Qualidade", 2)];

        var columns = BoardRules.StepColumns([a, b, c]);

        // C aparece na sequência 1 (b, c) e P também na 1: empate pela letra. Q e S na 2 (empate pela letra), M na 3.
        columns.Select(x => x.Code).Should().Equal("C", "P", "Q", "S", "M");
        columns.Single(x => x.Code == "C").Name.Should().Be("Corte", "é o nome mais usado para C");
    }

    [Fact]
    public void Nome_empatado_fica_com_a_ordem_alfabetica()
    {
        var columns = BoardRules.StepColumns([[S("M", "Montagem", 1)], [S("M", "Mecânica", 1)]]);

        columns.Single().Name.Should().Be("Mecânica");
    }

    /// <summary>PLN-01b CA 9: atrasado = prometido antes de hoje; em risco = previsão depois da prometida, sem tolerância (a janela de 7 dias saiu).</summary>
    [Theory]
    [InlineData(-3, null, ProductionOrderStatus.InProgress, DeliveryRisk.Late, 3)]
    [InlineData(-1, 5, ProductionOrderStatus.Open, DeliveryRisk.Late, 1)]
    [InlineData(-5, null, ProductionOrderStatus.Completed, DeliveryRisk.None, null)]
    [InlineData(-5, 2, ProductionOrderStatus.Cancelled, DeliveryRisk.None, null)]
    [InlineData(0, 1, ProductionOrderStatus.InProgress, DeliveryRisk.AtRisk, null)]
    [InlineData(10, 11, ProductionOrderStatus.Open, DeliveryRisk.AtRisk, null)]
    [InlineData(10, 10, ProductionOrderStatus.Open, DeliveryRisk.None, null)]
    [InlineData(3, null, ProductionOrderStatus.InProgress, DeliveryRisk.None, null)]
    [InlineData(0, -2, ProductionOrderStatus.Quality, DeliveryRisk.None, null)]
    public void Atraso_e_risco_pela_funcao_unica(int promisedIn, int? forecastIn, ProductionOrderStatus status, DeliveryRisk expected, int? delay)
    {
        DateOnly? forecast = forecastIn is { } f ? Today.AddDays(f) : null;
        var result = ProductionOrderRisk.Evaluate(Today.AddDays(promisedIn), forecast, Today, status);

        result.Risk.Should().Be(expected);
        result.DelayDays.Should().Be(delay);
    }

    [Fact]
    public void Prometido_em_poucos_dias_sem_previsao_atrasada_nao_e_mais_risco() =>
        ProductionOrderRisk.Evaluate(Today.AddDays(3), Today.AddDays(2), Today, ProductionOrderStatus.Open).Should().Be(DeliveryAssessment.None);

    [Fact]
    public void Sem_data_prometida_nao_ha_risco() =>
        ProductionOrderRisk.Evaluate(null, Today.AddDays(30), Today, ProductionOrderStatus.Open).Should().Be(DeliveryAssessment.None);

    [Fact]
    public void Folga_em_dias_corridos()
    {
        DeliveryRiskRules.SlackDays(new DateOnly(2026, 11, 28), new DateOnly(2026, 12, 5)).Should().Be(-7);
        DeliveryRiskRules.SlackDays(new DateOnly(2026, 11, 28), new DateOnly(2026, 11, 25)).Should().Be(3);
        DeliveryRiskRules.SlackDays(null, new DateOnly(2026, 11, 25)).Should().BeNull();
        DeliveryRiskRules.SlackDays(new DateOnly(2026, 11, 28), null).Should().BeNull();
    }

    [Fact]
    public void Virada_do_dia_segue_America_Sao_Paulo()
    {
        // 02:30 UTC de 23/10 ainda é 23:30 de 22/10 em São Paulo: prometido 22/10 não está atrasado.
        var lateNight = new FixedClock(new DateTimeOffset(2026, 10, 23, 2, 30, 0, TimeSpan.Zero));
        var morning = new FixedClock(new DateTimeOffset(2026, 10, 23, 3, 30, 0, TimeSpan.Zero));
        var promised = new DateOnly(2026, 10, 22);

        ProductionOrderRisk.Evaluate(promised, promised.AddDays(1), BusinessTime.Today(lateNight), ProductionOrderStatus.InProgress).Risk.Should().Be(DeliveryRisk.AtRisk);
        ProductionOrderRisk.Evaluate(promised, null, BusinessTime.Today(morning), ProductionOrderStatus.InProgress).Should().Be(new DeliveryAssessment(DeliveryRisk.Late, 1));
    }

    [Fact]
    public void Horas_restantes_nunca_negativas()
    {
        BoardRules.Remaining(4m, 1.5m).Should().Be(2.5m);
        BoardRules.Remaining(2m, 3m).Should().Be(0m);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
