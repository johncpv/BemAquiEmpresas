using System.Globalization;
using System.Net;
using System.Text.Json;
using BemAqui.Commercial.Domain;
using BemAqui.Dashboard.Application;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Dashboard;

/// <summary>DSH-01b CA 1 a 5: cada card igual à origem, a faixa "Atenção" e a geração do cache pelos eventos novos.</summary>
public partial class OwnerPanelTests
{
    private static string Day(DateOnly d) => d.ToString("dd/MM", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Card_Margem_e_igual_ao_relatorio_Orcado_x_realizado_com_o_filtro_correspondente()
    {
        await SeedAsync(
            new Seed("Margem A1", ProjectStatus.InProgress, 100_000m, Budget: new() { [CostType.Material] = 30_000m, [CostType.Labor] = 10_000m },
                Ledger: [(CostType.Material, ProjectCostSource.StockIssue, 36_000m, Today.AddDays(-5))]),
            new Seed("Margem A2", ProjectStatus.Planned, 50_000m, Budget: new() { [CostType.Material] = 20_000m }),
            new Seed("Margem F1", ProjectStatus.Delivered, 80_000m, ContractStatus.Completed, DeliveredOn: new DateOnly(Today.Year, 1, 15),
                Budget: new() { [CostType.Material] = 40_000m }, Ledger: [(CostType.Material, ProjectCostSource.StockIssue, 45_000.50m, new DateOnly(Today.Year, 1, 10))]));
        var owner = await OwnerAsync();

        var card = await OkAsync(owner, Base + "/cards/margin");
        var active = (await OkAsync(owner, "/api/v1/projects/margins/portfolio?situation=andamento")).Clone();
        var f = active.GetProperty("figures");
        card.GetProperty("activeCount").GetInt32().Should().Be(active.GetProperty("projectCount").GetInt32()).And.BeGreaterThanOrEqualTo(2);
        card.GetProperty("activeRevenue").GetDecimal().Should().Be(f.GetProperty("revenue").GetDecimal());
        Dec(card, "projectedMarginPct").Should().Be(Dec(f, "projectedMarginPct"), "margem projetada (com custos reais) = a do relatório em andamento");
        Dec(card, "budgetedMarginPct").Should().Be(Dec(f, "budgetedMarginPct"));
        Dec(card, "deviationPp").Should().Be(Dec(f, "marginDeviationPp"));

        var year = Today.Year;
        var finished = await OkAsync(owner, $"/api/v1/projects/margins/portfolio?situation=concluido&deliveredFrom={year}-01-01&deliveredTo={year}-12-31");
        card.GetProperty("finishedCount").GetInt32().Should().Be(finished.GetProperty("projectCount").GetInt32()).And.BeGreaterThanOrEqualTo(1);
        Dec(card, "finishedRealizedMarginPct").Should().Be(Dec(finished.GetProperty("figures"), "realizedMarginPct"), "encerrados no ano com a margem realizada");

        var ranking = await OkAsync(owner, "/api/v1/projects/margins/ranking?situation=andamento&pageSize=5");
        card.GetProperty("worst").EnumerateArray().Select(w => w.GetProperty("projectId").GetGuid())
            .Should().Equal(ranking.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("project").GetProperty("id").GetGuid()), "os 5 piores na ordem do relatório");

        // F1 sozinho: (80.000 − 45.000,50) ÷ 80.000 = 43,7494% realizada.
        var only = await OkAsync(owner, $"/api/v1/projects/margins/portfolio?situation=concluido&deliveredFrom={year}-01-15&deliveredTo={year}-01-15");
        Dec(only.GetProperty("figures"), "realizedMarginPct").Should().Be(43.7494m);
    }

    [Fact]
    public async Task Carteira_a_produzir_e_a_faturar_batem_com_a_lista_de_projetos_de_origem()
    {
        var before = await OkAsync(await OwnerAsync(), Base + "/cards/backlog");
        var (party, ids) = await SeedAsync(
            new Seed("Carteira B1", ProjectStatus.Planned, 100_000m),
            new Seed("Carteira B2", ProjectStatus.InProgress, 50_000m, Amendment: 10_000m),
            new Seed("Carteira B3", ProjectStatus.Fabricated, 30_000m),
            new Seed("Carteira B4", ProjectStatus.Delivered, 70_000m, ContractStatus.Completed, DeliveredOn: Today.AddDays(-40)),
            new Seed("Carteira B5", ProjectStatus.Cancelled, 999m, ContractStatus.Cancelled));
        await ReceivableAsync(party, ids["Carteira B1"], 20_000m, Today.AddDays(-10));
        await ReceivableAsync(party, ids["Carteira B3"], 30_000m, Today.AddDays(-5));
        await ReceivableAsync(party, ids["Carteira B4"], 70_000m, Today.AddDays(-40));
        var owner = await OwnerAsync();
        var after = await OkAsync(owner, Base + "/cards/backlog");

        decimal Delta(string name) => after.GetProperty(name).GetDecimal() - before.GetProperty(name).GetDecimal();
        Delta("toProduce").Should().Be(160_000m, "B1 + B2 com o aditivo (Planned e InProgress)");
        (after.GetProperty("toProduceCount").GetInt32() - before.GetProperty("toProduceCount").GetInt32()).Should().Be(2);
        Delta("contracted").Should().Be(190_000m, "contratos ativos: B1, B2 e B3");
        Delta("recognized").Should().Be(50_000m, "títulos emitidos de B1 e B3; B4 tem contrato concluído");
        Delta("toInvoice").Should().Be(140_000m);
        after.GetProperty("provisional").GetBoolean().Should().BeTrue("antes das notas reais, pelos títulos emitidos");

        // A lista de projetos de origem (COM-05) do cliente soma o mesmo "a produzir".
        var list = await OkAsync(owner, $"/api/v1/projects?partyId={party}&pageSize=50");
        list.GetProperty("items").EnumerateArray().Where(p => p.GetProperty("status").GetString() is "Planned" or "InProgress")
            .Sum(p => p.GetProperty("contractValue").GetDecimal()).Should().Be(160_000m);

        // Sem finance.view: só o "a produzir".
        var noFinance = await OkAsync(await ActorWithAsync("dashboard.view", "projects.view"), Base + "/cards/backlog");
        noFinance.GetProperty("toProduce").GetDecimal().Should().Be(after.GetProperty("toProduce").GetDecimal());
        noFinance.GetProperty("toInvoice").ValueKind.Should().Be(JsonValueKind.Null);
        noFinance.GetProperty("recognized").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Projetos_em_andamento_entregas_e_a_faixa_Atencao_com_o_projeto_que_vai_atrasar()
    {
        var owner0 = await OwnerAsync();
        var before = await OkAsync(owner0, Base + "/cards/deliveries");
        var monthStart = new DateOnly(Today.Year, Today.Month, 1);
        var (party, ids) = await SeedAsync(
            new Seed("Atrasado L1", ProjectStatus.InProgress, 50_000m, Promised: Today.AddDays(-400)),
            new Seed("Em risco R1", ProjectStatus.InProgress, 80_000m, Promised: Today.AddDays(10), Forecast: Today.AddDays(330)),
            new Seed("No prazo N1", ProjectStatus.Planned, 40_000m, Promised: Today.AddDays(100), Forecast: Today.AddDays(90)),
            new Seed("No mês F1", ProjectStatus.InProgress, 20_000m, Promised: Today.AddDays(30), Forecast: monthStart),
            new Seed("Entregue D1", ProjectStatus.Delivered, 10_000m, ContractStatus.Completed, DeliveredOn: monthStart));
        await ReceivableAsync(party, ids["Atrasado L1"], 10_000m, Today.AddDays(-20), paid: 4_000m, status: ReceivableStatus.PartiallyPaid);
        var owner = await OwnerAsync();

        // Tabela (CA 1): o atrasado e depois o em risco no topo; contratado e etapa; recebido e margem em rotas próprias.
        var active = await OkAsync(owner, Base + "/cards/active-projects");
        var items = active.GetProperty("items").EnumerateArray().ToList();
        items.Should().HaveCountLessThanOrEqualTo(OwnerRules.ActiveRows);
        items[0].GetProperty("id").GetGuid().Should().Be(ids["Atrasado L1"]);
        items[0].GetProperty("risk").GetString().Should().Be("Late");
        items[0].GetProperty("delayDays").GetInt32().Should().Be(400);
        items[1].GetProperty("id").GetGuid().Should().Be(ids["Em risco R1"]);
        items[1].GetProperty("slackDays").GetInt32().Should().Be(-320);
        items[1].GetProperty("contractedAmount").GetDecimal().Should().Be(80_000m);
        items[0].EnumerateObject().Select(p => p.Name).Should().NotContain(n => n.Contains("margin", StringComparison.OrdinalIgnoreCase) || n.Contains("cost", StringComparison.OrdinalIgnoreCase));

        var received = await OkAsync(owner, Base + "/cards/active-projects/received");
        var l1 = received.EnumerateArray().Single(r => r.GetProperty("projectId").GetGuid() == ids["Atrasado L1"]);
        (l1.GetProperty("received").GetDecimal(), l1.GetProperty("receivedPct").GetDecimal()).Should().Be((4_000m, 8.0m));

        var margins = await OkAsync(owner, Base + "/cards/active-projects/margins");
        foreach (var row in margins.EnumerateArray())
        {
            var costs = await OkAsync(owner, $"/api/v1/projects/{row.GetProperty("projectId").GetGuid()}/costs");
            (Dec(row, "budgetedMarginPct"), Dec(row, "projectedMarginPct")).Should().Be((Dec(costs, "budgetedMarginPct"), Dec(costs, "projectedMarginPct")), "a mesma margem da aba Custos");
        }

        margins.GetArrayLength().Should().Be(items.Count);

        // Entregas (CA 1): previstas no mês, entregues no mês, em risco e atrasadas.
        var after = await OkAsync(owner, Base + "/cards/deliveries");
        int Delta(string name) => after.GetProperty(name).GetInt32() - before.GetProperty(name).GetInt32();
        Delta("forecastInMonth").Should().Be(1, "F1");
        Delta("deliveredInMonth").Should().Be(1, "D1");
        Delta("atRisk").Should().Be(1, "R1");
        Delta("late").Should().Be(1, "L1");
        var worst = after.GetProperty("mostDelayed");
        worst.GetProperty("projectId").GetGuid().Should().Be(ids["Em risco R1"]);
        worst.GetProperty("delayDays").GetInt32().Should().Be(320);

        // Faixa "Atenção" (CA 4): o projeto que vai atrasar, crítico, com o link do cronograma, logo depois do caixa.
        var overview = await OkAsync(owner, Base);
        var alerts = overview.GetProperty("attention").EnumerateArray().ToList();
        alerts.Should().HaveCountLessThanOrEqualTo(3);
        var late = alerts.Single(a => a.GetProperty("code").GetString() == "project_late");
        late.GetProperty("tone").GetString().Should().Be("crit");
        late.GetProperty("title").GetString().Should().EndWith("vai atrasar 320 dias").And.StartWith("Em risco R1");
        late.GetProperty("detail").GetString().Should().Be($"Previsão {Day(Today.AddDays(330))}, prometido ao cliente {Day(Today.AddDays(10))}.");
        late.GetProperty("url").GetString().Should().Be($"/app/projetos/{ids["Em risco R1"]}?aba=cronograma");
        var order = new[] { "cash_negative", "project_late", "decisions", "overdue" };
        alerts.Select(a => Array.IndexOf(order, a.GetProperty("code").GetString())).Should().BeInAscendingOrder("caixa → projeto → decisões → maior vencido");
        overview.GetProperty("cards").EnumerateArray().Select(c => c.GetString())
            .Should().ContainInOrder("margin", "active-projects", "production", "backlog", "deliveries");
    }

    [Fact]
    public async Task Card_Producao_e_igual_ao_Kanban_e_a_ocupacao()
    {
        var owner = await OwnerAsync();
        var card = await OkAsync(owner, Base + "/cards/production");
        var board = await OkAsync(owner, "/api/v1/production/board");
        var load = await OkAsync(owner, "/api/v1/production/planning/load?weeks=5");
        card.GetProperty("lateOrders").GetInt32().Should().Be(board.GetProperty("lateCount").GetInt32());
        card.GetProperty("atRiskOrders").GetInt32().Should().Be(board.GetProperty("atRiskCount").GetInt32());
        var weeks = card.GetProperty("weeks").EnumerateArray().ToList();
        weeks.Should().HaveCount(5, "a semana atual e as 4 seguintes");
        var cells = load.GetProperty("load").GetProperty("factory").GetProperty("cells").EnumerateArray().ToList();
        weeks.Select(w => (w.GetProperty("start").GetString(), Dec(w, "percent"), w.GetProperty("committedHours").GetDecimal()))
            .Should().Equal(cells.Select(c => (c.GetProperty("weekStart").GetString(), Dec(c, "percent"), c.GetProperty("committedHours").GetDecimal())));
    }

    [Fact]
    public async Task Eventos_de_previsao_e_de_custo_do_projeto_sobem_a_geracao_do_cache()
    {
        var cache = tenancy.Factory.Services.GetRequiredService<DashboardCache>();
        await using var scope = await ScopeAsync();
        var consumer = ActivatorUtilities.CreateInstance<DashboardGenerationConsumer>(scope.ServiceProvider);
        var start = cache.Generation(tenancy.TenantA.Id);
        var project = Guid.CreateVersion7();
        await consumer.HandleAsync(new BemAqui.Production.Contracts.ProjectForecastChanged(project, Today, Today, null, DateTimeOffset.UtcNow), default);
        await consumer.HandleAsync(new BemAqui.Inventory.Contracts.StockIssued(Guid.CreateVersion7(), "ProductionOrder", Guid.CreateVersion7(), 1, project, null, Guid.CreateVersion7(), 1, 0, 10, DateTimeOffset.UtcNow), default);
        await consumer.HandleAsync(new BemAqui.Inventory.Contracts.StockReturned(Guid.CreateVersion7(), "ProductionOrder", Guid.CreateVersion7(), 1, project, null, Guid.CreateVersion7(), 1, 0, 10, DateTimeOffset.UtcNow), default);
        await consumer.HandleAsync(new BemAqui.Production.Contracts.LaborEntryClosed(Guid.CreateVersion7(), Guid.CreateVersion7(), project, Guid.CreateVersion7(), Guid.CreateVersion7(), 60), default);
        await consumer.HandleAsync(new BemAqui.Production.Contracts.LaborEntryCorrected(Guid.CreateVersion7()), default);
        cache.Generation(tenancy.TenantA.Id).Should().Be(start + 5, "cada evento do CA 5 sobe o contador do tenant");

        // Sem evento, vale o cache: o mesmo usuário lê o mesmo card (mesma hora de cálculo) dentro dos 60 s.
        var owner = await OwnerAsync();
        var first = (await OkAsync(owner, Base + "/cards/backlog")).GetProperty("updatedAt").GetString();
        (await OkAsync(owner, Base + "/cards/backlog")).GetProperty("updatedAt").GetString().Should().Be(first);
        await consumer.HandleAsync(new BemAqui.Production.Contracts.ProjectForecastChanged(project, Today, Today, null, DateTimeOffset.UtcNow), default);
        (await OkAsync(owner, Base + "/cards/backlog")).GetProperty("updatedAt").GetString().Should().NotBe(first, "o evento invalida o painel do tenant");
    }
}
