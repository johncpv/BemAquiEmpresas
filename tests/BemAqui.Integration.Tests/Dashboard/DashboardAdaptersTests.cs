using BemAqui.BuildingBlocks.Agenda;
using BemAqui.Dashboard.Application;
using BemAqui.Finance.Contracts;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Dashboard;

/// <summary>
/// DSH-01a CA 6, sem banco: os adaptadores do painel só traduzem os contratos dos donos (caixa da FIN-07, receita da FIN-08 e
/// pauta da DSH-04); cada número do card é o do contrato, sem recálculo.
/// </summary>
public class DashboardAdaptersTests
{
    private static readonly DateOnly Today = new(2026, 10, 16);

    private sealed class FakeForecast : ICashForecast
    {
        public List<int> Horizons { get; } = [];

        public Task<CashForecastSummary> SummaryAsync(int horizonDays, CancellationToken cancellationToken = default)
        {
            Horizons.Add(horizonDays);
            return Task.FromResult(new CashForecastSummary(Today, horizonDays, 1_000m, new CashForecastScenario(1_000m + horizonDays, null, null), new CashForecastScenario(900m + horizonDays, null, null), 0));
        }

        public Task<CashForecastWeeks> WeeksAsync(int horizonDays, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CashForecastWeeks(Today, horizonDays, 1_000.55m,
                [new CashForecastWeekBalance(Today, Today.AddDays(2), 1_100.10m, 1_050.20m), new CashForecastWeekBalance(Today.AddDays(3), Today.AddDays(9), 800m, -50.33m)],
                new CashForecastDay(Today.AddDays(8), 790m), new CashForecastDay(Today.AddDays(8), -60m), new CashForecastDay(Today.AddDays(7), -10m), 850m));
    }

    private sealed class FakeRevenue : IRevenueSummary
    {
        public (DateOnly From, DateOnly To) Asked { get; private set; }

        public Task<IReadOnlyList<MonthlyRevenue>> GrossRevenueAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            Asked = (from, to);
            return Task.FromResult<IReadOnlyList<MonthlyRevenue>>([new MonthlyRevenue(2026, 9, 100.10m, true), new MonthlyRevenue(2026, 10, 312_600.45m, true)]);
        }

        public Task<IReadOnlyList<ProjectMonthlyRevenue>> GrossRevenueByProjectsAsync(IReadOnlyCollection<Guid> projectIds, DateOnly from, DateOnly to, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProjectMonthlyRevenue>>([]);
    }

    private sealed class FakeAgenda : IAgendaService
    {
        public IReadOnlyCollection<string>? Asked { get; private set; }

        public IReadOnlyList<AgendaCategory> Registered { get; } =
        [
            new("finance.payment_orders", "finance", "finance.approve", AgendaTier.Unblocks, "as ordens de pagamento"),
            new("quotes.expiring", "quotes", "quotes.view", AgendaTier.DueSoon, "os orçamentos vencendo"),
        ];

        public Task<AgendaView> ForUserAsync(IReadOnlyCollection<string>? categories = null, CancellationToken cancellationToken = default)
        {
            Asked = categories;
            return Task.FromResult(new AgendaView(
            [
                new AgendaItem("finance.payment_orders", "3 ordens de pagamento aguardam você", "R$ 38.420,00 no total", "/app/financeiro/ordens-de-pagamento?aguardando=1", Today.AddDays(4), 38_420m, AgendaTier.Unblocks, 3, "Decidir", []),
                new AgendaItem("quotes.expiring", "2 orçamentos vencem", "Em 7 dias", "/app/orcamentos", Today.AddDays(2), null, AgendaTier.DueSoon, 2, "Ver", []),
            ], [], 5));
        }
    }

    [Fact]
    public async Task Caixa_vem_da_serie_semanal_e_dos_horizontes_da_FIN_07()
    {
        var forecast = new FakeForecast();
        var snapshot = await new CashForecastSource(forecast).SummaryAsync(12, TestContext.Current.CancellationToken);

        forecast.Horizons.Should().Equal(30, 60, 90);
        snapshot.CurrentBalance.Should().Be(1_000.55m);
        snapshot.Weeks.Select(w => (w.WeekEnd, w.BaseBalance, w.AdjustedBalance)).Should().Equal((Today.AddDays(2), 1_100.10m, 1_050.20m), (Today.AddDays(9), 800m, -50.33m));
        snapshot.Horizons.Select(h => (h.Days, h.BaseBalance, h.AdjustedBalance)).Should().Equal((30, 1_030m, 930m), (60, 1_060m, 960m), (90, 1_090m, 990m));
        snapshot.LowestBase.Should().Be(new CashForecastPoint(Today.AddDays(8), 790m));
        snapshot.LowestAdjusted.Should().Be(new CashForecastPoint(Today.AddDays(8), -60m));
        snapshot.Difference.Should().Be(850m, "a diferença é a do contrato, não recalculada");
        snapshot.FirstAdjustedNegative.Should().Be(new CashForecastPoint(Today.AddDays(7), -10m));
    }

    [Fact]
    public async Task Faturamento_e_a_receita_bruta_do_ano_ate_o_mes_pela_FIN_08()
    {
        var revenue = new FakeRevenue();
        var snapshot = await new RevenueSource(revenue).YearToDateAsync(Today, TestContext.Current.CancellationToken);

        revenue.Asked.Should().Be((new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 31)));
        snapshot.Months.Should().Equal(new RevenueMonth(9, 100.10m, true), new RevenueMonth(10, 312_600.45m, true));
    }

    [Fact]
    public async Task Decisoes_sao_o_nivel_1_da_pauta_da_DSH_04()
    {
        var agenda = new FakeAgenda();
        var items = await new DecisionAgenda(agenda).PendingAsync(TestContext.Current.CancellationToken);

        agenda.Asked.Should().Equal("finance.payment_orders");
        items.Should().ContainSingle().Which.Should().Be(new DecisionItem("finance.payment_orders", "3 ordens de pagamento aguardam você", "R$ 38.420,00 no total", 38_420m, Today.AddDays(4), "/app/financeiro/ordens-de-pagamento?aguardando=1", 3));
    }
}
