using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// PLN-01a sem banco: o motor de agendamento finito (CA 1 a 8) com o cenário fixo do CA 7 (hoje sexta 30/10/2026, feriado 02/11, Corte
/// 8 h/dia e Montagem 16 h/dia, P1/P2/P3) e a tabela de casos do plano de testes.
/// </summary>
public class FiniteSchedulerTests
{
    private static readonly Weekday[] MonToFri = [Weekday.Monday, Weekday.Tuesday, Weekday.Wednesday, Weekday.Thursday, Weekday.Friday];
    private static readonly DateOnly Today = new(2026, 10, 30);
    private static readonly Guid Cut = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid Assembly = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    private static DateOnly D(int month, int day) => new(2026, month, day);

    private static ScheduleCenterInput Center(Guid id, string name, decimal hours, Dictionary<DateOnly, decimal>? exceptions = null, bool active = true, DateOnly? createdOn = null) =>
        new(id, name, CapacityCalculator.Days(active, createdOn ?? new DateOnly(2026, 1, 1), hours, MonToFri,
            exceptions ?? new Dictionary<DateOnly, decimal> { [D(11, 2)] = 0m }, Today, Today.AddDays(FiniteScheduler.HorizonDays - 1)));

    private static ScheduleStepInput Step(string name, Guid? center, decimal standard, ProductionStepStatus status = ProductionStepStatus.Waiting, decimal logged = 0m, int sequence = 0) =>
        new(Guid.NewGuid(), sequence, name, center, standard, logged, status);

    private static ScheduleOrderInput Order(string number, Guid projectId, params ScheduleStepInput[] steps) =>
        new(Guid.NewGuid(), number, projectId, 1, [.. steps.Select((s, i) => s with { Sequence = i + 1 })]);

    private static ScheduleProjectInput Project(string number, DateOnly? promised, DateOnly? signed = null, int? position = null) =>
        new(Guid.NewGuid(), number, promised, signed ?? D(1, 10), position);

    private static ScheduleInput Input(IReadOnlyList<ScheduleProjectInput> projects, IReadOnlyList<ScheduleOrderInput> orders, params ScheduleCenterInput[] centers) =>
        new(Today, projects, orders, centers.Length == 0 ? [Center(Cut, "Corte", 8m), Center(Assembly, "Montagem", 16m)] : centers);

    private static string Allocation(ScheduleResult result, Guid stepId) =>
        string.Join("; ", result.Allocations.Where(a => a.StepId == stepId).Select(a => $"{a.Date:dd/MM}: {a.Hours:0.####} h"));

    private sealed record Fixed(ScheduleInput Input, ScheduleProjectInput P1, ScheduleProjectInput P2, ScheduleProjectInput P3, ScheduleOrderInput Os1, ScheduleOrderInput Os2, ScheduleOrderInput Os3);

    /// <summary>O cenário do CA 7: P1, P2 e P3 com datas prometidas nessa ordem (a lista entra embaralhada).</summary>
    private static Fixed Scenario()
    {
        var p1 = Project("PRJ-2026-0003", D(12, 1));
        var p2 = Project("PRJ-2026-0001", D(12, 10));
        var p3 = Project("PRJ-2026-0002", D(12, 20));
        var os1 = Order("OS-1", p1.ProjectId, Step("Corte", Cut, 12m, ProductionStepStatus.Ready), Step("Montagem", Assembly, 20m));
        var os2 = Order("OS-2", p2.ProjectId, Step("Corte", Cut, 6m, ProductionStepStatus.InProgress, 4m), Step("Montagem", Assembly, 10m));
        var os3 = Order("OS-3", p3.ProjectId, Step("Corte", Cut, 4m, ProductionStepStatus.Ready), Step("Montagem", Assembly, 4m));
        return new Fixed(Input([p3, p1, p2], [os3, os2, os1]), p1, p2, p3, os1, os2, os3);
    }

    [Fact]
    public void Cenario_fixo_do_CA_7_bate_as_datas_esperadas()
    {
        var s = Scenario();
        var result = FiniteScheduler.Run(s.Input);
        var steps = result.Steps.ToDictionary(x => x.StepId);

        void Expect(ScheduleStepInput step, decimal hours, DateOnly start, DateOnly end, string allocation)
        {
            var planned = steps[step.StepId];
            planned.Hours.Should().Be(hours, step.Name);
            planned.Start.Should().Be(start, step.Name);
            planned.End.Should().Be(end, step.Name);
            planned.NoForecastReason.Should().BeNull();
            Allocation(result, step.StepId).Should().Be(allocation);
        }

        Expect(s.Os2.Steps[0], 2m, D(10, 30), D(10, 30), "30/10: 2 h");
        Expect(s.Os1.Steps[0], 12m, D(10, 30), D(11, 3), "30/10: 6 h; 03/11: 6 h");
        Expect(s.Os1.Steps[1], 20m, D(11, 4), D(11, 5), "04/11: 16 h; 05/11: 4 h");
        Expect(s.Os2.Steps[1], 10m, D(11, 3), D(11, 3), "03/11: 10 h");
        Expect(s.Os3.Steps[0], 4m, D(11, 3), D(11, 4), "03/11: 2 h; 04/11: 2 h");
        Expect(s.Os3.Steps[1], 4m, D(11, 5), D(11, 5), "05/11: 4 h");

        var projects = result.Projects.ToDictionary(p => p.ProjectId, p => p.FabricationEnd);
        projects[s.P1.ProjectId].Should().Be(D(11, 5));
        projects[s.P2.ProjectId].Should().Be(D(11, 3));
        projects[s.P3.ProjectId].Should().Be(D(11, 5));
        var orders = result.Orders.ToDictionary(o => o.OrderId);
        orders[s.Os1.OrderId].Should().Be(new ScheduledOrder(s.Os1.OrderId, s.P1.ProjectId, D(10, 30), D(11, 5), null));
        orders[s.Os2.OrderId].Start.Should().Be(D(10, 30));
        orders[s.Os3.OrderId].Start.Should().Be(D(11, 3));
    }

    [Fact]
    public void Mesma_entrada_da_sempre_a_mesma_saida()
    {
        var s = Scenario();
        var first = FiniteScheduler.Run(s.Input);
        var reversed = s.Input with { Projects = [.. s.Input.Projects.Reverse()], Orders = [.. s.Input.Orders.Reverse()] };
        for (var i = 0; i < 3; i++)
        {
            var again = FiniteScheduler.Run(i % 2 == 0 ? s.Input : reversed);
            again.Steps.Should().BeEquivalentTo(first.Steps, o => o.WithStrictOrdering());
            again.Allocations.Should().BeEquivalentTo(first.Allocations, o => o.WithStrictOrdering());
            again.Orders.Should().BeEquivalentTo(first.Orders, o => o.WithStrictOrdering());
        }
    }

    [Fact]
    public void Fila_pela_data_prometida_depois_assinatura_e_numero_sem_data_no_fim()
    {
        var late = Project("PRJ-1", D(12, 20));
        var early = Project("PRJ-9", D(11, 20));
        var sameDateOlderSignature = Project("PRJ-8", D(11, 25), D(1, 5));
        var sameDateNewerSignature = Project("PRJ-2", D(11, 25), D(3, 5));
        var tieA = Project("PRJ-5", D(11, 30), D(2, 1));
        var tieB = Project("PRJ-4", D(11, 30), D(2, 1));
        var noDateOld = Project("PRJ-7", null, D(1, 1));
        var noDateNew = Project("PRJ-3", null, D(5, 1));
        var queue = FiniteScheduler.Queue([noDateNew, late, tieA, noDateOld, sameDateNewerSignature, early, tieB, sameDateOlderSignature]);
        queue.Select(p => p.Number).Should().Equal("PRJ-9", "PRJ-8", "PRJ-2", "PRJ-4", "PRJ-5", "PRJ-1", "PRJ-7", "PRJ-3");
    }

    [Fact]
    public void Com_a_fila_manual_vale_a_posicao()
    {
        var a = Project("PRJ-A", D(11, 1), position: 2);
        var b = Project("PRJ-B", D(12, 1), position: 1);
        var c = Project("PRJ-C", D(10, 31));
        FiniteScheduler.Queue([a, b, c]).Select(p => p.Number).Should().Equal("PRJ-B", "PRJ-A", "PRJ-C");
    }

    [Theory]
    [InlineData(ProductionStepStatus.Done, 6, 3, 0)]
    [InlineData(ProductionStepStatus.Waiting, 6, 0, 6)]
    [InlineData(ProductionStepStatus.Ready, 6, 0, 6)]
    [InlineData(ProductionStepStatus.InProgress, 6, 4, 2)]
    [InlineData(ProductionStepStatus.Paused, 6, 1.5, 4.5)]
    [InlineData(ProductionStepStatus.InProgress, 6, 7, 0.6)]
    [InlineData(ProductionStepStatus.Paused, 6, 5.8, 0.6)]
    [InlineData(ProductionStepStatus.Rejected, 6, 9, 6)]
    public void Horas_restantes(ProductionStepStatus status, double standard, double logged, double expected) =>
        FiniteScheduler.RemainingHours(status, (decimal)standard, (decimal)logged).Should().Be((decimal)expected);

    [Fact]
    public void Retrabalho_usa_as_horas_padrao_inteiras()
    {
        // A etapa devolvida pela qualidade volta a Ready/Waiting: as horas apontadas da tentativa anterior não descontam.
        var p = Project("PRJ-1", D(12, 1));
        var rework = Step("Corte", Cut, 6m, ProductionStepStatus.Ready, logged: 5m);
        var order = Order("OS-1", p.ProjectId, Step("Projeto", Cut, 2m, ProductionStepStatus.Done), rework);
        var result = FiniteScheduler.Run(Input([p], [order]));
        result.Steps.Single(x => x.StepId == rework.StepId).Hours.Should().Be(6m);
        Allocation(result, rework.StepId).Should().Be("30/10: 6 h");
    }

    [Fact]
    public void Etapa_em_andamento_entra_antes_da_fila()
    {
        var first = Project("PRJ-1", D(11, 10));
        var last = Project("PRJ-2", D(12, 20));
        var queued = Order("OS-1", first.ProjectId, Step("Corte", Cut, 8m, ProductionStepStatus.Ready));
        var running = Step("Corte", Cut, 10m, ProductionStepStatus.Paused, 5m);
        var onMachine = Order("OS-2", last.ProjectId, running);
        var result = FiniteScheduler.Run(Input([first, last], [queued, onMachine]));
        Allocation(result, running.StepId).Should().Be("30/10: 5 h");
        Allocation(result, queued.Steps[0].StepId).Should().Be("30/10: 3 h; 03/11: 5 h");
    }

    [Fact]
    public void Etapa_seguinte_comeca_no_dia_util_seguinte_e_a_menor_prioridade_usa_a_sobra_sem_atrasar()
    {
        var high = Project("PRJ-1", D(11, 10));
        var low = Project("PRJ-2", D(12, 10));
        var a = Order("OS-1", high.ProjectId, Step("Corte", Cut, 4m, ProductionStepStatus.Ready), Step("Montagem", Assembly, 8m));
        var b = Order("OS-2", low.ProjectId, Step("Montagem", Assembly, 20m, ProductionStepStatus.Ready));
        var result = FiniteScheduler.Run(Input([high, low], [a, b]));
        var steps = result.Steps.ToDictionary(x => x.StepId);
        steps[a.Steps[0].StepId].End.Should().Be(D(10, 30));
        steps[a.Steps[1].StepId].Start.Should().Be(D(11, 3), "o dia seguinte é sábado; domingo e o feriado de 02/11 não são úteis");
        Allocation(result, a.Steps[1].StepId).Should().Be("03/11: 8 h");
        Allocation(result, b.Steps[0].StepId).Should().Be("30/10: 16 h; 03/11: 4 h", "a OS de menor prioridade ocupa a sobra antes da de maior, sem atrasá-la");
    }

    [Fact]
    public void OS_do_mesmo_projeto_nao_esperam_uma_pela_outra()
    {
        var p = Project("PRJ-1", D(11, 10));
        var first = Order("OS-1", p.ProjectId, Step("Corte", Cut, 8m, ProductionStepStatus.Ready));
        var second = Order("OS-2", p.ProjectId, Step("Montagem", Assembly, 8m, ProductionStepStatus.Ready)) with { ItemSequence = 2 };
        var result = FiniteScheduler.Run(Input([p], [second, first]));
        result.Steps.Should().OnlyContain(s => s.Start == D(10, 30) && s.End == D(10, 30));
    }

    [Fact]
    public void Parada_parcial_reduz_as_horas_do_dia()
    {
        var p = Project("PRJ-1", D(11, 10));
        var order = Order("OS-1", p.ProjectId, Step("Corte", Cut, 10m, ProductionStepStatus.Ready));
        var stoppage = new Dictionary<DateOnly, decimal> { [D(10, 30)] = 4m, [D(11, 2)] = 0m };
        var result = FiniteScheduler.Run(Input([p], [order], Center(Cut, "Corte", 8m, stoppage)));
        Allocation(result, order.Steps[0].StepId).Should().Be("30/10: 4 h; 03/11: 6 h");
    }

    [Fact]
    public void Centro_criado_depois_de_hoje_comeca_na_criacao()
    {
        var p = Project("PRJ-1", D(11, 10));
        var order = Order("OS-1", p.ProjectId, Step("Corte", Cut, 8m, ProductionStepStatus.Ready));
        var result = FiniteScheduler.Run(Input([p], [order], Center(Cut, "Corte", 8m, createdOn: D(11, 10))));
        Allocation(result, order.Steps[0].StepId).Should().Be("10/11: 8 h");
    }

    [Fact]
    public void Centro_inativo_ou_sem_capacidade_deixa_a_etapa_e_as_seguintes_sem_data()
    {
        var p = Project("PRJ-1", D(11, 10));
        var order = Order("OS-1", p.ProjectId, Step("Corte", Cut, 8m, ProductionStepStatus.Ready), Step("Montagem", Assembly, 8m));
        var result = FiniteScheduler.Run(Input([p], [order], Center(Cut, "Corte", 8m, active: false), Center(Assembly, "Montagem", 16m)));
        result.Steps.Should().OnlyContain(s => s.Start == null && s.End == null && s.NoForecastReason == "Centro Corte sem capacidade nos próximos 2 anos");
        result.Orders.Single().NoForecastReason.Should().Be("Centro Corte sem capacidade nos próximos 2 anos");
        result.Projects.Single().FabricationEnd.Should().BeNull();
        result.Allocations.Should().BeEmpty("nada fica ocupado pela etapa sem data");
    }

    [Fact]
    public void Etapa_sem_centro_fica_sem_data_com_o_motivo()
    {
        var p = Project("PRJ-1", D(11, 10));
        var order = Order("OS-1", p.ProjectId, Step("Pintura", null, 4m, ProductionStepStatus.Ready), Step("Montagem", Assembly, 8m));
        var result = FiniteScheduler.Run(Input([p], [order]));
        result.Steps.Select(s => s.NoForecastReason).Should().Equal("Etapa Pintura sem centro de trabalho", "Etapa Pintura sem centro de trabalho");
        result.Orders.Single().Should().Be(new ScheduledOrder(order.OrderId, p.ProjectId, null, null, "Etapa Pintura sem centro de trabalho"));
    }

    [Fact]
    public void Horas_alem_do_horizonte_de_2_anos_ficam_sem_data_e_liberam_a_capacidade()
    {
        var p = Project("PRJ-1", D(11, 10));
        var q = Project("PRJ-2", D(12, 10));
        var huge = Order("OS-1", p.ProjectId, Step("Corte", Cut, 100_000m, ProductionStepStatus.Ready));
        var small = Order("OS-2", q.ProjectId, Step("Corte", Cut, 4m, ProductionStepStatus.Ready));
        var result = FiniteScheduler.Run(Input([p, q], [huge, small]));
        result.Steps.Single(s => s.StepId == huge.Steps[0].StepId).NoForecastReason.Should().Be("Centro Corte sem capacidade nos próximos 2 anos");
        Allocation(result, small.Steps[0].StepId).Should().Be("30/10: 4 h");
    }

    [Fact]
    public void Etapa_com_zero_horas_comeca_e_termina_no_dia_em_que_poderia_comecar()
    {
        var p = Project("PRJ-1", D(11, 10));
        var order = Order("OS-1", p.ProjectId, Step("Corte", Cut, 8m, ProductionStepStatus.Ready), Step("Conferência", Assembly, 0m), Step("Montagem", Assembly, 4m));
        var result = FiniteScheduler.Run(Input([p], [order]));
        var steps = result.Steps.ToDictionary(x => x.StepId);
        steps[order.Steps[1].StepId].Should().Match<ScheduledStep>(s => s.Start == D(11, 3) && s.End == D(11, 3) && s.Hours == 0m);
        Allocation(result, order.Steps[1].StepId).Should().BeEmpty();
        steps[order.Steps[2].StepId].Start.Should().Be(D(11, 4));
    }

    [Fact]
    public void Etapas_concluidas_nao_alocam_e_OS_sem_etapas_fica_fora()
    {
        var p = Project("PRJ-1", D(11, 10));
        var done = Step("Corte", Cut, 8m, ProductionStepStatus.Done);
        var order = Order("OS-1", p.ProjectId, done, Step("Montagem", Assembly, 4m, ProductionStepStatus.Ready));
        var noRouting = new ScheduleOrderInput(Guid.NewGuid(), "OS-2", p.ProjectId, 2, []);
        var result = FiniteScheduler.Run(Input([p], [order, noRouting]));
        result.Steps.Should().ContainSingle().Which.Start.Should().Be(D(10, 30));
        result.Orders.Should().ContainSingle(o => o.OrderId == order.OrderId);
        Allocation(result, done.StepId).Should().BeEmpty();
    }

    [Fact]
    public void Trezentas_OS_com_cinco_etapas_e_seis_centros_rodam_rapido()
    {
        var centers = Enumerable.Range(1, 6).Select(i => Center(Guid.NewGuid(), "Centro " + i, 8m)).ToArray();
        var projects = Enumerable.Range(1, 300).Select(i => Project($"PRJ-{i:0000}", Today.AddDays(i % 90))).ToList();
        var orders = projects.Select((p, i) => Order($"OS-{i:0000}", p.ProjectId,
            [.. Enumerable.Range(0, 5).Select(k => Step("Etapa " + k, centers[(i + k) % 6].WorkCenterId, 6m + k, k == 0 ? ProductionStepStatus.Ready : ProductionStepStatus.Waiting))])).ToList();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = FiniteScheduler.Run(Input(projects, orders, centers));
        watch.Stop();
        result.Steps.Should().HaveCount(1500).And.OnlyContain(s => s.End != null);
        watch.ElapsedMilliseconds.Should().BeLessThan(2000, "o motor de 300 OS cabe com folga no limite das 50 OS (CA 17)");
    }
}
