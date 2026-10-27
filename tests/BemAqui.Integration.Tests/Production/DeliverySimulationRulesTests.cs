using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// PLN-05 sem banco: a posição do projeto virtual (CA 3) e o cenário fixo de datas (hoje sexta 30/10/2026, feriado 02/11, Corte 8 h/dia):
/// P1 com 16 h e P2 com 8 h na fila, o orçamento com 8 h; no fim e na posição 1, com o prazo de instalação de 5 dias úteis (CA 4) e quem
/// passa a atrasar.
/// </summary>
public class DeliverySimulationRulesTests
{
    private static readonly Weekday[] MonToFri = [Weekday.Monday, Weekday.Tuesday, Weekday.Wednesday, Weekday.Thursday, Weekday.Friday];
    private static readonly DateOnly Today = new(2026, 10, 30);
    private static readonly DateOnly Holiday = new(2026, 11, 2);
    private static readonly Guid Cut = Guid.Parse("00000000-0000-0000-0000-0000000000c1");

    private static DateOnly D(int month, int day) => new(2026, month, day);

    private static bool IsWorkingDay(DateOnly d) => MonToFri.Contains((Weekday)d.DayOfWeek) && d != Holiday;

    private static ScheduleOrderInput Order(Guid project, decimal hours) =>
        new(Guid.NewGuid(), "OS-" + hours, project, 1, [new ScheduleStepInput(Guid.NewGuid(), 1, "Corte", Cut, hours, 0m, ProductionStepStatus.Ready)]);

    /// <summary>A fila com o virtual em <paramref name="position"/>, como o contrato monta (as posições reais andam uma casa).</summary>
    private static (ScheduleResult Before, ScheduleResult After, Guid P1, Guid P2, Guid Virtual) Run(int? position)
    {
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        var quote = Guid.NewGuid();
        var order = new[] { p1, p2 };
        var at = DeliverySimulationRules.Position(position, order.Length);
        var ranks = DeliverySimulationRules.ShiftedPositions(order, at);
        var centers = new[]
        {
            new ScheduleCenterInput(Cut, "Corte", CapacityCalculator.Days(true, D(1, 1), 8m, MonToFri, new Dictionary<DateOnly, decimal> { [Holiday] = 0m }, Today,
                Today.AddDays(FiniteScheduler.HorizonDays - 1))),
        };
        var projects = new[] { new ScheduleProjectInput(p1, "PRJ-1", D(11, 3), D(10, 1), 1), new ScheduleProjectInput(p2, "PRJ-2", D(11, 4), D(10, 2), 2) };
        var orders = new[] { Order(p1, 16m), Order(p2, 8m) };
        var before = FiniteScheduler.Run(new ScheduleInput(Today, projects, orders, centers));
        var shifted = projects.Select(p => p with { QueuePosition = ranks[p.ProjectId] }).Append(new ScheduleProjectInput(quote, "ORC-1", null, Today, at)).ToList();
        var after = FiniteScheduler.Run(new ScheduleInput(Today, shifted, [.. orders, Order(quote, 8m)], centers));
        return (before, after, p1, p2, quote);
    }

    private static DateOnly? End(ScheduleResult result, Guid project) => result.Projects.Single(p => p.ProjectId == project).FabricationEnd;

    [Fact]
    public void Posicao_padrao_e_o_fim_e_vale_de_1_ate_o_tamanho_mais_1()
    {
        DeliverySimulationRules.Position(null, 3).Should().Be(4);
        DeliverySimulationRules.Position(1, 3).Should().Be(1);
        DeliverySimulationRules.Position(3, 3).Should().Be(3);
        DeliverySimulationRules.Position(4, 3).Should().Be(4);
        DeliverySimulationRules.Position(0, 3).Should().Be(4, "fora da faixa vai para o fim");
        DeliverySimulationRules.Position(9, 3).Should().Be(4);
        DeliverySimulationRules.Position(null, 0).Should().Be(1, "fila vazia: a fábrica está livre");

        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        DeliverySimulationRules.ShiftedPositions([a, b, c], 2).Should().BeEquivalentTo(new Dictionary<Guid, int> { [a] = 1, [b] = 3, [c] = 4 });
        DeliverySimulationRules.ShiftedPositions([a, b, c], 4).Should().BeEquivalentTo(new Dictionary<Guid, int> { [a] = 1, [b] = 2, [c] = 3 });
    }

    [Fact]
    public void No_fim_da_fila_o_orcamento_espera_os_outros_e_ninguem_atrasa()
    {
        var (before, after, p1, p2, quote) = Run(null);
        End(before, p1).Should().Be(D(11, 3), "30/10 + 03/11 (fim de semana e feriado de 02/11 no meio)");
        End(before, p2).Should().Be(D(11, 4));
        End(after, p1).Should().Be(D(11, 3));
        End(after, p2).Should().Be(D(11, 4));
        End(after, quote).Should().Be(D(11, 5), "o orçamento entra depois de P2");
        ProjectForecastCalculator.AddWorkingDays(D(11, 5), 5, IsWorkingDay).Should().Be(D(11, 12), "com instalação: 5 dias úteis depois do fim da fabricação");
        PlanningQueueService.Worsened(D(11, 3), D(11, 3), End(after, p1)).Should().BeFalse();
        PlanningQueueService.Worsened(D(11, 4), D(11, 4), End(after, p2)).Should().BeFalse();
    }

    [Fact]
    public void Na_posicao_1_o_orcamento_sai_hoje_e_os_dois_passam_a_atrasar()
    {
        var (before, after, p1, p2, quote) = Run(1);
        End(after, quote).Should().Be(Today, "8 h no Corte hoje");
        End(after, p1).Should().Be(D(11, 4));
        End(after, p2).Should().Be(D(11, 5));
        PlanningQueueService.Worsened(D(11, 3), End(before, p1), End(after, p1)).Should().BeTrue("P1 prometido 03/11 passa para 04/11");
        PlanningQueueService.Worsened(D(11, 4), End(before, p2), End(after, p2)).Should().BeTrue();
        var result = ProjectForecastCalculator.Compute(new ProjectForecastInput(quote, [new ProjectForecastOrder(Guid.NewGuid(), "001", false, End(after, quote), null)], [], true), 5, IsWorkingDay);
        result.FabricationEnd.Should().Be(Today);
        result.ForecastDate.Should().Be(D(11, 9), "03, 04, 05, 06 e 09/11 (o feriado de 02/11 não conta)");
    }
}
