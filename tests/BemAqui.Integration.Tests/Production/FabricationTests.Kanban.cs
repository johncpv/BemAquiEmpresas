using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Production;

public abstract partial class FabricationTestBase
{
    protected const string Board = "/api/v1/production/board";

    protected static async Task<JsonElement> BoardAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync(Board + "?" + query);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    protected static JsonElement Card(JsonElement board, Guid orderId) =>
        board.GetProperty("cards").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == orderId);

    protected static int ColumnCount(JsonElement board, string key) =>
        board.GetProperty("columns").EnumerateArray().Single(c => c.GetProperty("key").GetString() == key).GetProperty("count").GetInt32();

    /// <summary>Roteiro P-C-M-Q (Q de qualidade) num centro com linha, e um projeto com as OS do roteiro.</summary>
    protected async Task<(HttpClient Owner, Guid Routing, Guid Project, List<Guid> Orders, string Line)> KanbanDatasetAsync(int orders = 3)
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var line = "Linha " + Guid.NewGuid().ToString("N")[..4];
        var center = await CenterAsync("Montagem", line);
        var routing = await RoutingAsync(owner, center, null, null,
            Step("Projeto", "P", center, 2m), Step("Corte", "C", center, 1.5m), Step("Montagem", "M", center, 3m), Step("Qualidade", "Q", center, 0.5m, quality: true));
        var product = await ProductAsync();
        var (project, _, items) = await ApprovedAsync(owner, Enumerable.Range(0, orders).Select(_ => ("Equipment", (Guid?)product, 1m)).ToArray(), routingOnItems: routing);
        var ids = new List<Guid>();
        foreach (var item in items)
        {
            ids.Add((await OrderOfAsync(item))!.Id);
        }

        return (owner, routing, project, ids, line);
    }

    protected Task<int> SetPromisedAsync(Guid projectId, DateOnly? promised) =>
        WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE commercial.projects SET promised_date = {promised} WHERE id = {projectId}"));

    /// <summary>ProductionStepStarted da OS no outbox (o payload é jsonb: filtra pelo campo, não por LIKE).</summary>
    protected Task<int> StepStartedCountAsync(Guid orderId) =>
        WithDbAsync(db => db.Database.SqlQuery<int>(
            $"SELECT count(*)::int AS \"Value\" FROM shared.outbox_messages WHERE type = {typeof(ProductionStepStarted).FullName} AND payload ->> 'ProductionOrderId' = {orderId.ToString()}").SingleAsync());

    protected async Task RunLateJobAsync()
    {
        var job = ActivatorUtilities.CreateInstance<LateOrdersNoticeJob>(tenancy.Factory.Services);
        await job.ExecuteAsync(tenancy.TenantA.Id, new LateOrdersArgs(), CancellationToken.None);
    }

    protected async Task SetProductionReleasedAsync(bool released)
    {
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules
                .Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == "production").ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
        Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }
}

/// <summary>
/// PRD-02, Kanban: colunas pelo código da etapa, cartões, contadores e filtros (CA 1 a 6), "Concluir etapa" (CA 8 e 9), permissões
/// e módulo (CA 15), job "OS atrasada" (CA 11) e desempenho com 300 OS (CA 14). Dados no banco desta classe; cada teste filtra
/// pelo roteiro próprio.
/// </summary>
public partial class FabricationBoardTests
{
    [Fact]
    public async Task Quadro_tem_colunas_pelo_codigo_e_o_cartao_na_etapa_atual()
    {
        var (owner, routing, project, orders, line) = await KanbanDatasetAsync();
        await MarkStepAsync(orders[1], 1, ProductionStepStatus.Done);
        await MarkStepAsync(orders[1], 2, ProductionStepStatus.Ready);
        for (var s = 1; s <= 4; s++)
        {
            await MarkStepAsync(orders[2], s, ProductionStepStatus.Done);
        }

        var board = await BoardAsync(owner, $"routingTemplateId={routing}");

        board.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("key").GetString()).Should().Equal("P", "C", "M", "Q", "pronta");
        var p = board.GetProperty("columns")[0];
        p.GetProperty("name").GetString().Should().Be("Projeto");
        p.GetProperty("count").GetInt32().Should().Be(1);
        p.GetProperty("remainingHours").GetDecimal().Should().Be(2m, "Σ max(padrão − apontadas, 0) das etapas atuais; apontadas = 0 até a PRD-03a");
        ColumnCount(board, "C").Should().Be(1);
        ColumnCount(board, "pronta").Should().Be(1);
        Card(board, orders[2]).GetProperty("readyToComplete").GetBoolean().Should().BeTrue("todas as etapas Done ficam em Pronta para concluir (D12)");

        var card = Card(board, orders[0]);
        card.GetProperty("columnKey").GetString().Should().Be("P");
        card.GetProperty("projectId").GetGuid().Should().Be(project);
        card.GetProperty("partyName").GetString().Should().NotBeNullOrEmpty();
        card.GetProperty("productName").GetString().Should().Be("Elevador 6 paradas");
        card.GetProperty("standardHours").GetDecimal().Should().Be(2m);
        card.GetProperty("appliedHours").GetDecimal().Should().Be(0m);
        card.GetProperty("line").GetString().Should().Be(line);
        card.GetProperty("codes").EnumerateArray().Select(c => c.GetString()).Should().Equal("P", "C", "M", "Q");
        card.TryGetProperty("cost", out _).Should().BeFalse("o cartão não mostra custo");

        (await BoardAsync(owner, $"routingTemplateId={routing}&line={Uri.EscapeDataString(line)}")).GetProperty("cards").GetArrayLength().Should().Be(3);
        (await BoardAsync(owner, $"routingTemplateId={routing}&line=Outra")).GetProperty("cards").GetArrayLength().Should().Be(0);
        (await BoardAsync(owner, $"routingTemplateId={routing}&projectId={Guid.CreateVersion7()}")).GetProperty("cards").GetArrayLength().Should().Be(0);
        var all = await BoardAsync(owner, string.Empty);
        all.GetProperty("routings").EnumerateArray().Should().Contain(r => r.GetProperty("id").GetGuid() == routing);
        all.GetProperty("lines").EnumerateArray().Select(l => l.GetString()).Should().Contain(line);
    }

    [Fact]
    public async Task OS_sem_roteiro_tem_coluna_propria_e_cancelada_sai_do_quadro()
    {
        var (owner, routing, _, orders, _) = await KanbanDatasetAsync(2);
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.production_orders SET status = 'Cancelled' WHERE id = {orders[1]}"));

        var board = await BoardAsync(owner, $"routingTemplateId={routing}");

        board.GetProperty("cards").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).Should().Equal(orders[0]);

        // Sem roteiro ativo nenhum para o item: a OS nasce sem etapas e vai para "Sem roteiro".
        var noRouting = await WithDbAsync(async db =>
        {
            var order = await db.ProductionOrders.AsNoTracking().SingleAsync(o => o.Id == orders[0]);
            var copy = new ProductionOrder
            {
                Number = "OS-SR-" + Guid.NewGuid().ToString("N")[..6], Type = ProductionOrderType.Fabrication, ProjectId = order.ProjectId, ContractId = order.ContractId,
                ProjectItemId = Guid.CreateVersion7(), Quantity = 1m, CreatedAt = DateTimeOffset.UtcNow,
            };
            db.ProductionOrders.Add(copy);
            await db.SaveChangesAsync();
            return copy.Id;
        });
        var withNone = await BoardAsync(owner, string.Empty);
        Card(withNone, noRouting).GetProperty("columnKey").GetString().Should().Be("sem-roteiro");
        withNone.GetProperty("columns")[0].GetProperty("key").GetString().Should().Be("sem-roteiro");
    }

    [Fact]
    public async Task Atraso_e_risco_pela_mesma_regra_com_contadores_e_filtros()
    {
        var (owner, routing, project, orders, _) = await KanbanDatasetAsync(1);
        await SetPromisedAsync(project, Today.AddDays(-2));
        var late = await BoardAsync(owner, $"routingTemplateId={routing}");
        late.GetProperty("lateCount").GetInt32().Should().Be(1);
        Card(late, orders[0]).GetProperty("risk").GetString().Should().Be("Late");
        Card(late, orders[0]).GetProperty("delayDays").GetInt32().Should().Be(2);
        (await GetOrderAsync(owner, orders[0])).GetProperty("delayDays").GetInt32().Should().Be(2, "a lista e o detalhe da PRD-01 usam a mesma função");

        // PLN-01b CA 9: em risco = previsão de entrega do projeto (o plano) depois da prometida, sem tolerância.
        await SetPromisedAsync(project, Today.AddDays(3));
        await RecalcNowAsync();
        await SetForecastsAsync(project, Today.AddDays(4));
        var risk = await BoardAsync(owner, $"routingTemplateId={routing}");
        risk.GetProperty("atRiskCount").GetInt32().Should().Be(1);
        Card(risk, orders[0]).GetProperty("risk").GetString().Should().Be("AtRisk");
        (await BoardAsync(owner, $"routingTemplateId={routing}&late=true")).GetProperty("cards").GetArrayLength().Should().Be(0);
        (await BoardAsync(owner, $"routingTemplateId={routing}&atRisk=true")).GetProperty("cards").GetArrayLength().Should().Be(1);

        // Prometido em 3 dias com a previsão no prazo não é mais risco (a janela de 7 dias da Fase 3 saiu).
        await SetForecastsAsync(project, Today.AddDays(3));
        Card(await BoardAsync(owner, $"routingTemplateId={routing}"), orders[0]).GetProperty("risk").GetString().Should().Be("None");
    }

    [Fact]
    public async Task Concluir_etapa_avanca_a_OS_publica_o_evento_e_o_projeto_entra_em_producao()
    {
        var (owner, routing, project, orders, _) = await KanbanDatasetAsync(1);
        var (supervisorId, supervisor) = await ActorAsync("production:view,approve");
        var order = await GetOrderAsync(supervisor, orders[0]);
        var first = order.GetProperty("steps")[0].GetProperty("id").GetGuid();

        var done = await supervisor.PostAsJsonAsync($"{Base}/{orders[0]}/steps/{first}/complete", new { reason = "Projeto entregue por e-mail, sem apontamento.", version = Version(order) });

        done.StatusCode.Should().Be(HttpStatusCode.OK, await done.Content.ReadAsStringAsync());
        var detail = await JsonAsync(done);
        detail.GetProperty("status").GetString().Should().Be("InProgress");
        detail.GetProperty("steps")[0].GetProperty("status").GetString().Should().Be("Done");
        detail.GetProperty("steps")[1].GetProperty("status").GetString().Should().Be("Ready");
        var step = await WithDbAsync(db => db.ProductionOrderSteps.AsNoTracking().SingleAsync(s => s.Id == first));
        step.CompletedBySupervisor.Should().BeTrue();
        step.CompletionReason.Should().Be("Projeto entregue por e-mail, sem apontamento.");
        step.CompletedBy.Should().Be(supervisorId);
        (await WithDbAsync(db => db.ProductionOrders.AsNoTracking().SingleAsync(o => o.Id == orders[0]))).ActualStart.Should().NotBeNull();
        (await StepStartedCountAsync(orders[0])).Should().Be(1);

        await DrainAsync();
        (await WithDbAsync(db => db.Projects.AsNoTracking().SingleAsync(p => p.Id == project))).Status.ToString().Should().Be("InProgress");
        Card(await BoardAsync(owner, $"routingTemplateId={routing}"), orders[0]).GetProperty("columnKey").GetString().Should().Be("C");

        // Segunda etapa: a OS já está em andamento, nada vai para o outbox.
        order = await GetOrderAsync(supervisor, orders[0]);
        var second = order.GetProperty("steps")[1].GetProperty("id").GetGuid();
        (await supervisor.PostAsJsonAsync($"{Base}/{orders[0]}/steps/{second}/complete", new { reason = "Corte terceirizado.", version = Version(order) })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await StepStartedCountAsync(orders[0])).Should().Be(1);
    }

    [Fact]
    public async Task Concluir_etapa_recusa_qualidade_etapa_que_nao_e_a_atual_e_quem_nao_aprova()
    {
        var (_, _, _, orders, _) = await KanbanDatasetAsync(2);
        var (_, supervisor) = await ActorAsync("production:view,approve");
        var (_, editor) = await ActorAsync("production:view,edit");
        var order = await GetOrderAsync(supervisor, orders[0]);
        var steps = order.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("id").GetGuid()).ToList();
        var otherStep = (await GetOrderAsync(supervisor, orders[1])).GetProperty("steps")[0].GetProperty("id").GetGuid();
        object Body(JsonElement o, string reason = "Motivo") => new { reason, version = Version(o) };

        (await editor.PostAsJsonAsync($"{Base}/{orders[0]}/steps/{steps[0]}/complete", Body(order))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await supervisor.PostAsJsonAsync($"{Base}/{orders[0]}/steps/{steps[0]}/complete", Body(order, " "))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await supervisor.PostAsJsonAsync($"{Base}/{orders[0]}/steps/{steps[0]}/complete", Body(order, new string('x', 201)))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var notCurrent = await supervisor.PostAsJsonAsync($"{Base}/{orders[0]}/steps/{steps[1]}/complete", Body(order));
        notCurrent.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(notCurrent);
        Code(problem).Should().Be("step_not_current");
        problem.GetProperty("detail").GetString().Should().Be("Esta etapa não é a etapa atual da OS.");
        Code(await JsonAsync(await supervisor.PostAsJsonAsync($"{Base}/{orders[0]}/steps/{otherStep}/complete", Body(order)))).Should().Be("step_not_current", "etapa de outra OS");

        for (var s = 1; s <= 3; s++)
        {
            await MarkStepAsync(orders[0], s, ProductionStepStatus.Done);
        }

        order = await GetOrderAsync(supervisor, orders[0]);
        var quality = await supervisor.PostAsJsonAsync($"{Base}/{orders[0]}/steps/{steps[3]}/complete", Body(order));
        quality.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(quality)).Should().Be("quality_step", "a etapa de qualidade é decidida na PRD-05");

        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.production_orders SET status = 'Cancelled' WHERE id = {orders[1]}"));
        var cancelled = await GetOrderAsync(supervisor, orders[1]);
        Code(await JsonAsync(await supervisor.PostAsJsonAsync($"{Base}/{orders[1]}/steps/{otherStep}/complete", Body(cancelled))))
            .Should().Be("step_not_current", "OS cancelada não conclui etapa");
    }

    [Fact]
    public async Task Duas_conclusoes_juntas_so_uma_grava()
    {
        var (_, _, _, orders, _) = await KanbanDatasetAsync(1);
        var (_, supervisor) = await ActorAsync("production:view,approve");
        var order = await GetOrderAsync(supervisor, orders[0]);
        var first = order.GetProperty("steps")[0].GetProperty("id").GetGuid();
        var body = new { reason = "Concorrência", version = Version(order) };

        var results = await Task.WhenAll(
            supervisor.PostAsJsonAsync($"{Base}/{orders[0]}/steps/{first}/complete", body),
            supervisor.PostAsJsonAsync($"{Base}/{orders[0]}/steps/{first}/complete", body));

        results.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        (await StepStartedCountAsync(orders[0])).Should().Be(1);
    }

    [Fact]
    public async Task Quadro_e_concluir_etapa_respeitam_as_tres_camadas()
    {
        var (_, none) = await ActorAsync(null);
        var (_, viewer) = await ActorAsync("production:view");
        var board = await none.GetAsync(Board);
        board.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(board)).Should().Be("forbidden");
        (await viewer.GetAsync(Board)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.PostAsJsonAsync($"{Base}/{Guid.CreateVersion7()}/steps/{Guid.CreateVersion7()}/complete", new { reason = "x", version = 0 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await SetProductionReleasedAsync(false);
        try
        {
            var notReleased = await viewer.GetAsync(Board);
            notReleased.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(notReleased)).Should().Be("module_not_released");
        }
        finally
        {
            await SetProductionReleasedAsync(true);
        }
    }

    [Fact]
    public async Task Job_OS_atrasada_avisa_uma_vez_por_OS_e_prometido_e_confere_o_modulo()
    {
        var (_, _, project, orders, _) = await KanbanDatasetAsync(1);
        var (approver, _) = await ActorAsync("production:view,approve");
        var manager = await WithDbAsync(db => db.Projects.AsNoTracking().Where(p => p.Id == project).Select(p => p.ManagerUserId).SingleAsync());
        await SetPromisedAsync(project, Today.AddDays(-4));
        var number = await WithDbAsync(db => db.ProductionOrders.AsNoTracking().Where(o => o.Id == orders[0]).Select(o => o.Number).SingleAsync());

        await RunLateJobAsync();
        await RunLateJobAsync();

        var notices = await WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.Type == LateOrdersNotifier.Type && n.Link == FabricationOrderService.Link + orders[0]).ToListAsync());
        notices.Where(n => n.UserId == approver).Should().ContainSingle("rodar duas vezes no mesmo dia não duplica");
        notices.Single(n => n.UserId == approver).Title.Should().StartWith($"OS {number} de ").And.EndWith("está atrasada 4 d.");
        if (manager is { } m)
        {
            notices.Should().Contain(n => n.UserId == m, "o responsável do projeto também é avisado");
        }

        // Nova data prometida, nova chave: avisa de novo.
        await SetPromisedAsync(project, Today.AddDays(-1));
        await RunLateJobAsync();
        (await WithDbAsync(db => db.Notifications.CountAsync(n => n.Type == LateOrdersNotifier.Type && n.UserId == approver && n.Link == FabricationOrderService.Link + orders[0])))
            .Should().Be(2);

        await SetProductionReleasedAsync(false);
        try
        {
            await using var scope = await ScopeAsync();
            (await scope.ServiceProvider.GetRequiredService<LateOrdersNotifier>().NotifyAsync()).Should().Be(0, "com o módulo não liberado o job não avisa");
        }
        finally
        {
            await SetProductionReleasedAsync(true);
        }
    }

    [Fact]
    public async Task Quadro_com_300_OS_abertas_carrega_em_menos_de_1_s()
    {
        var (owner, routing, _, orders, _) = await KanbanDatasetAsync(1);
        await WithDbAsync(async db =>
        {
            var template = await db.ProductionOrders.AsNoTracking().SingleAsync(o => o.Id == orders[0]);
            var steps = await db.ProductionOrderSteps.AsNoTracking().Where(s => s.OrderId == orders[0]).OrderBy(s => s.Sequence).ToListAsync();
            for (var i = 0; i < 300; i++)
            {
                var order = new ProductionOrder
                {
                    Number = "OS-PERF-" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + Guid.NewGuid().ToString("N")[..4], Type = ProductionOrderType.Fabrication,
                    ProjectId = template.ProjectId, ContractId = template.ContractId, ProjectItemId = Guid.CreateVersion7(), ProductId = template.ProductId, Quantity = 1m,
                    RoutingTemplateId = routing, CreatedAt = DateTimeOffset.UtcNow,
                };
                db.ProductionOrders.Add(order);
                foreach (var s in steps)
                {
                    db.ProductionOrderSteps.Add(new ProductionOrderStep
                    {
                        OrderId = order.Id, Sequence = s.Sequence, Name = s.Name, Code = s.Code, WorkCenterId = s.WorkCenterId, StandardHours = s.StandardHours,
                        IsQualityStep = s.IsQualityStep, Status = s.Sequence == 1 ? ProductionStepStatus.Ready : ProductionStepStatus.Waiting,
                    });
                }
            }

            return await db.SaveChangesAsync();
        });
        await BoardAsync(owner, $"routingTemplateId={routing}"); // aquece

        var watch = Stopwatch.StartNew();
        var board = await BoardAsync(owner, $"routingTemplateId={routing}");
        watch.Stop();

        board.GetProperty("cards").GetArrayLength().Should().Be(301);
        watch.ElapsedMilliseconds.Should().BeLessThan(1000);
    }
}