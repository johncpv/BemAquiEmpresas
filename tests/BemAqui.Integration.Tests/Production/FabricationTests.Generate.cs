using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Commercial.Contracts;
using BemAqui.Identity.Domain;
using BemAqui.Production.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Production;

public partial class FabricationTests
{
    [Fact]
    public async Task Equipamento_gera_OS_com_o_roteiro_do_produto_e_componente_e_servico_nao()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var center = await CenterAsync("Montagem");
        var product = await ProductAsync();
        var routing = await RoutingAsync(owner, center, product);
        var (projectId, _, items) = await ApprovedAsync(owner, [("Equipment", product, 2m), ("Product", null, 5m), ("Service", null, 1m)]);

        // CA 1 e 4: uma OS aberta por equipamento, com a quantidade do item e as etapas do roteiro do produto.
        var order = (await OrderOfAsync(items[0]))!;
        order.Number.Should().MatchRegex(@"^OS-\d{4}-\d{5}$");
        order.Status.Should().Be(ProductionOrderStatus.Open);
        order.Quantity.Should().Be(2m);
        order.ProductId.Should().Be(product);
        order.ProjectId.Should().Be(projectId);
        order.Priority.Should().Be(0);
        order.RoutingTemplateId.Should().Be(routing);
        order.InstallerId.Should().BeNull();
        order.ContractValue.Should().BeNull();
        var detail = await GetOrderAsync(owner, order.Id);
        detail.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("code").GetString()).Should().Equal("C", "M", "Q");
        detail.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("status").GetString()).Should().Equal("Ready", "Waiting", "Waiting");
        detail.GetProperty("steps")[1].GetProperty("checklist").EnumerateArray().Select(c => c.GetString()).Should().Equal("Conferir prumo");
        detail.GetProperty("totalStandardHours").GetDecimal().Should().Be(5.5m);
        detail.GetProperty("routingTemplateName").GetString().Should().NotBeNullOrEmpty();
        detail.GetProperty("started").GetBoolean().Should().BeFalse();

        // CA 2: componente fica para o "Gerar OS" e serviço nunca gera.
        (await OrderOfAsync(items[1])).Should().BeNull();
        (await OrderOfAsync(items[2])).Should().BeNull();
        var pending = await JsonAsync(await owner.GetAsync($"{Base}/pending-items?projectId={projectId}"));
        pending.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("projectItemId").GetGuid()).Should().Equal(items[1]);
        var service = await owner.SendAsync(Post(Base, new { projectItemId = items[2] }));
        service.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(service)).GetProperty("detail").GetString().Should().Be("Este item não gera OS de fabricação.");

        // CA 1: idempotente pelo item (mensagem reentregue com outro id não cria outra OS).
        await PublishRawAsync(Payload(projectId, items[0], product, 2m, "\"Kind\":\"Equipment\",\"IsOnboarding\":false"));
        await DrainAsync();
        (await WithDbAsync(db => db.ProductionOrders.CountAsync(o => o.ProjectItemId == items[0]))).Should().Be(1);

        // CA 5: o componente pelo botão, com Idempotency-Key (a repetição devolve a mesma OS).
        var key = Guid.NewGuid();
        var generated = await owner.SendAsync(Post(Base, new { projectItemId = items[1] }, key));
        generated.StatusCode.Should().Be(HttpStatusCode.Created, await generated.Content.ReadAsStringAsync());
        var component = await JsonAsync(generated);
        component.GetProperty("quantity").GetDecimal().Should().Be(5m);
        var replay = await JsonAsync(await owner.SendAsync(Post(Base, new { projectItemId = items[1] }, key)));
        replay.GetProperty("id").GetGuid().Should().Be(component.GetProperty("id").GetGuid());
        var again = await owner.SendAsync(Post(Base, new { projectItemId = items[1] }));
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(again)).GetProperty("detail").GetString().Should().Contain("já está na " + component.GetProperty("number").GetString());
    }

    [Fact]
    public async Task Mensagem_antiga_sem_o_tipo_nao_gera_OS_e_o_item_fica_para_o_botao()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (projectId, _, items) = await ApprovedAsync(owner, [("Equipment", null, 1m)]);
        var order = (await OrderOfAsync(items[0]))!;
        var cancelled = await owner.PostAsJsonAsync($"{Base}/{order.Id}/cancel", new { reason = "Teste", version = order.Version });
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());

        // CA 3: o payload do outbox anterior à PRD-01 (sem Kind, IsOnboarding e RoutingTemplateId) é lido sem erro e não gera OS.
        await PublishRawAsync(Payload(projectId, items[0], null, 1m, null));
        await DrainAsync();
        (await OrderOfAsync(items[0])).Should().BeNull();
        var pending = await JsonAsync(await owner.GetAsync($"{Base}/pending-items?projectId={projectId}"));
        pending.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("kind").GetString()).Should().Equal("Equipment");

        // CA 5: "Gerar OS" em lote mostra quantas nascem; cancelada não conta como OS do item.
        var batch = await owner.SendAsync(Post($"{Base}/batch", new { projectItemIds = new[] { items[0], Guid.NewGuid() } }));
        batch.StatusCode.Should().Be(HttpStatusCode.OK, await batch.Content.ReadAsStringAsync());
        var result = await JsonAsync(batch);
        result.GetProperty("created").GetInt32().Should().Be(1);
        result.GetProperty("skipped").GetArrayLength().Should().Be(1);
        (await OrderOfAsync(items[0])).Should().NotBeNull();
        (await JsonAsync(await owner.GetAsync($"{Base}/pending-items?projectId={projectId}"))).GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Implantacao_nunca_gera_OS_e_projeto_fabricado_bloqueia_o_botao()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);

        // CA 2: contrato de implantação não gera OS, nem pelo botão.
        var (onboardingProject, _, onboardingItems) = await ApprovedAsync(owner, [("Equipment", null, 1m)], onboarding: true);
        (await OrderOfAsync(onboardingItems[0])).Should().BeNull();
        var refused = await owner.SendAsync(Post(Base, new { projectItemId = onboardingItems[0] }));
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(refused)).GetProperty("detail").GetString().Should().Be("Contrato de implantação não gera OS: a fabricação aconteceu antes do sistema.");
        (await JsonAsync(await owner.GetAsync($"{Base}/pending-items?projectId={onboardingProject}"))).GetProperty("total").GetInt32().Should().Be(0);

        // CA 6: projeto já fabricado (registro manual da COM-04b) não gera OS nova e sai do filtro.
        var (projectId, _, items) = await ApprovedAsync(owner, [("Product", null, 1m)]);
        var project = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}"));
        var fabricated = await owner.PostAsJsonAsync($"/api/v1/projects/{projectId}/fabrication", new { date = Iso(Today), version = Version(project) });
        fabricated.StatusCode.Should().Be(HttpStatusCode.OK, await fabricated.Content.ReadAsStringAsync());
        var blocked = await owner.SendAsync(Post(Base, new { projectItemId = items[0] }));
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(blocked)).GetProperty("detail").GetString().Should().Be($"O projeto {project.GetProperty("number").GetString()} já foi fabricado: não gera OS nova.");
        (await JsonAsync(await owner.GetAsync($"{Base}/pending-items?projectId={projectId}"))).GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Sem_roteiro_sugerido_a_OS_nasce_sem_etapas_e_avisa_quem_edita()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (supervisorId, _) = await ActorAsync("production_supervision");
        var center = await CenterAsync("Corte");

        // Dois roteiros ativos sem vínculo e item sem produto: nenhuma regra do CA 4 escolhe.
        await RoutingAsync(owner, center);
        var other = await RoutingAsync(owner, center);
        var (projectId, _, items) = await ApprovedAsync(owner, [("Equipment", null, 1m)]);
        var order = (await OrderOfAsync(items[0]))!;
        order.RoutingTemplateId.Should().BeNull();
        var detail = await GetOrderAsync(owner, order.Id);
        detail.GetProperty("steps").GetArrayLength().Should().Be(0);
        var notification = await WithDbAsync(db => db.Notifications.AsNoTracking().SingleAsync(n => n.UserId == supervisorId && n.Link == "/app/producao/os/" + order.Id));
        notification.Title.Should().Be($"OS {order.Number} de {detail.GetProperty("projectNumber").GetString()} sem roteiro.");

        // CA 4 e 9: "Trocar roteiro" lista os ativos com o impacto; trocar copia as etapas.
        var options = await JsonAsync(await owner.GetAsync($"{Base}/{order.Id}/routings"));
        options.EnumerateArray().Should().Contain(o => o.GetProperty("id").GetGuid() == other && o.GetProperty("stepCount").GetInt32() == 3);
        options.EnumerateArray().Should().NotContain(o => o.GetProperty("suggested").GetBoolean());
        var changed = await owner.PostAsJsonAsync($"{Base}/{order.Id}/routing", new { routingTemplateId = other, version = Version(detail) });
        changed.StatusCode.Should().Be(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        (await JsonAsync(changed)).GetProperty("routingTemplateId").GetGuid().Should().Be(other);

        // CA 4: o último roteiro usado com o mesmo produto é sugerido na próxima OS do produto.
        var product = await ProductAsync("Porta de pavimento");
        var (_, _, first) = await ApprovedAsync(owner, [("Equipment", product, 1m)]);
        var firstOrder = (await OrderOfAsync(first[0]))!;
        firstOrder.RoutingTemplateId.Should().BeNull();
        var firstDetail = await GetOrderAsync(owner, firstOrder.Id);
        (await owner.PostAsJsonAsync($"{Base}/{firstOrder.Id}/routing", new { routingTemplateId = other, version = Version(firstDetail) })).StatusCode.Should().Be(HttpStatusCode.OK);
        var (_, _, second) = await ApprovedAsync(owner, [("Equipment", product, 1m)]);
        (await OrderOfAsync(second[0]))!.RoutingTemplateId.Should().Be(other);
        _ = projectId;
    }

    private static string Payload(Guid projectId, Guid projectItemId, Guid? itemId, decimal quantity, string? extra)
    {
        var item = itemId is { } id ? "\"" + id + "\"" : "null";
        var tail = extra is null ? string.Empty : "," + extra;
        return "{\"ProjectId\":\"" + projectId + "\",\"ProjectItemId\":\"" + projectItemId + "\",\"QuoteItemId\":\"" + Guid.NewGuid() + "\",\"ItemId\":" + item
            + ",\"Quantity\":" + quantity.ToString(System.Globalization.CultureInfo.InvariantCulture) + tail + "}";
    }

    /// <summary>Grava no outbox um ProjectItemCreated com o payload dado (mensagem antiga ou reentrega com outro id).</summary>
    private Task<int> PublishRawAsync(string payload) => WithDbAsync(async db =>
    {
        var now = DateTimeOffset.UtcNow;
        db.OutboxMessages.Add(new OutboxMessage { Type = IntegrationEventRegistry.NameOf(typeof(ProjectItemCreated)), Payload = payload, OccurredAt = now, NextAttemptAt = now });
        return await db.SaveChangesAsync();
    });
}