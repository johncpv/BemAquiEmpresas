using BemAqui.Production.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// PLN-03, regras puras (sem banco): o período padrão e o limite de 12 meses (CA 6), o período do Projeto 360 (CA 11), a interseção, a
/// proporção feita da etapa em andamento (CA 2), a etapa que define a entrega (CA 3) e as sub-linhas do centro (CA 5).
/// </summary>
public class GanttRulesTests
{
    private static DateOnly D(int month, int day, int year = 2026) => new(year, month, day);

    private static readonly DateOnly Today = D(10, 30);

    [Fact]
    public void Periodo_padrao_vai_de_7_dias_antes_a_12_semanas_depois()
    {
        var period = GanttRules.DefaultPeriod(Today);
        (period.From, period.To).Should().Be((D(10, 23), D(1, 22, 2027)));
        GanttRules.Resolve(Today, null, null).Should().Be(period);
    }

    [Fact]
    public void Periodo_pedido_ate_12_meses()
    {
        GanttRules.Resolve(Today, D(11, 1), D(11, 30)).Should().Be(new GanttPeriod(D(11, 1), D(11, 30)));
        GanttRules.Resolve(Today, D(1, 1), D(1, 1, 2027)).Should().NotBeNull("366 dias cabem");
        GanttRules.Resolve(Today, D(1, 1), D(1, 2, 2027)).Should().BeNull("passa de 12 meses");
        GanttRules.Resolve(Today, D(11, 30), D(11, 1)).Should().BeNull("fim antes do início");
        var onlyFrom = GanttRules.Resolve(Today, D(11, 2), null)!.Value;
        onlyFrom.Days.Should().Be(GanttRules.DefaultPeriod(Today).Days, "só o início: o tamanho do padrão");
        GanttRules.Resolve(Today, null, D(12, 31))!.Value.To.Should().Be(D(12, 31));
    }

    [Fact]
    public void Periodo_do_projeto_cabe_o_projeto_inteiro_sem_passar_de_12_meses()
    {
        var period = GanttRules.ProjectPeriod(Today, [D(8, 1), D(3, 15, 2027), null]);
        period.From.Should().Be(D(7, 25), "7 dias antes do primeiro início");
        period.To.Should().Be(D(3, 22, 2027));
        GanttRules.ProjectPeriod(Today, []).Should().Be(GanttRules.DefaultPeriod(Today));
        var huge = GanttRules.ProjectPeriod(Today, [D(1, 1), D(6, 1, 2028)]);
        huge.Days.Should().Be(GanttRules.MaxDays);
    }

    [Fact]
    public void Intersecao_inclui_as_pontas()
    {
        var period = new GanttPeriod(D(11, 1), D(11, 30));
        GanttRules.Intersects(D(10, 20), D(11, 1), period).Should().BeTrue();
        GanttRules.Intersects(D(11, 30), D(12, 5), period).Should().BeTrue();
        GanttRules.Intersects(D(10, 1), D(10, 31), period).Should().BeFalse();
        GanttRules.Intersects(D(12, 1), D(12, 2), period).Should().BeFalse();
    }

    [Theory]
    [InlineData(6, 2, 0.6667)]
    [InlineData(10, 10, 0)]
    [InlineData(10, 1, 0.9)]
    [InlineData(4, 0, 1)]
    [InlineData(0, 3, 0)]
    [InlineData(4, 6, 0)]
    public void Proporcao_feita_e_padrao_menos_restantes(decimal standard, decimal remaining, decimal expected) =>
        GanttRules.Progress(standard, remaining).Should().Be(expected);

    [Fact]
    public void Define_a_entrega_e_a_etapa_de_maior_fim_previsto()
    {
        var (a, b, c, d) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        GanttRules.DrivingStep([new(a, 0, 1, D(11, 5)), new(b, 0, 2, D(11, 20)), new(c, 1, 1, D(11, 12)), new(d, 1, 2, null)]).Should().Be(b);
        GanttRules.DrivingStep([new(a, 0, 2, D(11, 20)), new(b, 1, 1, D(11, 20))]).Should().Be(b, "no empate, a OS mais adiante");
        GanttRules.DrivingStep([new(a, 0, 1, D(11, 20)), new(b, 0, 2, D(11, 20))]).Should().Be(b, "na mesma OS, a de maior sequência");
        GanttRules.DrivingStep([new(a, 0, 1, null)]).Should().BeNull();
    }

    [Fact]
    public void Etapas_que_se_sobrepoem_no_dia_vao_para_sub_linhas()
    {
        var (a, b, c, d) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (lanes, count) = GanttRules.Lanes([
            new GanttSpan(a, D(11, 2), D(11, 4)),
            new GanttSpan(b, D(11, 4), D(11, 5)),
            new GanttSpan(c, D(11, 5), D(11, 6)),
            new GanttSpan(d, D(11, 9), D(11, 9)),
        ]);
        count.Should().Be(2);
        (lanes[a], lanes[b], lanes[c], lanes[d]).Should().Be((0, 1, 0, 0), "a e b se tocam em 04/11; c começa depois do fim de a; d cabe na primeira");
        GanttRules.Lanes([]).Count.Should().Be(0);
    }
}
