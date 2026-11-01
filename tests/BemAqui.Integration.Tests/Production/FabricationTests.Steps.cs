using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Identity.Domain;
using BemAqui.Production.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Production;

public partial class FabricationOrdersTests
{
    [Fact]
    public async Task Etapas_editaveis_antes_do_inicio_com_as_regras_do_roteiro_e_bloqueadas_depois()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var center = await CenterAsync("Solda");
        var product = await ProductAsync();
        var routing = await RoutingAsync(owner, center, product);
        var (_, _, items) = await ApprovedAsync(owner, [("Equipment", product, 1m), ("Equipment", product, 1m)]);
        var orderId = (await OrderOfAsync(items[0]))!.Id;
        var otherId = (await OrderOfAsync(items[1]))!.Id;
        var order = await GetOrderAsync(owner, orderId);
        var other = await GetOrderAsync(owner, otherId);

        // CA 7: regras do roteiro (letra repetida, primeira de qualidade, sem centro).
        var steps = CurrentSteps(order);
        async Task<JsonElement> RejectedAsync(List<object> body)
        {
            var response = await owner.PutAsJsonAsync($"{Base}/{orderId}/steps", StepsBody(order, body));
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, await response.Content.ReadAsStringAsync());
            return (await JsonAsync(response)).GetProperty("errors");
        }

        (await RejectedAsync([.. steps, Step("Corte fino", "C", center)])).GetProperty("steps[3].code")[0].GetString().Should().Be("A letra C já está na etapa 1.");
        (await RejectedAsync([Step("Inspeção", "I", center, quality: true), .. steps])).GetProperty("steps[0].isQualityStep")[0].GetString().Should().Be("A primeira etapa não pode ser de qualidade.");
        (await RejectedAsync([.. steps, Step("Expedição", "E", null)])).GetProperty("steps[3].workCenterId")[0].GetString().Should().Be("Escolha o centro de trabalho da etapa.");

        // CA 23 (IDOR): etapa de outra OS não entra nesta.
        var foreign = CurrentSteps(other)[0];
        (await owner.PutAsJsonAsync($"{Base}/{orderId}/steps", StepsBody(order, [foreign]))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // CA 7: incluir, remover, reordenar e mudar horas; o template não muda.
        var expedicao = Step("Expedição", "E", center, 1.25m, checklist: ["Preparar e embalar"]);
        var saved = await owner.PutAsJsonAsync($"{Base}/{orderId}/steps", StepsBody(order, [steps[1], steps[0], expedicao]));
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var after = await JsonAsync(saved);
        after.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("code").GetString()).Should().Equal("M", "C", "E");
        after.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("status").GetString()).Should().Equal("Ready", "Waiting", "Waiting");
        after.GetProperty("steps")[2].GetProperty("checklist")[0].GetString().Should().Be("Preparar e embalar");
        after.GetProperty("totalStandardHours").GetDecimal().Should().Be(6.25m);
        (await JsonAsync(await owner.GetAsync($"{Routings}/{routing}"))).GetProperty("steps").GetArrayLength().Should().Be(3);

        // CA 24: a versão antiga recebe 409.
        var stale = await owner.PutAsJsonAsync($"{Base}/{orderId}/steps", StepsBody(order, [steps[1], steps[0]]));
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(stale);
        Code(problem).Should().Be("concurrency_conflict");
        problem.GetProperty("detail").GetString().Should().Be("A OS mudou desde que você abriu. Recarregue.");

        // CA 25: histórico com a OS e as etapas.
        var history = await JsonAsync(await owner.GetAsync($"{Base}/{orderId}/history"));
        history.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("entity").GetString()).Should().Contain(["ProductionOrderStep", "ProductionOrderChecklistItem"]);

        // CA 8 e 9: com a etapa iniciada, nem etapas nem roteiro mudam.
        await MarkStepAsync(orderId, 1, ProductionStepStatus.InProgress);
        after = await GetOrderAsync(owner, orderId);
        after.GetProperty("started").GetBoolean().Should().BeTrue();
        var locked = await owner.PutAsJsonAsync($"{Base}/{orderId}/steps", StepsBody(after, CurrentSteps(after)));
        locked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(locked)).GetProperty("detail").GetString().Should().Be("A OS já começou: as etapas não mudam.");
        (await owner.PostAsJsonAsync($"{Base}/{orderId}/routing", new { routingTemplateId = routing, version = Version(after) })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cancelar_OS_aberta_publica_o_evento_e_a_iniciada_exige_quem_aprova()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var center = await CenterAsync("Pintura");
        var product = await ProductAsync();
        await RoutingAsync(owner, center, product);
        var (projectId, _, items) = await ApprovedAsync(owner, [("Equipment", product, 1m), ("Equipment", product, 1m)]);
        var open = await GetOrderAsync(owner, (await OrderOfAsync(items[0]))!.Id);
        var startedId = (await OrderOfAsync(items[1]))!.Id;
        var (_, engineering) = await ActorAsync("engineering");
        var (_, supervisor) = await ActorAsync("production_supervision");

        // CA 16: motivo obrigatório; aberta sem etapa iniciada com production.edit; publica ProductionOrderCancelled.
        var openId = open.GetProperty("id").GetGuid();
        (await engineering.PostAsJsonAsync($"{Base}/{openId}/cancel", new { reason = " ", version = Version(open) })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var cancelled = await engineering.PostAsJsonAsync($"{Base}/{openId}/cancel", new { reason = "Cliente trocou o modelo", version = Version(open) });
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        (await JsonAsync(cancelled)).GetProperty("status").GetString().Should().Be("Cancelled");
        var outbox = await WithDbAsync(db => db.OutboxMessages.AsNoTracking().Where(m => m.Type.EndsWith("ProductionOrderCancelled")).Select(m => m.Payload).ToListAsync());
        outbox.Should().Contain(p => p.Contains(openId.ToString()) && p.Contains(projectId.ToString()) && !p.Contains("modelo"));

        // CA 17: iniciada só com production.approve.
        await MarkStepAsync(startedId, 1, ProductionStepStatus.InProgress);
        var started = await GetOrderAsync(owner, startedId);
        (await engineering.PostAsJsonAsync($"{Base}/{startedId}/cancel", new { reason = "Parar", version = Version(started) })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var approved = await supervisor.PostAsJsonAsync($"{Base}/{startedId}/cancel", new { reason = "Parar", version = Version(started) });
        approved.StatusCode.Should().Be(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        (await supervisor.PostAsJsonAsync($"{Base}/{startedId}/cancel", new { reason = "De novo", version = Version(await JsonAsync(approved)) })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Contrato_cancelado_cancela_as_OS_de_fabricacao_abertas_e_avisa_das_iniciadas()
    {
        var (ownerId, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (projectId, contractId, items) = await ApprovedAsync(owner, [("Equipment", null, 1m), ("Equipment", null, 1m)]);
        var open = (await OrderOfAsync(items[0]))!;
        var started = (await OrderOfAsync(items[1]))!;
        await MarkStepAsync(started.Id, 1, ProductionStepStatus.InProgress);
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.production_orders SET status = 'InProgress' WHERE id = {started.Id}"));

        var contract = await JsonAsync(await owner.GetAsync($"/api/v1/contracts/{contractId}"));
        var cancelled = await owner.PostAsJsonAsync($"/api/v1/contracts/{contractId}/cancel", new { reason = "Cliente desistiu", version = Version(contract) });
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        await DrainAsync();

        // CA 18: a aberta cai; a iniciada fica para decisão, com aviso ao responsável do projeto.
        (await WithDbAsync(db => db.ProductionOrders.AsNoTracking().SingleAsync(o => o.Id == open.Id))).Status.Should().Be(ProductionOrderStatus.Cancelled);
        (await WithDbAsync(db => db.ProductionOrders.AsNoTracking().SingleAsync(o => o.Id == started.Id))).Status.Should().Be(ProductionOrderStatus.InProgress);
        (await WithDbAsync(db => db.Notifications.CountAsync(n => n.UserId == ownerId && n.Title == $"Contrato cancelado com a OS {started.Number} iniciada: decida se continua ou cancela."))).Should().Be(1);
        (await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}"))).GetProperty("status").GetString().Should().Be("Cancelled");
    }
}