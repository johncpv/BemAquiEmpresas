using BemAqui.Dashboard.Application;
using BemAqui.Finance.Application;
using BemAqui.Integration.Tests.Persistence;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Dashboard;

/// <summary>
/// DSH-01a, regras sem banco: saudação e frase-resumo (CA 2), ordem e limite da faixa "Atenção" (CA 10), % da meta e dias
/// úteis que faltam (CA 9), chave do cache com geração e versão de permissão, validade de 60 s e "Atualizar" (CA 12 e 13).
/// </summary>
public class DashboardRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 16);
    private static readonly DateTimeOffset At = new(2026, 10, 16, 15, 0, 0, TimeSpan.Zero);

    private static CashForecastCard Cash(CashPointItem? negative) =>
        new(true, 100_000, Enumerable.Range(1, 12).Select(i => new CashWeekItem(Today.AddDays(7 * i), 1000, 900)).ToList(), [], new CashPointItem(Today, 1), new CashPointItem(Today, 1), 0, negative, At);

    private static DecisionsCard Decisions(params DecisionItem[] items) =>
        new(true, items.Length, items.Sum(i => i.Amount ?? 0), items.Where(i => i.DueDate is not null).Min(i => i.DueDate), items, At);

    private static OverdueCard Overdue(decimal total, OverdueLargestItem? largest = null) => new(Today, total, total > 0 ? 1 : 0, total > 0 ? 1 : 0, [], [], 9, largest, At);

    private static readonly OverdueLargestItem Largest = new(Guid.CreateVersion7(), "CR-2026-0001", Guid.CreateVersion7(), "Hospital São Lucas", 74_670m, 9, 11m);

    [Theory]
    [InlineData(0, "Bom dia")]
    [InlineData(11, "Bom dia")]
    [InlineData(12, "Boa tarde")]
    [InlineData(17, "Boa tarde")]
    [InlineData(18, "Boa noite")]
    [InlineData(23, "Boa noite")]
    public void Saudacao_pelo_horario(int hour, string expected) => DashboardRules.Greeting(hour).Should().Be(expected);

    [Theory]
    [InlineData("John Marcus da Mata", "John")]
    [InlineData("  Ana  ", "Ana")]
    [InlineData(null, "")]
    public void Primeiro_nome(string? name, string expected) => DashboardRules.FirstName(name).Should().Be(expected);

    [Fact]
    public void Frase_resumo_usa_so_os_cards_visiveis()
    {
        var item = new DecisionItem("payment_order", "OP-2026-0001", null, 100, Today.AddDays(3), "/app/financeiro/ordens-de-pagamento/1");
        DashboardRules.Summary(Cash(null), null, Decisions(item, item, item), null).Should().Be("Caixa positivo nas próximas 12 semanas; 3 decisões esperam você.");
        DashboardRules.Summary(Cash(new CashPointItem(new DateOnly(2026, 11, 27), -5000)), null, Decisions(item), Overdue(0))
            .Should().Be("Caixa ajustado fica negativo em 27/11; 1 decisão espera você; nada vencido a receber.");
        DashboardRules.Summary(null, new BalancesCard(Today, 214_380m, [], false, At), null, Overdue(82_870m))
            .Should().Be("R$ 214.380,00 em contas; R$ 82.870,00 a receber vencido.");
        DashboardRules.Summary(null, null, null, null).Should().BeNull("sem card visível não há frase");
        DashboardRules.Summary(new CashForecastCard(false, null, [], [], null, null, null, null, At), new BalancesCard(Today, 10m, [], false, At), new DecisionsCard(false, 0, 0, null, [], At), null)
            .Should().Be("R$ 10,00 em contas.", "card ainda não ligado não entra na frase");
    }

    [Fact]
    public void Faixa_Atencao_na_ordem_caixa_decisoes_vencido_e_com_link_para_a_acao()
    {
        var op = new DecisionItem("payment_order", "OP-2026-0007", null, 38_420m, new DateOnly(2026, 10, 20), "/app/financeiro/ordens-de-pagamento/7");
        var other = op with { Amount = 0, DueDate = null, Url = "/app/financeiro/ordens-de-pagamento/8" };
        var alerts = DashboardRules.Attention(Today, Cash(new CashPointItem(new DateOnly(2026, 11, 27), -1_500m)), Decisions(op, other), Overdue(74_670m, Largest));

        alerts.Select(a => a.Code).Should().Equal("cash_negative", "decisions", "overdue");
        alerts[0].Tone.Should().Be("crit");
        alerts[0].Url.Should().Be("/app/financeiro/fluxo-de-caixa?cenario=ajustado");
        alerts[1].Title.Should().Be("2 decisões aguardam você");
        alerts[1].Detail.Should().Be("R$ 38.420,00 no total; a primeira vence em 20/10.");
        alerts[2].Title.Should().Be("Hospital São Lucas com título vencido");
        alerts[2].Detail.Should().Be("R$ 74.670,00 vencidos há 9 dias. Atraso médio do cliente: 11 dias.");
        alerts[2].Url.Should().Be("/app/financeiro/receber/" + Largest.ReceivableId);
        alerts.Should().HaveCountLessThanOrEqualTo(DashboardRules.MaxAlerts);
    }

    [Fact]
    public void Sem_alerta_a_faixa_vem_vazia_e_caixa_negativo_depois_de_90_dias_nao_entra()
    {
        DashboardRules.Attention(Today, Cash(null), Decisions(), Overdue(0)).Should().BeEmpty();
        DashboardRules.Attention(Today, Cash(new CashPointItem(Today.AddDays(91), -1)), null, null).Should().BeEmpty();
        DashboardRules.Attention(Today, null, null, Overdue(10, Largest with { CustomerAvgDelayDays = null, DaysLate = 1 }))
            .Should().ContainSingle().Which.Detail.Should().Be("R$ 74.670,00 vencidos há 1 dia. Cliente sem histórico de pagamento.");
    }

    [Fact]
    public void Uma_decisao_leva_direto_ao_documento()
    {
        var op = new DecisionItem("payment_order", "OP-2026-0007", null, 10m, null, "/app/financeiro/ordens-de-pagamento/7");
        DashboardRules.Attention(Today, null, Decisions(op), null).Should().ContainSingle().Which.Should().BeEquivalentTo(new { Title = "1 decisão aguarda você", Detail = "R$ 10,00 no total.", Url = op.Url });
    }

    [Theory]
    [InlineData("312600", "380000", "82.3")]
    [InlineData("0", "1000", "0")]
    [InlineData("500", null, null)]
    [InlineData("500", "0", null)]
    [InlineData(null, "1000", null)]
    public void Percentual_da_meta_com_uma_casa(string? amount, string? target, string? expected)
    {
        static decimal? D(string? v) => v is null ? null : decimal.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
        DashboardRules.Pct(D(amount), D(target)).Should().Be(D(expected));
    }

    [Fact]
    public void Dias_uteis_que_faltam_pelo_calendario_com_feriado_e_parada_parcial()
    {
        Weekday[] week = [Weekday.Monday, Weekday.Tuesday, Weekday.Wednesday, Weekday.Thursday, Weekday.Friday];
        var end = new DateOnly(2026, 10, 31);
        BusinessDays.Count(week, new Dictionary<DateOnly, decimal>(), Today, end).Should().Be(11, "16/10 (sexta) até 31/10, contando hoje, como no protótipo");
        BusinessDays.Count(week, new Dictionary<DateOnly, decimal> { [new DateOnly(2026, 10, 28)] = 0, [new DateOnly(2026, 10, 29)] = 4, [new DateOnly(2026, 10, 31)] = 8 }, Today, end)
            .Should().Be(10, "feriado de 0 h sai; parada parcial conta; exceção no sábado não vira dia útil");
        BusinessDays.Count(week, new Dictionary<DateOnly, decimal>(), end, Today).Should().Be(0);
    }

    [Fact]
    public void Metas_do_ano_com_os_12_meses_e_total_so_dos_meses_com_meta()
    {
        var year = RevenueTargetService.ToYear(2026, new Dictionary<int, decimal> { [1] = 100m, [10] = 380_000m });
        year.Months.Should().HaveCount(12);
        year.Months.Single(m => m.Month == 10).Amount.Should().Be(380_000m);
        year.Months.Single(m => m.Month == 2).Amount.Should().BeNull();
        year.YearTotal.Should().Be(380_100m);
        RevenueTargetService.ToYear(2026, new Dictionary<int, decimal>()).YearTotal.Should().BeNull();
    }

    [Fact]
    public void Chave_do_cache_tem_tenant_usuario_versao_de_permissao_geracao_card_e_parametros()
    {
        var tenant = Guid.CreateVersion7();
        var user = Guid.CreateVersion7();
        DashboardCache.Key(tenant, user, "7", 3, "overdue", "top=5").Should().Be($"dashboard:{tenant}:{user}:7:3:overdue:top=5");
        DashboardCache.GenerationKey(tenant).Should().Be($"dashboard:{tenant}:gen");
    }

    [Fact]
    public async Task Cache_guarda_60_s_e_muda_com_a_versao_de_permissao_a_geracao_e_o_Atualizar()
    {
        var clock = new MutableClock(At);
        using var cache = new DashboardCache(clock);
        var tenant = Guid.CreateVersion7();
        var user = Guid.CreateVersion7();
        var calls = 0;
        Task<object> Compute(CancellationToken _) => Task.FromResult<object>(++calls);
        async Task<object> Get(string version) => (await cache.GetOrComputeAsync(new CardScope(tenant, user, version), "balances", string.Empty, Compute, TestContext.Current.CancellationToken)).Value;

        (await Get("1")).Should().Be(1);
        (await Get("1")).Should().Be(1, "dentro dos 60 s vem do cache");
        (await Get("2")).Should().Be(2, "a perda (ou ganho) de permissão muda a chave na hora");

        cache.Bump(tenant).Should().Be(1);
        (await Get("2")).Should().Be(3, "evento do tenant sobe a geração e invalida o painel dele");

        clock.Now = At.AddSeconds(30);
        cache.MarkRefresh(tenant, user);
        clock.Now = At.AddSeconds(31);
        (await Get("2")).Should().Be(4, "Atualizar ignora o que foi calculado antes");
        (await Get("2")).Should().Be(4);

        clock.Now = At.AddSeconds(31 + 60);
        (await Get("2")).Should().Be(5, "passados 60 s, recalcula");

        var other = Guid.CreateVersion7();
        (await cache.GetOrComputeAsync(new CardScope(other, user, "2"), "balances", string.Empty, Compute, TestContext.Current.CancellationToken)).Value.Should().Be(6, "outro tenant não lê o cache deste");
        cache.Generation(other).Should().Be(0);
    }
}
