using System.Globalization;
using System.Net;
using System.Text.Json;
using BemAqui.Production.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// PLN-03 com o banco real e projetos do Commercial: a visão por projeto na ordem da fila com as OS e as etapas do plano, prometido,
/// previsto, folga e risco (CA 1); concluída com as datas reais e em andamento com a parte feita (CA 2); "Define a entrega" no projeto em
/// risco (CA 3); a OS de instalação só de leitura com as datas digitadas (CA 4); os filtros (CA 9); a rota do projeto para o Projeto 360,
/// com e sem <c>production.view</c> e <c>projects.view</c> (CA 11); as três camadas, o IDOR, a allowlist e nenhum custo na resposta (CA 14).
/// A fila e o plano são do tenant inteiro: cada teste usa projetos próprios, com datas prometidas bem no futuro, e filtra pelos ids que criou.
/// </summary>
public partial class FabricationTests
{
    private const string Gantt = "/api/v1/production/planning/gantt";

    private static DateOnly DateOf(JsonElement value) => DateOnly.Parse(value.GetString()!, CultureInfo.InvariantCulture);

    private static async Task<JsonElement> GanttAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    private static JsonElement? GanttRow(JsonElement view, Guid project) =>
        view.GetProperty("projects").EnumerateArray().Where(p => p.GetProperty("projectId").GetGuid() == project).Select(p => (JsonElement?)p).FirstOrDefault();

    /// <summary>Um projeto com uma OS do roteiro P-C-M-Q, o prometido bem no futuro e o plano calculado.</summary>
    private async Task<(HttpClient Owner, Guid Project, Guid Order, string Line)> GanttProjectAsync(int promisedInDays = 3000)
    {
        var (owner, _, project, orders, line) = await KanbanDatasetAsync(1);
        await SetPromisedAsync(project, Today.AddDays(promisedInDays));
        await RecalcNowAsync();
        await DrainAsync();
        return (owner, project, orders[0], line);
    }

    [Fact]
    public async Task Por_projeto_mostra_a_fila_com_as_OS_as_etapas_reais_e_previstas()
    {
        var (owner, project, order, _) = await GanttProjectAsync();
        var steps = await WithDbAsync(db => db.ProductionOrderSteps.AsNoTracking().Where(s => s.OrderId == order).OrderBy(s => s.Sequence).ToListAsync());

        // CA 2: a primeira etapa concluída (datas reais) e a segunda em andamento com metade das horas restantes no plano.
        var started = DateTimeOffset.UtcNow.AddDays(-3);
        var finished = DateTimeOffset.UtcNow.AddDays(-2);
        await WithDbAsync(db => db.Database.ExecuteSqlAsync(
            $"UPDATE production.production_order_steps SET status = 'Done', actual_start = {started}, actual_end = {finished}, completed_at = {finished} WHERE id = {steps[0].Id}"));
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.production_order_steps SET status = 'InProgress', actual_start = {finished} WHERE id = {steps[1].Id}"));
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.schedule_step_forecasts SET hours = 0.75 WHERE step_id = {steps[1].Id}"));

        var view = await GanttAsync(owner, $"{Gantt}/projects");
        var row = GanttRow(view, project) ?? throw new InvalidOperationException("CA 1: o projeto da fila aparece");
        DateOf(view.GetProperty("from")).Should().Be(Today.AddDays(-7), "CA 6: 7 dias antes de hoje");
        DateOf(view.GetProperty("to")).Should().Be(Today.AddDays(84), "CA 6: 12 semanas depois");
        view.GetProperty("planComputedAt").ValueKind.Should().Be(JsonValueKind.String);
        row.GetProperty("projectNumber").GetString().Should().StartWith("PRJ-");
        row.GetProperty("partyName").GetString().Should().StartWith("Cliente ");
        DateOf(row.GetProperty("promisedDate")).Should().Be(Today.AddDays(3000));
        row.GetProperty("forecastDate").ValueKind.Should().Be(JsonValueKind.String, "a previsão publicada (PLN-01b)");
        row.GetProperty("slackDays").GetInt32().Should().BeGreaterThan(0);
        row.GetProperty("risk").GetString().Should().Be("None");

        // CA 1: a ordem é a da fila (PLN-02), com as posições contíguas.
        var queue = QueueOrder(await PlanningQueueAsync(owner));
        var ids = view.GetProperty("projects").EnumerateArray().Select(p => p.GetProperty("projectId").GetGuid()).ToList();
        ids.Should().Equal(queue.Where(ids.Contains), "a ordem da fila");
        row.GetProperty("position").GetInt32().Should().Be(queue.IndexOf(project) + 1);

        var os = row.GetProperty("orders").EnumerateArray().Single();
        os.GetProperty("orderId").GetGuid().Should().Be(order);
        os.GetProperty("kind").GetString().Should().Be("Fabrication");
        var bars = os.GetProperty("steps").EnumerateArray().ToList();
        bars.Select(s => s.GetProperty("code").GetString()).Should().Equal("P", "C", "M", "Q");
        bars[0].GetProperty("state").GetString().Should().Be("Done");
        DateOf(bars[0].GetProperty("actualStart")).Should().Be(DateOnly.FromDateTime(BemAqui.BuildingBlocks.Time.BusinessTime.ToBusiness(started).DateTime));
        DateOf(bars[0].GetProperty("actualEnd")).Should().Be(DateOnly.FromDateTime(BemAqui.BuildingBlocks.Time.BusinessTime.ToBusiness(finished).DateTime));
        bars[0].GetProperty("plannedStart").ValueKind.Should().Be(JsonValueKind.Null, "a concluída não tem previsão");
        bars[1].GetProperty("state").GetString().Should().Be("InProgress");
        bars[1].GetProperty("progress").GetDecimal().Should().Be(0.5m, "CA 2: (1,5 − 0,75) ÷ 1,5");
        (bars[1].GetProperty("remainingHours").GetDecimal(), bars[1].GetProperty("standardHours").GetDecimal()).Should().Be((0.75m, 1.5m));
        bars.Skip(2).Should().OnlyContain(s => s.GetProperty("state").GetString() == "Planned" && s.GetProperty("plannedEnd").ValueKind == JsonValueKind.String);
        bars.Should().OnlyContain(s => !s.GetProperty("drivesDelivery").GetBoolean(), "sem risco, nada destacado");
        bars.Should().OnlyContain(s => s.GetProperty("workCenterName").GetString()!.StartsWith("Montagem", StringComparison.Ordinal));
        DateOf(os.GetProperty("start")).Should().Be(DateOf(bars[0].GetProperty("actualStart")), "a OS começa no início real da primeira etapa");
        DateOf(row.GetProperty("start")).Should().Be(DateOf(os.GetProperty("start")));
    }

    [Fact]
    public async Task Projeto_em_risco_destaca_a_etapa_que_define_a_entrega_e_filtra_por_risco()
    {
        var (owner, project, _, line) = await GanttProjectAsync();
        var safe = await GanttAsync(owner, $"{Gantt}/projects?atRisk=true");
        GanttRow(safe, project).Should().BeNull("CA 9: \"Só em risco\" tira o projeto com folga");

        // Prometido para hoje: a previsão (fabricação + instalação) passa dele, sem tolerância.
        await SetPromisedAsync(project, Today);
        var view = await GanttAsync(owner, $"{Gantt}/projects?atRisk=true&projectId={project}");
        var row = GanttRow(view, project) ?? throw new InvalidOperationException("em risco aparece no filtro");
        row.GetProperty("risk").GetString().Should().Be("AtRisk");
        row.GetProperty("slackDays").GetInt32().Should().BeNegative();
        row.GetProperty("daysLate").GetInt32().Should().Be(-row.GetProperty("slackDays").GetInt32(), "dias depois do prometido");
        var steps = row.GetProperty("orders").EnumerateArray().Single().GetProperty("steps").EnumerateArray().ToList();
        steps.Count(s => s.GetProperty("drivesDelivery").GetBoolean()).Should().Be(1, "CA 3: só uma etapa define a entrega");
        var driver = steps.Single(s => s.GetProperty("drivesDelivery").GetBoolean());
        driver.GetProperty("code").GetString().Should().Be("Q", "a de maior fim previsto");
        DateOf(driver.GetProperty("plannedEnd")).Should().Be(steps.Max(s => DateOf(s.GetProperty("plannedEnd"))));

        // Atrasado: o prometido já passou.
        await SetPromisedAsync(project, Today.AddDays(-2));
        var late = GanttRow(await GanttAsync(owner, $"{Gantt}/projects?projectId={project}"), project)!.Value;
        late.GetProperty("risk").GetString().Should().Be("Late");

        // CA 9: projeto, linha e centro filtram; outro projeto some.
        (await GanttAsync(owner, $"{Gantt}/projects?line={Uri.EscapeDataString(line)}")).GetProperty("projects").EnumerateArray()
            .Select(p => p.GetProperty("projectId").GetGuid()).Should().Contain(project);
        GanttRow(await GanttAsync(owner, $"{Gantt}/projects?projectId={Guid.CreateVersion7()}"), project).Should().BeNull();
        GanttRow(await GanttAsync(owner, $"{Gantt}/projects?workCenterId={Guid.CreateVersion7()}"), project).Should().BeNull();
        var party = row.GetProperty("partyId").GetGuid();
        GanttRow(await GanttAsync(owner, $"{Gantt}/projects?partyId={party}"), project).Should().NotBeNull();
        var options = await GanttAsync(owner, $"{Gantt}/projects?projectId={Guid.CreateVersion7()}");
        options.GetProperty("projectOptions").EnumerateArray().Select(o => o.GetProperty("id").GetGuid()).Should().Contain(project, "as opções não encolhem com o filtro");
        options.GetProperty("lines").EnumerateArray().Select(l => l.GetString()).Should().Contain(line);
    }

    [Fact]
    public async Task OS_de_instalacao_aparece_so_de_leitura_com_as_datas_digitadas()
    {
        var (owner, project, order, _) = await GanttProjectAsync();
        var contract = await WithDbAsync(db => db.ProductionOrders.AsNoTracking().Where(o => o.Id == order).Select(o => o.ContractId).SingleAsync());
        var installation = new ProductionOrder
        {
            Number = "OSI-" + Guid.NewGuid().ToString("N")[..6], Type = ProductionOrderType.Installation, ProjectId = project, ContractId = contract, InstallerId = Guid.CreateVersion7(),
            ContractValue = 1500m, PlannedStart = Today.AddDays(40), PlannedEnd = Today.AddDays(45), CreatedAt = DateTimeOffset.UtcNow,
        };
        await WithDbAsync(async db =>
        {
            db.ProductionOrders.Add(installation);
            return await db.SaveChangesAsync();
        });

        var before = await WithDbAsync(db => db.ProductionOrders.AsNoTracking().Where(o => o.Id == installation.Id).Select(o => new { o.PlannedStart, o.PlannedEnd, o.Version }).SingleAsync());
        var row = GanttRow(await GanttAsync(owner, $"{Gantt}/projects?projectId={project}"), project)!.Value;
        var orders = row.GetProperty("orders").EnumerateArray().ToList();
        orders.Select(o => o.GetProperty("kind").GetString()).Should().Equal("Fabrication", "Installation");
        var bar = orders[1];
        bar.GetProperty("orderId").GetGuid().Should().Be(installation.Id);
        (DateOf(bar.GetProperty("start")), DateOf(bar.GetProperty("end"))).Should().Be((Today.AddDays(40), Today.AddDays(45)), "CA 4: as datas digitadas na OS");
        bar.GetProperty("steps").GetArrayLength().Should().Be(0);
        (await WithDbAsync(db => db.ProductionOrders.AsNoTracking().Where(o => o.Id == installation.Id).Select(o => new { o.PlannedStart, o.PlannedEnd, o.Version }).SingleAsync()))
            .Should().Be(before, "nada é editável no cronograma");
        var body = await (await owner.GetAsync($"{Gantt}/projects?projectId={project}")).Content.ReadAsStringAsync();
        body.Should().NotContainAny(["contractValue", "installerId", "1500"], "CA 14: sem valor nem instalador");
    }

    [Fact]
    public async Task Rota_do_projeto_exige_production_view_e_projects_view_e_valida_o_projeto()
    {
        var (owner, project, _, _) = await GanttProjectAsync();
        var view = await GanttAsync(owner, $"/api/v1/production/planning/projects/{project}/gantt");
        view.GetProperty("projects").GetArrayLength().Should().Be(1, "CA 11: só esse projeto");
        GanttRow(view, project)!.Value.GetProperty("position").GetInt32().Should().BeGreaterThan(0);
        // O período começa 7 dias antes do primeiro início e alarga até caber o projeto, no máximo 12 meses: o prometido em 3000 dias não
        // cabe, então o fim é o limite contado do início (GanttRules.ProjectPeriod).
        var start = DateOf(view.GetProperty("from"));
        start.Should().BeOnOrBefore(Today.AddDays(-GanttRules.DefaultDaysBefore), "7 dias antes de hoje ou do primeiro início");
        DateOf(view.GetProperty("to")).Should().Be(start.AddDays(GanttRules.MaxDays - 1), "o período cabe o projeto, até 12 meses");
        var period = await GanttAsync(owner, $"/api/v1/production/planning/projects/{project}/gantt?from={Iso(Today)}&to={Iso(Today.AddDays(30))}");
        (DateOf(period.GetProperty("from")), DateOf(period.GetProperty("to"))).Should().Be((Today, Today.AddDays(30)));

        var (_, both) = await ActorAsync("production:view;projects:view");
        (await both.GetAsync($"/api/v1/production/planning/projects/{project}/gantt")).StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var seed in new[] { "production:view", "projects:view" })
        {
            var (_, client) = await ActorAsync(seed);
            var denied = await client.GetAsync($"/api/v1/production/planning/projects/{project}/gantt");
            denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, seed);
            Code(await JsonAsync(denied)).Should().Be("forbidden");
        }

        // CA 14: IDOR — projeto que não existe no tenant dá 404, sem vazar nada.
        var missing = await owner.GetAsync($"/api/v1/production/planning/projects/{Guid.CreateVersion7()}/gantt");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await JsonAsync(missing)).Should().Be("project_not_found");
    }

    [Fact]
    public async Task Tres_camadas_allowlist_e_sem_custo_no_cronograma()
    {
        var (owner, project, _, _) = await GanttProjectAsync();
        var (_, viewer) = await ActorAsync("production:view");
        var (_, nobody) = await ActorAsync(null);
        var (_, other) = await ActorAsync("inventory:view");

        var ok = await viewer.GetAsync($"{Gantt}/projects?projectId={project}");
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        (await ok.Content.ReadAsStringAsync()).Should().NotContainAny(["cost", "Cost", "amount", "price", "document"], "CA 14: sem custo nem dado pessoal");
        (await viewer.GetAsync($"{Gantt}/centers")).StatusCode.Should().Be(HttpStatusCode.OK);

        foreach (var invalid in new[]
                 {
                     $"{Gantt}/projects?from={Iso(Today)}&to={Iso(Today.AddDays(-1))}", $"{Gantt}/projects?from={Iso(Today)}&to={Iso(Today.AddDays(GanttRules.MaxDays))}",
                     $"{Gantt}/centers?from={Iso(Today)}&to={Iso(Today.AddDays(400))}", $"{Gantt}/projects?line=" + new string('x', 121), $"{Gantt}/centers?line=" + new string('x', 121),
                     $"/api/v1/production/planning/projects/{project}/gantt?from={Iso(Today)}&to={Iso(Today.AddDays(-5))}",
                 })
        {
            var response = await owner.GetAsync(invalid);
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, invalid);
            Code(await JsonAsync(response)).Should().StartWith("planning_gantt.");
        }

        (await owner.GetAsync($"{Gantt}/projects?projectId=abc")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        foreach (var client in new[] { nobody, other })
        {
            foreach (var url in new[] { $"{Gantt}/projects", $"{Gantt}/centers", $"/api/v1/production/planning/projects/{project}/gantt" })
            {
                var denied = await client.GetAsync(url);
                denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
                Code(await JsonAsync(denied)).Should().Be("forbidden");
            }
        }

        await SetProductionReleasedAsync(false);
        try
        {
            foreach (var url in new[] { $"{Gantt}/projects", $"{Gantt}/centers", $"/api/v1/production/planning/projects/{project}/gantt" })
            {
                var blocked = await viewer.GetAsync(url);
                blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
                Code(await JsonAsync(blocked)).Should().Be("module_not_released");
            }
        }
        finally
        {
            await SetProductionReleasedAsync(true);
        }
    }
}
