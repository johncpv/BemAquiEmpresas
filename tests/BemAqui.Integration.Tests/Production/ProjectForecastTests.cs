using BemAqui.Commercial.Application;
using BemAqui.Commercial.Domain;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// PLN-01b sem banco: a data prevista do projeto nos casos (a), (b) e (c) e sem previsão (CA 5 e 6), o prazo em dias úteis do calendário
/// padrão, a folga e o risco sem tolerância lidos pelo Commercial (CA 8 e 9) e a regra do aviso de risco (CA 10).
/// </summary>
public class ProjectForecastTests
{
    private static readonly Guid ProjectId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    // Calendário do cenário fixo da PLN-01a: seg a sex, feriado 02/11/2026; o fim da fabricação de P1 é quinta 05/11/2026.
    private static readonly HashSet<DateOnly> Holidays = [new(2026, 11, 2), new(2026, 11, 20)];

    private static bool Working(DateOnly d) => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !Holidays.Contains(d);

    private static DateOnly D(int month, int day) => new(2026, month, day);

    private static ProjectForecastOrder Open(string number, DateOnly? end, string? reason = null) => new(Guid.NewGuid(), number, false, end, reason);

    private static ProjectForecastResult Compute(IReadOnlyList<ProjectForecastOrder> orders, IReadOnlyList<DateOnly>? installations = null, bool budget = false, int days = 5) =>
        ProjectForecastCalculator.Compute(new ProjectForecastInput(ProjectId, orders, installations ?? [], budget), days, Working);

    [Fact]
    public void Caso_c_sem_instalacao_a_entrega_e_o_fim_da_fabricacao()
    {
        var result = Compute([Open("OS-1", D(11, 3)), Open("OS-2", D(11, 5))]);

        result.Should().Be(new ProjectForecastResult(ProjectId, D(11, 5), D(11, 5), null, null));
    }

    [Fact]
    public void Caso_b_custo_de_instalacao_no_orcamento_soma_5_dias_uteis()
    {
        // 05/11 (qui) + 5 dias úteis: 06, 09, 10, 11 e 12/11.
        Compute([Open("OS-1", D(11, 5))], budget: true).ForecastDate.Should().Be(D(11, 12));
    }

    [Fact]
    public void Caso_a_com_OS_de_instalacao_vale_o_maior_entre_prazo_e_fim_digitado()
    {
        Compute([Open("OS-1", D(11, 5))], [D(11, 10)]).ForecastDate.Should().Be(D(11, 12), "fim da fabricação + 5 dias úteis passa do fim digitado");
        Compute([Open("OS-1", D(11, 5))], [D(11, 10), D(11, 30)]).ForecastDate.Should().Be(D(11, 30), "o fim digitado da instalação passa do prazo");
    }

    [Fact]
    public void Prazo_pula_fim_de_semana_e_feriado_do_calendario()
    {
        // 18/11 (qua) + 3: 19, (20 feriado), 23, 24.
        ProjectForecastCalculator.AddWorkingDays(D(11, 18), 3, Working).Should().Be(D(11, 24));
        ProjectForecastCalculator.AddWorkingDays(D(11, 18), 0, Working).Should().Be(D(11, 18));
        Compute([Open("OS-1", D(11, 5))], budget: true, days: 0).ForecastDate.Should().Be(D(11, 5));
    }

    [Fact]
    public void Calendario_sem_dia_util_nao_trava()
    {
        var result = ProjectForecastCalculator.AddWorkingDays(D(11, 5), 5, _ => false);

        result.Should().BeAfter(D(11, 5));
    }

    [Fact]
    public void OS_concluida_conta_pela_data_da_conclusao()
    {
        var done = new ProjectForecastOrder(Guid.NewGuid(), "OS-1", true, D(11, 20), null);

        Compute([done, Open("OS-2", D(11, 9))]).FabricationEnd.Should().Be(D(11, 20));
    }

    [Theory]
    [InlineData(ProjectForecastReasons.NoRouting)]
    [InlineData(ProjectForecastReasons.NoWorkCenter)]
    [InlineData(ProjectForecastReasons.NoCapacity)]
    public void Alguma_OS_sem_previsao_deixa_a_data_vazia_com_o_motivo_e_a_OS(string reason)
    {
        var blocked = Open("OS-2", null, reason);

        var result = Compute([Open("OS-1", D(11, 5)), blocked], [D(11, 30)], budget: true);

        result.Should().Be(new ProjectForecastResult(ProjectId, null, null, reason, blocked.OrderId));
    }

    [Fact]
    public void Sem_OS_de_fabricacao_a_data_fica_vazia() =>
        Compute([]).Should().Be(new ProjectForecastResult(ProjectId, null, null, ProjectForecastReasons.NoFabricationOrder, null));

    [Fact]
    public void Motivo_do_motor_vira_codigo()
    {
        ProjectForecastCalculator.ReasonCode(FiniteScheduler.NoCenter("Corte")).Should().Be(ProjectForecastReasons.NoWorkCenter);
        ProjectForecastCalculator.ReasonCode(FiniteScheduler.NoCapacity("Montagem")).Should().Be(ProjectForecastReasons.NoCapacity);
        ProjectForecastReasons.All.Should().OnlyHaveUniqueItems().And.HaveCount(4);
    }

    [Fact]
    public void Folga_e_risco_sem_tolerancia_no_Projeto_360()
    {
        var today = D(11, 1);
        var row = new ProjectForecast { ProjectId = ProjectId, ForecastDate = D(12, 5), FabricationEnd = D(11, 28), ComputedAt = DateTimeOffset.UtcNow };

        var late = ProjectDelivery.Assess(ProjectStatus.InProgress, D(11, 28), row, null, today);
        late.Should().BeEquivalentTo(new { ForecastDate = (DateOnly?)D(12, 5), SlackDays = (int?)-7, Risk = DeliveryRisk.AtRisk, DelayDays = (int?)null });

        var onDay = ProjectDelivery.Assess(ProjectStatus.InProgress, D(12, 5), row, null, today);
        onDay.Risk.Should().Be(DeliveryRisk.None, "previsão no próprio dia prometido não é risco");
        onDay.SlackDays.Should().Be(0);
    }

    [Fact]
    public void Previsao_vazia_mostra_o_motivo_e_a_OS()
    {
        var orderId = Guid.NewGuid();
        var row = new ProjectForecast { ProjectId = ProjectId, Reason = ProjectForecastReasons.NoRouting, ReasonOrderId = orderId };

        var view = ProjectDelivery.Assess(ProjectStatus.Planned, D(11, 28), row, null, D(11, 1), "OS-2026-0007");

        view.Should().BeEquivalentTo(new { ForecastDate = (DateOnly?)null, Reason = ProjectForecastReasons.NoRouting, ReasonOrderId = (Guid?)orderId, ReasonOrderNumber = "OS-2026-0007", Risk = DeliveryRisk.None });
    }

    [Fact]
    public void A_partir_de_fabricado_o_risco_e_so_da_instalacao()
    {
        var row = new ProjectForecast { ProjectId = ProjectId, ForecastDate = D(11, 10) };

        var view = ProjectDelivery.Assess(ProjectStatus.Installing, D(11, 20), row, D(11, 25), D(11, 1));

        view.InstallationOnly.Should().BeTrue();
        view.ForecastDate.Should().Be(D(11, 25));
        view.Risk.Should().Be(DeliveryRisk.AtRisk);
        ProjectDelivery.Assess(ProjectStatus.Fabricated, D(11, 20), row, null, D(11, 1)).ForecastDate.Should().Be(D(11, 10), "sem OS de instalação, fica a previsão gravada");
        ProjectDelivery.Assess(ProjectStatus.Delivered, D(10, 20), row, null, D(11, 1)).Should().BeEquivalentTo(new { Risk = DeliveryRisk.None, SlackDays = (int?)null, DelayDays = (int?)null });
    }

    [Theory]
    [InlineData(null, 2, true)] // primeira previsão já em risco
    [InlineData(-3, 2, true)] // entrou em risco
    [InlineData(0, 2, true)] // entrou em risco (estava no próprio dia)
    [InlineData(2, 7, true)] // piorou
    [InlineData(7, 2, false)] // melhorou, ainda em risco
    [InlineData(2, 2, false)] // igual
    [InlineData(2, -1, false)] // saiu do risco
    [InlineData(null, 0, false)] // no dia prometido não é risco
    public void Aviso_ao_entrar_em_risco_ou_piorar(int? previousOffset, int nextOffset, bool warn)
    {
        var promised = D(11, 28);
        DateOnly? previous = previousOffset is { } p ? promised.AddDays(p) : null;

        ProjectForecastConsumer.ShouldWarn(promised, previous, promised.AddDays(nextOffset)).Should().Be(warn);
    }

    [Fact]
    public void Sem_previsao_ou_sem_prometido_nao_avisa()
    {
        ProjectForecastConsumer.ShouldWarn(D(11, 28), D(11, 20), null).Should().BeFalse();
        ProjectForecastConsumer.ShouldWarn(null, null, D(12, 30)).Should().BeFalse();
    }

    [Fact]
    public void Texto_link_e_chave_do_aviso()
    {
        ProjectForecastConsumer.Title("Ed. Monte Castelo", D(12, 5), D(11, 28))
            .Should().Be("Ed. Monte Castelo vai atrasar 7 dias: previsão 05/12/2026, prometido ao cliente 28/11/2026.");
        ProjectForecastConsumer.Title("Ed. Sol", D(11, 29), D(11, 28)).Should().Contain("vai atrasar 1 dia:");
        ProjectForecastConsumer.Link(ProjectId).Should().Be("/app/projetos/" + ProjectId);
        ProjectForecastConsumer.DedupKey(ProjectId, D(12, 5)).Should().Be($"project-at-risk:{ProjectId}:2026-12-05");
    }

    [Fact]
    public void Evento_leva_so_ids_datas_e_codigo()
    {
        typeof(ProjectForecastChanged).GetProperties().Select(p => Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType).Distinct()
            .Should().BeSubsetOf([typeof(Guid), typeof(DateOnly), typeof(DateTimeOffset), typeof(string)], "ADR 0019 §19: sem texto livre nem dado pessoal");
    }
}
