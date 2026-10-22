using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Commercial.Application;
using BemAqui.Commercial.Contracts;
using BemAqui.Finance.Contracts;
using BemAqui.Identity.Domain;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.Production.Infrastructure;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// PRD-06a1, conclusão da OS e fabricação concluída do projeto: "Pronta para concluir" gravada e a migration (CA 1), diálogo e conclusão com o
/// evento na mesma transação (CA 2, 3 e 5), bloqueios (CA 4), a ação da qualidade recusada (CA 6), a cadeia até a parcela (CA 7), projeto já
/// fabricado à mão (CA 8), registro manual com e sem OS (CA 9), estorno com aviso e reconclusão (CA 10), estorno recusado (CA 11), consulta
/// (CA 12), três camadas e IDOR (CA 13), concorrência (CA 14) e histórico (CA 15). Cada teste filtra pelos ids que criou.
/// </summary>
public partial class FabricationTests
{
    private static readonly object[] FabricationMilestones =
    [
        new { description = "Sinal", percent = 60m, trigger = "Event", triggerEvent = "Signature", dueDate = (string?)null, daysAfterEvent = 0 },
        new { description = "Fabricação concluída", percent = 40m, trigger = "Event", triggerEvent = "FabricationDone", dueDate = (string?)null, daysAfterEvent = 10 },
    ];

    /// <summary>Projeto com {n} equipamentos (uma OS de fabricação cada, roteiro C → M sem qualidade) e a parcela "fabricação concluída".</summary>
    private async Task<(HttpClient Owner, Guid Project, Guid Contract, List<Guid> Orders)> CompletionDatasetAsync(int equipments = 1, object[]? milestones = null)
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var center = await CenterAsync("Montagem");
        var routing = await RoutingAsync(owner, center, null, null, Step("Corte", "C", center), Step("Montagem", "M", center));
        var lines = new List<(string, Guid?, decimal)>();
        for (var i = 0; i < equipments; i++)
        {
            lines.Add(("Equipment", await ProductAsync(), 1m));
        }

        var (project, contract, items) = await ApprovedAsync(owner, [.. lines], routingOnItems: routing, milestones: milestones ?? FabricationMilestones);
        var orders = new List<Guid>();
        foreach (var item in items)
        {
            orders.Add((await OrderOfAsync(item))!.Id);
        }

        return (owner, project, contract, orders);
    }

    /// <summary>O supervisor conclui as etapas que faltam pelo "Concluir etapa" (PRD-02), até a OS ficar pronta para concluir.</summary>
    private static async Task FinishStepsAsync(HttpClient supervisor, Guid orderId)
    {
        for (var round = 0; round < 10; round++)
        {
            var order = await GetOrderAsync(supervisor, orderId);
            var pending = order.GetProperty("steps").EnumerateArray().FirstOrDefault(s => s.GetProperty("status").GetString() != "Done");
            if (pending.ValueKind == JsonValueKind.Undefined)
            {
                return;
            }

            var done = await supervisor.PostAsJsonAsync($"{Base}/{orderId}/steps/{pending.GetProperty("id").GetGuid()}/complete", new { reason = "Feito fora do sistema.", version = Version(order) });
            done.StatusCode.Should().Be(HttpStatusCode.OK, await done.Content.ReadAsStringAsync());
        }
    }

    private static async Task<HttpResponseMessage> CompleteOrderAsync(HttpClient client, Guid orderId, uint version) =>
        await client.SendAsync(Post($"{Base}/{orderId}/complete", new { version }));

    private static async Task<HttpResponseMessage> ReverseOrderAsync(HttpClient client, Guid orderId, uint version, string? reason = "Medida errada na cabine") =>
        await client.SendAsync(Post($"{Base}/{orderId}/reverse-completion", new { reason, version }));

    private Task<List<BemAqui.Finance.Domain.Receivable>> FabricationTitlesAsync(Guid contract) =>
        WithDbAsync(db => db.Receivables.AsNoTracking().Where(r => r.ContractId == contract && r.TriggerEvent == TriggerEvent.FabricationDone).ToListAsync());

    private Task<BemAqui.Commercial.Domain.Project> ProjectAsync(Guid project) => WithDbAsync(db => db.Projects.AsNoTracking().SingleAsync(p => p.Id == project));

    /// <summary>Todos os textos de uma resposta JSON, já sem o escape de acentos, para conferir mensagens.</summary>
    private static async Task<string> TextsAsync(HttpClient client, string url)
    {
        var root = await OkAsync(await client.GetAsync(url));
        var texts = new List<string>();
        void Walk(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.String: texts.Add(e.GetString()!); break;
                case JsonValueKind.Array: foreach (var x in e.EnumerateArray()) { Walk(x); } break;
                case JsonValueKind.Object: foreach (var p in e.EnumerateObject()) { Walk(p.Value); } break;
                default: break;
            }
        }

        Walk(root);
        return string.Join('\n', texts);
    }

    [Fact]
    public async Task Ultima_etapa_grava_pronta_para_concluir_no_filtro_aguardando_voce_e_a_migration_ajusta_as_antigas()
    {
        var (owner, project, _, orders) = await CompletionDatasetAsync(2);
        await FinishStepsAsync(owner, orders[0]);

        (await OrderAsync(orders[0])).Status.Should().Be(ProductionOrderStatus.ReadyToComplete, "CA 1: estado gravado");
        var detail = await GetOrderAsync(owner, orders[0]);
        detail.GetProperty("status").GetString().Should().Be("ReadyToComplete");
        detail.GetProperty("completionBlocker").ValueKind.Should().Be(JsonValueKind.Null);
        var ready = await JsonAsync(await owner.GetAsync($"{Base}?situation=pronta&projectId={project}"));
        ready.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Equal(orders[0]);

        // OS antiga com todas as etapas concluídas e a situação calculada (antes desta story): a migration grava ReadyToComplete.
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.production_order_steps SET status = 'Done' WHERE order_id = {orders[1]}"));
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.production_orders SET status = 'InProgress' WHERE id = {orders[1]}"));
        await WithDbAsync(db => db.Database.ExecuteSqlRawAsync(ProductionDataFixes.ReadyToComplete));
        (await OrderAsync(orders[1])).Status.Should().Be(ProductionOrderStatus.ReadyToComplete);
    }

    [Fact]
    public async Task Concluir_a_ultima_OS_fabrica_o_projeto_e_libera_a_parcela_sem_estoque()
    {
        var (owner, project, contract, orders) = await CompletionDatasetAsync();
        var (supervisorId, supervisor) = await ActorAsync("production:view,approve;finance:view");
        await FinishStepsAsync(owner, orders[0]);

        var preview = await OkAsync(await supervisor.GetAsync($"{Base}/{orders[0]}/completion"));
        preview.GetProperty("canComplete").GetBoolean().Should().BeTrue();
        preview.GetProperty("lastFabricationOrder").GetBoolean().Should().BeTrue("CA 2");
        preview.GetProperty("stockNotice").GetString().Should().Be("O produto acabado ainda não entra no estoque nesta versão.");
        var milestone = preview.GetProperty("milestones").EnumerateArray().Single();
        milestone.GetProperty("description").GetString().Should().Be("Fabricação concluída");
        milestone.GetProperty("amount").GetDecimal().Should().BeGreaterThan(0, "com finance.view, o valor");
        milestone.GetProperty("dueDate").GetString().Should().Be(Iso(Today.AddDays(10)));
        var (_, blind) = await ActorAsync("production:view,approve");
        var hidden = (await OkAsync(await blind.GetAsync($"{Base}/{orders[0]}/completion"))).GetProperty("milestones")[0];
        hidden.GetProperty("amount").ValueKind.Should().Be(JsonValueKind.Null, "sem finance.view, só o marco");

        var done = await OkAsync(await CompleteOrderAsync(supervisor, orders[0], Version(preview)));
        done.GetProperty("status").GetString().Should().Be("Completed", "CA 3");
        var saved = await OrderAsync(orders[0]);
        saved.ActualEnd.Should().NotBeNull();
        saved.CompletedBy.Should().Be(supervisorId);
        saved.Quantity.Should().Be(1m, "CA 5: a quantidade inteira");
        (await OutboxAsync<ProductionOrderCompleted>(orders[0])).Should().Be(1, "CA 3: o evento na mesma transação");
        done.GetProperty("completedByName").GetString().Should().NotBeNullOrEmpty("CA 12: Concluída em {data} por {nome}");

        await DrainAsync();
        var fabricated = await ProjectAsync(project);
        fabricated.Status.ToString().Should().Be("Fabricated", "CA 7");
        fabricated.FabricatedByOrders.Should().BeTrue();
        fabricated.FabricatedOn.Should().Be(Today);
        (await OutboxAsync<ProjectFabricated>(project)).Should().Be(1);
        var title = (await FabricationTitlesAsync(contract)).Single();
        title.Status.Should().Be(ReceivableStatus.Open, "CA 7: a parcela é liberada sozinha");
        title.DueDate.Should().Be(Today.AddDays(10));

        var timeline = await TextsAsync(owner, $"/api/v1/projects/{project}/timeline");
        timeline.Should().Contain("Fabricação concluída pelas OS", "CA 12").And.Contain($"{done.GetProperty("number").GetString()} concluída");
        var manual = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{project}"));
        manual.GetProperty("fabricatedByOrders").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Projeto_so_fabrica_com_todas_as_OS_de_fabricacao_e_a_cancelada_e_a_instalacao_nao_contam()
    {
        var (owner, project, _, orders) = await CompletionDatasetAsync(3);
        await InstallationAsync(owner, project);
        (await owner.SendAsync(Post($"{Base}/{orders[2]}/cancel", new { reason = "Item trocado.", version = Version(await GetOrderAsync(owner, orders[2])) })))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await FinishStepsAsync(owner, orders[0]);
        await OkAsync(await CompleteOrderAsync(owner, orders[0], (await OrderAsync(orders[0])).Version));
        await DrainAsync();
        (await ProjectAsync(project)).Status.ToString().Should().NotBe("Fabricated", "falta uma OS de fabricação");

        await FinishStepsAsync(owner, orders[1]);
        var preview = await OkAsync(await owner.GetAsync($"{Base}/{orders[1]}/completion"));
        preview.GetProperty("lastFabricationOrder").GetBoolean().Should().BeTrue("a cancelada não conta");
        await OkAsync(await CompleteOrderAsync(owner, orders[1], Version(preview)));
        await DrainAsync();
        (await ProjectAsync(project)).Status.ToString().Should().Be("Fabricated", "CA 7: a OS de instalação não conta");
    }

    [Fact]
    public async Task Conclusao_recusa_OS_que_nao_esta_pronta_e_quem_so_decide_a_qualidade()
    {
        var (owner, center, order) = await QualityDatasetAsync();
        var (_, _, operatorClient, _) = await OperatorAsync(center);
        var first = await StepIdAsync(order, 1);
        await OkAsync(await operatorClient.SendAsync(Tap($"{Shopfloor}/steps/{first}/start")));

        var open = await CompleteOrderAsync(owner, order, await OrderVersionAsync(order));
        open.StatusCode.Should().Be(HttpStatusCode.Conflict, "CA 4");
        (await JsonAsync(open)).GetProperty("detail").GetString().Should().Be("Há apontamento aberto na etapa Corte.");
        (await GetOrderAsync(owner, order)).GetProperty("completionBlocker").GetString().Should().Be("Há apontamento aberto na etapa Corte.", "o botão desabilitado diz o mesmo");

        await CheckAllAsync(operatorClient, first);
        await OkAsync(await operatorClient.SendAsync(Tap($"{Shopfloor}/steps/{first}/finish", new { forAll = false })));
        await WorkUntilQualityAsync(operatorClient, order, 2);
        var quality = await CompleteOrderAsync(owner, order, await OrderVersionAsync(order));
        quality.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(quality);
        Code(problem).Should().Be("production_order_not_ready");
        problem.GetProperty("detail").GetString().Should().Be("Falta aprovar a Qualidade.");

        // CA 6: production.approve_quality sozinha não conclui nem estorna.
        var (_, inspector) = await ActorAsync("production:view,approve_quality");
        (await CompleteOrderAsync(inspector, order, await OrderVersionAsync(order))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReverseOrderAsync(inspector, order, await OrderVersionAsync(order))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await inspector.GetAsync($"{Base}/{order}/completion")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Cancelada: 409 com o motivo.
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.production_orders SET status = 'Cancelled' WHERE id = {order}"));
        (await JsonAsync(await CompleteOrderAsync(owner, order, await OrderVersionAsync(order)))).GetProperty("detail").GetString().Should().Be("A OS foi cancelada.");
    }

    [Fact]
    public async Task Projeto_ja_fabricado_a_mao_nao_muda_nem_publica_de_novo()
    {
        var (owner, project, contract, orders) = await CompletionDatasetAsync();
        var before = Today.AddDays(-2);
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE commercial.projects SET status = 'Fabricated', fabricated_on = {before}, fabricated_by_orders = false WHERE id = {project}"));
        await FinishStepsAsync(owner, orders[0]);
        await OkAsync(await CompleteOrderAsync(owner, orders[0], (await OrderAsync(orders[0])).Version));
        await DrainAsync();

        var saved = await ProjectAsync(project);
        saved.FabricatedOn.Should().Be(before, "CA 8");
        saved.FabricatedByOrders.Should().BeFalse();
        (await OutboxAsync<ProjectFabricated>(project)).Should().Be(0);
        (await FabricationTitlesAsync(contract)).Single().Status.Should().Be(ReceivableStatus.Planned, "nenhum ProjectFabricated foi publicado neste teste");
    }

    [Fact]
    public async Task Registro_manual_recusado_com_OS_de_fabricacao_e_permitido_sem_OS()
    {
        var (owner, project, _, _) = await CompletionDatasetAsync(2);
        var detail = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{project}"));
        detail.GetProperty("fabricationOrders").GetInt32().Should().Be(2);
        detail.GetProperty("openFabricationOrders").GetInt32().Should().Be(2);
        var refused = await owner.PostAsJsonAsync($"/api/v1/projects/{project}/fabrication", new { date = Iso(Today), version = Version(detail) });
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict, "CA 9");
        var problem = await JsonAsync(refused);
        Code(problem).Should().Be("fabrication_by_orders");
        problem.GetProperty("detail").GetString().Should().Be("A fabricação deste projeto é concluída pelas OS (2 abertas).");

        // Sem OS de fabricação (só serviço): o registro manual continua.
        var (service, _, _) = await ApprovedAsync(owner, [("Service", (Guid?)null, 1m)], milestones: FabricationMilestones);
        var plain = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{service}"));
        plain.GetProperty("fabricationOrders").GetInt32().Should().Be(0);
        (await owner.PostAsJsonAsync($"/api/v1/projects/{service}/fabrication", new { date = Iso(Today), version = Version(plain) })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ProjectAsync(service)).FabricatedByOrders.Should().BeFalse();
    }

    [Fact]
    public async Task Estorno_volta_a_OS_e_o_projeto_avisa_o_Financeiro_e_a_reconclusao_nao_libera_de_novo()
    {
        var (owner, project, contract, orders) = await CompletionDatasetAsync();
        var (financeId, _) = await ActorAsync("finance:view,edit");
        await FinishStepsAsync(owner, orders[0]);
        await OkAsync(await CompleteOrderAsync(owner, orders[0], (await OrderAsync(orders[0])).Version));
        await DrainAsync();
        var released = (await FabricationTitlesAsync(contract)).Single();

        var preview = await OkAsync(await owner.GetAsync($"{Base}/{orders[0]}/completion"));
        preview.GetProperty("canReverse").GetBoolean().Should().BeTrue();
        preview.GetProperty("projectFabricatedByOrders").GetBoolean().Should().BeTrue("o diálogo diz que o projeto volta");
        (await ReverseOrderAsync(owner, orders[0], Version(preview), reason: " ")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "motivo obrigatório");

        var reversed = await OkAsync(await ReverseOrderAsync(owner, orders[0], Version(preview)));
        reversed.GetProperty("status").GetString().Should().Be("InProgress", "CA 10");
        reversed.GetProperty("steps")[1].GetProperty("status").GetString().Should().Be("Ready", "a última etapa volta");
        reversed.GetProperty("completionReversalReason").GetString().Should().Be("Medida errada na cabine");
        (await OutboxAsync<ProductionOrderCompletionReversed>(orders[0])).Should().Be(1);
        await DrainAsync();

        var back = await ProjectAsync(project);
        back.Status.ToString().Should().Be("InProgress");
        back.FabricatedByOrders.Should().BeFalse();
        (await FabricationTitlesAsync(contract)).Single().Should().BeEquivalentTo(new { released.Id, Status = ReceivableStatus.Open, released.DueDate }, "o título não é estornado");
        (await WithDbAsync(db => db.Notifications.CountAsync(n => n.UserId == financeId && n.Type == FabricationCompletionConsumers.ReversedType
            && n.Title == $"Conclusão da {reversed.GetProperty("number").GetString()} estornada. A parcela Fabricação concluída já liberada continua em aberto."))).Should().Be(1);
        var timeline = await TextsAsync(owner, $"/api/v1/projects/{project}/timeline");
        timeline.Should().Contain("estornada: Medida errada na cabine", "CA 15");

        // Reconclusão: o projeto volta a Fabricado, o gatilho fica e a parcela não é liberada de novo.
        await FinishStepsAsync(owner, orders[0]);
        await OkAsync(await CompleteOrderAsync(owner, orders[0], (await OrderAsync(orders[0])).Version));
        await DrainAsync();
        (await ProjectAsync(project)).Status.ToString().Should().Be("Fabricated");
        (await FabricationTitlesAsync(contract)).Single().Should().BeEquivalentTo(new { released.Id, Status = ReceivableStatus.Open, released.DueDate });
        (await WithDbAsync(db => db.ProjectTriggerEvents.CountAsync(t => t.ProjectId == project && t.Event == TriggerEvent.FabricationDone))).Should().Be(1);
    }

    [Fact]
    public async Task Estorno_recusado_com_a_parcela_recebida_e_com_o_projeto_alem_da_fabricacao()
    {
        var (owner, project, contract, orders) = await CompletionDatasetAsync();
        await FinishStepsAsync(owner, orders[0]);
        await OkAsync(await CompleteOrderAsync(owner, orders[0], (await OrderAsync(orders[0])).Version));
        await DrainAsync();
        var title = (await FabricationTitlesAsync(contract)).Single();
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE finance.receivables SET paid_amount = 10, status = 'PartiallyPaid' WHERE id = {title.Id}"));

        var refused = await ReverseOrderAsync(owner, orders[0], (await OrderAsync(orders[0])).Version);
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict, "CA 11");
        var problem = await JsonAsync(refused);
        Code(problem).Should().Be("fabrication_reversal_blocked");
        problem.GetProperty("detail").GetString().Should().Be("A parcela Fabricação concluída já foi recebida. Fale com o Financeiro antes de estornar.");
        (await OrderAsync(orders[0])).Status.Should().Be(ProductionOrderStatus.Completed, "nada grava");
        (await OutboxAsync<ProductionOrderCompletionReversed>(orders[0])).Should().Be(0);

        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE finance.receivables SET paid_amount = 0, status = 'Open' WHERE id = {title.Id}"));
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE commercial.projects SET status = 'Delivered' WHERE id = {project}"));
        (await JsonAsync(await ReverseOrderAsync(owner, orders[0], (await OrderAsync(orders[0])).Version))).GetProperty("detail").GetString().Should().Be("O projeto já passou da fabricação.");
        (await JsonAsync(await ReverseOrderAsync(owner, orders[0], (await OrderAsync(orders[0])).Version))).GetProperty("code").GetString().Should().Be("fabrication_reversal_blocked");
    }

    [Fact]
    public async Task Conclusao_tem_as_tres_camadas_e_IDOR()
    {
        var (owner, project, _, orders) = await CompletionDatasetAsync();
        await FinishStepsAsync(owner, orders[0]);
        var version = (await OrderAsync(orders[0])).Version;
        var (_, viewer) = await ActorAsync("production:view,edit");
        (await CompleteOrderAsync(viewer, orders[0], version)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "CA 13: sem production.approve");
        var (_, nobody) = await ActorAsync(null);
        var forbidden = await CompleteOrderAsync(nobody, orders[0], version);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(forbidden)).Should().Be("forbidden");

        var installation = await InstallationAsync(owner, project);
        (await CompleteOrderAsync(owner, installation, 0)).StatusCode.Should().Be(HttpStatusCode.NotFound, "OS de instalação: 404");
        (await ReverseOrderAsync(owner, installation, 0)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetAsync($"{Base}/{Guid.NewGuid()}/completion")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CompleteOrderAsync(owner, orders[0], version + 99)).StatusCode.Should().Be(HttpStatusCode.Conflict, "versão antiga");
    }

    [Fact]
    public async Task Duas_conclusoes_juntas_so_uma_grava_e_o_projeto_fabrica_uma_vez()
    {
        var (owner, project, _, orders) = await CompletionDatasetAsync();
        await FinishStepsAsync(owner, orders[0]);
        var version = (await OrderAsync(orders[0])).Version;
        var results = await Task.WhenAll(CompleteOrderAsync(owner, orders[0], version), CompleteOrderAsync(owner, orders[0], version));
        results.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict], "CA 14: xmin");
        (await OutboxAsync<ProductionOrderCompleted>(orders[0])).Should().Be(1);
        await DrainAsync();
        (await OutboxAsync<ProjectFabricated>(project)).Should().Be(1);

        // Conclusão e estorno juntos na mesma versão: só um grava.
        var completed = (await OrderAsync(orders[0])).Version;
        var both = await Task.WhenAll(ReverseOrderAsync(owner, orders[0], completed), ReverseOrderAsync(owner, orders[0], completed));
        both.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
    }

    [Fact]
    public async Task Historico_da_OS_mostra_a_conclusao_e_o_estorno_com_o_motivo()
    {
        var (owner, _, _, orders) = await CompletionDatasetAsync();
        await FinishStepsAsync(owner, orders[0]);
        await OkAsync(await CompleteOrderAsync(owner, orders[0], (await OrderAsync(orders[0])).Version));
        await OkAsync(await ReverseOrderAsync(owner, orders[0], (await OrderAsync(orders[0])).Version, "Painel amassado no transporte"));

        var history = await TextsAsync(owner, $"{Base}/{orders[0]}/history?pageSize=100");
        history.Should().Contain("Painel amassado no transporte", "CA 15: o motivo do estorno").And.Contain("Completed").And.Contain("ReadyToComplete");
    }
}