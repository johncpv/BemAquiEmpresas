using BemAqui.Commercial.Contracts;
using BemAqui.Dashboard.Application;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Dashboard;

/// <summary>
/// DSH-01b, regras sem banco: ordem da tabela de projetos e carteira (CA 1 e 3), entregas do mês e o projeto que mais vai atrasar, a faixa
/// "Atenção" com o projeto (CA 4) e a montagem do resultado por projeto (CA 6 e 7) com casos fixos.
/// </summary>
public class OwnerRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 16);
    private static readonly DateTimeOffset At = new(2026, 10, 16, 15, 0, 0, TimeSpan.Zero);

    private static ProjectPortfolioItem P(
        string number, string status, decimal contracted, DeliveryRisk risk = DeliveryRisk.None, int? delay = null, int? slack = null, DateOnly? promised = null,
        DateOnly? forecast = null, DateOnly? delivered = null, string contract = "Active") =>
        new(Guid.CreateVersion7(), number, "Projeto " + number, Guid.CreateVersion7(), status, contract, Today.AddDays(-90), contracted, delivered,
            new ProjectForecastSummary(Guid.Empty, promised, forecast, null, null, slack, risk, delay, At));

    [Fact]
    public void Tabela_de_projetos_poe_atrasados_e_em_risco_primeiro_e_deixa_os_encerrados_fora()
    {
        var list = new[]
        {
            P("P-05", "Planned", 10, promised: Today.AddDays(60)),
            P("P-01", "InProgress", 10, DeliveryRisk.AtRisk, slack: -3, promised: Today.AddDays(5)),
            P("P-02", "Installing", 10, DeliveryRisk.Late, delay: 2, promised: Today.AddDays(-2)),
            P("P-03", "Fabricated", 10, DeliveryRisk.Late, delay: 9, promised: Today.AddDays(-9)),
            P("P-04", "InProgress", 10, DeliveryRisk.AtRisk, slack: -12, promised: Today.AddDays(20)),
            P("P-06", "InProgress", 10, promised: Today.AddDays(30)),
            P("P-07", "InProgress", 10),
            P("P-08", "Delivered", 10, delivered: Today, contract: "Completed"),
            P("P-09", "Closed", 10, contract: "Completed"),
        };
        OwnerRules.ActiveOrder(list).Select(p => p.Number).Should().Equal("P-03", "P-02", "P-04", "P-01", "P-06", "P-05", "P-07");
    }

    [Fact]
    public void Carteira_a_produzir_so_dos_nao_fabricados_e_a_faturar_dos_contratos_ativos()
    {
        var list = new[]
        {
            P("A", "Planned", 100_000m), P("B", "InProgress", 60_000.55m), P("C", "Fabricated", 30_000m), P("D", "Installing", 20_000m),
            P("E", "Delivered", 70_000m, contract: "Completed"), P("F", "Delivered", 5_000m),
        };
        OwnerRules.ToProduce(list).Should().Be((160_000.55m, 2));
        OwnerRules.ToInvoiceProjects(list).Select(p => p.Number).Should().Equal("A", "B", "C", "D", "F");
    }

    [Fact]
    public void Entregas_do_mes_e_o_projeto_que_mais_vai_atrasar()
    {
        var list = new[]
        {
            P("E1", "InProgress", 1, forecast: new DateOnly(2026, 10, 30), promised: new DateOnly(2026, 11, 5), slack: 6),
            P("E2", "InProgress", 1, DeliveryRisk.AtRisk, forecast: new DateOnly(2026, 11, 20), promised: new DateOnly(2026, 11, 5), slack: -15),
            P("E3", "Planned", 1, DeliveryRisk.AtRisk, forecast: new DateOnly(2026, 10, 31), promised: new DateOnly(2026, 10, 24), slack: -7),
            P("E4", "InProgress", 1, DeliveryRisk.Late, delay: 3, promised: new DateOnly(2026, 10, 13)),
            P("E5", "Delivered", 1, delivered: new DateOnly(2026, 10, 2), forecast: new DateOnly(2026, 10, 2)),
            P("E6", "Delivered", 1, delivered: new DateOnly(2026, 9, 30)),
        };
        var d = OwnerRules.Deliveries(list, Today);
        (d.Forecast, d.Delivered, d.AtRisk, d.Late).Should().Be((2, 1, 2, 1), "previstas no mês: E1 e E3; o entregue E5 não conta como previsto");
        d.MostDelayed!.Number.Should().Be("E2");
        d.MostDelayed.DelayDays.Should().Be(15);
    }

    [Fact]
    public void Faixa_Atencao_poe_o_projeto_depois_do_caixa_e_antes_das_decisoes_com_no_maximo_3()
    {
        var cash = new CashForecastCard(true, 1, [], [], null, null, 0, new CashPointItem(Today.AddDays(10), -100m), At);
        var decisions = new DecisionsCard(true, 1, 10m, null, [new DecisionItem("x", "OP", null, 10m, null, "/app/x")], At);
        var overdue = new OverdueCard(Today, 10, 1, 1, [], [], 9, new OverdueLargestItem(Guid.CreateVersion7(), "CR-1", Guid.CreateVersion7(), "Cliente", 10m, 9, null), At);
        var project = Guid.CreateVersion7();
        var deliveries = new DeliveriesCard(2026, 10, 0, 0, 1, 0, new DelayedProjectItem(project, "PRJ-1", "Ed. Monte Castelo", new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 10), 7), At);

        var alerts = DashboardRules.Attention(Today, cash, decisions, overdue, deliveries);
        alerts.Select(a => a.Code).Should().Equal("cash_negative", "project_late", "decisions");
        alerts[1].Should().BeEquivalentTo(new
        {
            Tone = "crit", Title = "Ed. Monte Castelo vai atrasar 7 dias", Detail = "Previsão 10/11, prometido ao cliente 03/11.", Url = $"/app/projetos/{project}?aba=cronograma",
        });
        DashboardRules.Attention(Today, null, null, overdue, deliveries).Select(a => a.Code).Should().Equal("project_late", "overdue");
        DashboardRules.Attention(Today, null, null, null, deliveries with { MostDelayed = null }).Should().BeEmpty();
    }

    [Fact]
    public void Periodo_padrao_sao_os_12_meses_ate_o_mes_corrente_e_o_maximo_24()
    {
        var p = ProjectResultRules.Period(null, null, Today);
        (p.From, p.To, p.Months.Count, p.Error).Should().Be((new DateOnly(2025, 11, 1), new DateOnly(2026, 10, 31), 12, (string?)null));
        ProjectResultRules.Period("2026-08", "2026-10", Today).Months.Should().Equal(new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1));
        ProjectResultRules.Period("2026-11", "2026-10", Today).Error.Should().NotBeNull();
        ProjectResultRules.Period("2024-01", "2026-01", Today).Error.Should().Be("Escolha no máximo 24 meses.");
        ProjectResultRules.Period("2026-1", null, Today).Error.Should().Be("Informe o mês no formato aaaa-mm.");
    }

    [Fact]
    public void Montagem_do_resultado_por_projeto_e_da_visao_mes_a_mes()
    {
        var (a, b) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        var revenue = new[] { new ProjectMonthlyRevenue(a, 2026, 9, 10_000m, true), new ProjectMonthlyRevenue(a, 2026, 10, 2_500.50m, true), new ProjectMonthlyRevenue(b, 2026, 9, 0m, true) };
        var costs = new[]
        {
            new ProjectRealizedByPeriod(a, 2026, 9, CostType.Material, 4_000.25m), new ProjectRealizedByPeriod(a, 2026, 10, CostType.Tax, 250.05m),
            new ProjectRealizedByPeriod(a, 2026, 10, CostType.Material, -100m), new ProjectRealizedByPeriod(b, 2026, 10, CostType.Labor, 300m),
        };
        var figures = ProjectResultRules.ByProject([a, b], revenue, costs);
        (figures[a].Revenue, figures[a].DirectCosts, figures[a].Result, figures[a].MarginPct).Should().Be((12_500.50m, 4_150.30m, 8_350.20m, 66.8m));
        (figures[b].Result, figures[b].MarginPct, figures[b].IsEmpty).Should().Be((-300m, (decimal?)null, false), "receita 0: margem —");
        ProjectResultRules.Totals([figures[a], figures[b]]).Should().BeEquivalentTo(new { Projects = 2, Revenue = 12_500.50m, DirectCosts = 4_450.30m, Result = 8_050.20m, MarginPct = 64.4m });
        ProjectResultRules.Sort([(Summary(a, "A"), figures[a]), (Summary(b, "B"), figures[b])], "result").Select(r => r.Project.Number).Should().Equal("B", "A");

        var lines = ProjectResultRules.Lines([new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)], revenue.Where(r => r.ProjectId == a), costs.Where(c => c.ProjectId == a));
        lines.Select(l => l.Key).Should().Equal("revenue", "Material", "Labor", "Installation", "Freight", "Tax", "Other", "costs", "result");
        lines.Single(l => l.Key == "Material").Values.Should().Equal(4_000.25m, -100m);
        lines.Single(l => l.Key == "costs").Values.Should().Equal(4_000.25m, 150.05m);
        var result = lines.Single(l => l.Key == "result");
        (result.Values[0], result.Values[1], result.Total, result.Drillable).Should().Be((5_999.75m, 2_350.45m, 8_350.20m, false));
        lines.Single(l => l.Key == "revenue").Drillable.Should().BeTrue();
    }

    private static ProjectSummary Summary(Guid id, string number) => new(id, number, number, Guid.CreateVersion7(), Guid.CreateVersion7(), "InProgress", null);
}
