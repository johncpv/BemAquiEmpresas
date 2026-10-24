using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Inventory.Contracts;
using BemAqui.Purchasing.Application;
using BemAqui.Purchasing.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Purchasing;

public partial class PurchaseRequestTests
{
    private const string Buyer = "purchasing:view,create,edit";

    private static object ManualBody(Guid? project, Guid item, decimal quantity = 5m, DateOnly? neededBy = null, Guid? unit = null, Guid? order = null) =>
        new { projectId = project, productionOrderId = order, notes = "Para o estoque de segurança", lines = new[] { new { itemId = item, quantity, unitId = unit, neededBy = neededBy ?? Today.AddDays(15) } } };

    /// <summary>CA 3: nasce Open, origem Manual, número SC-AAAA-NNNN, sem aprovação; a mesma Idempotency-Key não cria outra.</summary>
    [Fact]
    public async Task Enviar_solicitacao_manual_com_idempotencia()
    {
        await SetReleasedAsync(true);
        var (_, client) = await ActorAsync(Buyer);
        var item = await ItemAsync();
        var project = Project();
        var key = Guid.NewGuid();

        var created = await client.SendAsync(Post(Base + "/purchase-requests", ManualBody(project, item), key));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var body = await JsonAsync(created);
        body.GetProperty("number").GetString().Should().MatchRegex($"^SC-{Today.Year}-\\d{{4}}$");
        body.GetProperty("origin").GetString().Should().Be("Manual");
        body.GetProperty("status").GetString().Should().Be("Open");
        body.GetProperty("projectId").GetGuid().Should().Be(project);
        var line = body.GetProperty("lines")[0];
        line.GetProperty("quantity").GetDecimal().Should().Be(5m);
        line.GetProperty("neededBy").GetString().Should().Be(Today.AddDays(15).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

        var again = await client.SendAsync(Post(Base + "/purchase-requests", ManualBody(project, item), key));
        (await JsonAsync(again)).GetProperty("id").GetGuid().Should().Be(body.GetProperty("id").GetGuid(), "a mesma chave devolve a mesma SC");
        (await LinesOfItemAsync(item)).Should().ContainSingle();

        // Estoque geral: sem projeto.
        var general = await client.SendAsync(Post(Base + "/purchase-requests", ManualBody(null, item)));
        general.StatusCode.Should().Be(HttpStatusCode.Created);
        (await JsonAsync(general)).GetProperty("projectId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>CA 4 (ponto 33) e validações do CA 3.</summary>
    [Fact]
    public async Task Servico_necessidade_no_passado_e_projeto_encerrado_sao_recusados()
    {
        await SetReleasedAsync(true);
        var (_, client) = await ActorAsync(Buyer);
        var service = await ItemAsync(ItemType.Service);
        var item = await ItemAsync();

        var refused = await client.SendAsync(Post(Base + "/purchase-requests", ManualBody(null, service)));
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await JsonAsync(refused);
        Code(problem).Should().Be(PurchaseRequestService.ServiceNotAllowedCode);
        problem.GetProperty("detail").GetString().Should().Be("Serviço não entra na solicitação de compra. Lance um título a pagar manual.");

        (await client.SendAsync(Post(Base + "/purchase-requests", ManualBody(null, item, neededBy: Today.AddDays(-1))))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await client.SendAsync(Post(Base + "/purchase-requests", ManualBody(Project("Closed"), item)))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await client.SendAsync(Post(Base + "/purchase-requests", ManualBody(null, item, quantity: 0m)))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await LinesOfItemAsync(item)).Should().BeEmpty();
    }

    /// <summary>CA 13: editar a linha automática grava com auditoria, marca "Editada à mão", avisa a falta e sai da recomposição.</summary>
    [Fact]
    public async Task Editar_linha_da_falta_marca_editada_a_mao_e_avisa_a_falta()
    {
        await SetReleasedAsync(true);
        var (_, client) = await ActorAsync(Buyer);
        var item = await ItemAsync();
        var order = Order();
        Short(order, item, 10m);
        await DeliverAsync(order, item, 10m);
        var line = (await LinesOfOrderAsync(order)).Single();

        var response = await client.PutAsJsonAsync($"{Base}/purchase-requests/{line.RequestId}/lines/{line.Id}", new { quantity = 4m, neededBy = Today.AddDays(5), version = line.Version });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await JsonAsync(response);
        body.GetProperty("warning").GetString().Should().Be($"A OS {Orders[order].Number} continua com falta de 6.");
        var edited = (await LinesOfOrderAsync(order)).Single();
        edited.ManuallyEdited.Should().BeTrue();
        edited.Quantity.Should().Be(4m);

        // A falta sobe na fonte: a linha editada à mão não é sobrescrita e nenhuma outra nasce.
        Short(order, item, 12m);
        await DeliverAsync(order, item, 12m);
        (await LinesOfOrderAsync(order)).Should().ContainSingle().Which.Quantity.Should().Be(4m);

        // Versão antiga: 409.
        (await client.PutAsJsonAsync($"{Base}/purchase-requests/{line.RequestId}/lines/{line.Id}", new { quantity = 3m, neededBy = Today.AddDays(5), version = line.Version }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>CA 14: SC com linha em cotação não é cancelada (409 com o documento); a linha aberta e a SC aberta são.</summary>
    [Fact]
    public async Task Cancelar_solicitacao_e_linha()
    {
        await SetReleasedAsync(true);
        var (_, client) = await ActorAsync(Buyer);
        var a = await ItemAsync();
        var b = await ItemAsync();
        var body = new { projectId = (Guid?)null, notes = (string?)null, lines = new[] { new { itemId = a, quantity = 2m, neededBy = Today.AddDays(3) }, new { itemId = b, quantity = 1m, neededBy = Today.AddDays(3) } } };
        var created = await JsonAsync(await client.SendAsync(Post(Base + "/purchase-requests", body)));
        var id = created.GetProperty("id").GetGuid();
        var lineA = created.GetProperty("lines")[0];
        var lineB = created.GetProperty("lines")[1];
        await SetLineStatusAsync(lineB.GetProperty("id").GetGuid(), PurchaseRequestStatus.Quoting);
        var version = await WithDbAsync(db => db.PurchaseRequests.Where(r => r.Id == id).Select(r => r.Version).SingleAsync());

        var refused = await client.PostAsJsonAsync($"{Base}/purchase-requests/{id}/cancel", new { reason = "Não precisa mais", version });
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(refused);
        Code(problem).Should().Be(PurchaseRequestService.InProgressCode);
        problem.GetProperty("detail").GetString().Should().Contain(created.GetProperty("number").GetString());

        (await client.PostAsJsonAsync($"{Base}/purchase-requests/{id}/lines/{lineA.GetProperty("id").GetGuid()}/cancel", new { reason = "", version = lineA.GetProperty("version").GetUInt32() }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "o motivo é obrigatório");
        var cancelled = await client.PostAsJsonAsync($"{Base}/purchase-requests/{id}/lines/{lineA.GetProperty("id").GetGuid()}/cancel", new { reason = "Comprado no balcão", version = lineA.GetProperty("version").GetUInt32() });
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        var after = await JsonAsync(cancelled);
        after.GetProperty("lines")[0].GetProperty("status").GetString().Should().Be("Cancelled");
        after.GetProperty("status").GetString().Should().Be("Quoting", "a outra linha segue em cotação");

        // SC toda aberta: cancela com o motivo.
        var other = await JsonAsync(await client.SendAsync(Post(Base + "/purchase-requests", ManualBody(null, a))));
        var ok = await client.PostAsJsonAsync($"{Base}/purchase-requests/{other.GetProperty("id").GetGuid()}/cancel", new { reason = "Pedido em duplicidade", version = other.GetProperty("version").GetUInt32() });
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var done = await JsonAsync(ok);
        done.GetProperty("status").GetString().Should().Be("Cancelled");
        done.GetProperty("cancelReason").GetString().Should().Be("Pedido em duplicidade");
    }

    /// <summary>CA 12: "Aguardando você" para quem edita, filtros e a visão "Por item".</summary>
    [Fact]
    public async Task Lista_aguardando_voce_filtros_e_por_item()
    {
        await SetReleasedAsync(true);
        var (_, buyer) = await ActorAsync(Buyer);
        var (_, viewer) = await ActorAsync("purchasing:view");
        var item = await ItemAsync();
        var first = await JsonAsync(await buyer.SendAsync(Post(Base + "/purchase-requests", ManualBody(null, item, 2m, Today.AddDays(2)))));
        var second = await JsonAsync(await buyer.SendAsync(Post(Base + "/purchase-requests", ManualBody(Project(), item, 3m, Today.AddDays(9)))));
        var quoting = await JsonAsync(await buyer.SendAsync(Post(Base + "/purchase-requests", ManualBody(null, item, 7m))));
        await SetLineStatusAsync(quoting.GetProperty("lines")[0].GetProperty("id").GetGuid(), PurchaseRequestStatus.Quoting);

        var awaiting = await JsonAsync(await buyer.GetAsync($"{Base}/purchase-requests?itemId={item}"));
        var ids = awaiting.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).ToList();
        ids.Should().Equal([first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid()], "aguardando você: só as abertas, da necessidade mais próxima para a mais distante");
        awaiting.GetProperty("items")[0].GetProperty("nearestNeededBy").GetString().Should().Be(Today.AddDays(2).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

        var open = await JsonAsync(await viewer.GetAsync($"{Base}/purchase-requests?itemId={item}"));
        open.GetProperty("items").GetArrayLength().Should().Be(3, "sem purchasing.edit, o padrão são as abertas e em cotação");
        (await JsonAsync(await buyer.GetAsync($"{Base}/purchase-requests?itemId={item}&origin=MinStock&show=todas"))).GetProperty("items").GetArrayLength().Should().Be(0);

        var byItem = await JsonAsync(await viewer.GetAsync($"{Base}/purchase-requests/by-item?q={Uri.EscapeDataString((await WithDbAsync(db => db.Items.Where(i => i.Id == item).Select(i => i.Code).SingleAsync())))}"));
        var row = byItem.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("itemId").GetGuid() == item);
        row.GetProperty("openQuantity").GetDecimal().Should().Be(5m, "soma das linhas abertas (a em cotação fica fora)");
        row.GetProperty("requestCount").GetInt32().Should().Be(2);
    }

    /// <summary>CA 16: módulo não liberado, sem grupo, Almoxarifado só vê, IDOR da linha.</summary>
    [Fact]
    public async Task Tres_camadas_e_IDOR_da_linha()
    {
        await SetReleasedAsync(true);
        var (_, buyer) = await ActorAsync(Buyer);
        var (_, warehouse) = await ActorAsync("warehouse");
        var (_, nobody) = await ActorAsync(null);
        var item = await ItemAsync();
        var mine = await JsonAsync(await buyer.SendAsync(Post(Base + "/purchase-requests", ManualBody(null, item))));
        var other = await JsonAsync(await buyer.SendAsync(Post(Base + "/purchase-requests", ManualBody(null, item))));

        (await warehouse.GetAsync(Base + "/purchase-requests")).StatusCode.Should().Be(HttpStatusCode.OK, "o Almoxarifado vê a lista");
        var create = await warehouse.SendAsync(Post(Base + "/purchase-requests", ManualBody(null, item)));
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden, "sem purchasing.create");
        var nobodyList = await nobody.GetAsync(Base + "/purchase-requests");
        nobodyList.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(nobodyList)).Should().Be("forbidden");

        // IDOR: a linha de outra SC responde como inexistente.
        var foreignLine = other.GetProperty("lines")[0];
        var idor = await buyer.PutAsJsonAsync($"{Base}/purchase-requests/{mine.GetProperty("id").GetGuid()}/lines/{foreignLine.GetProperty("id").GetGuid()}",
            new { quantity = 1m, neededBy = Today.AddDays(1), version = foreignLine.GetProperty("version").GetUInt32() });
        idor.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await SetReleasedAsync(false);
        try
        {
            var locked = await buyer.GetAsync(Base + "/purchase-requests");
            locked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(locked)).Should().Be("module_not_released");
        }
        finally
        {
            await SetReleasedAsync(true);
        }
    }

    /// <summary>CA 17: antes e depois, o usuário ou "Sistema · Falta da BOM {OS}" / "Sistema · Estoque mínimo", e a data.</summary>
    [Fact]
    public async Task Historico_com_usuario_e_origem_do_sistema()
    {
        await SetReleasedAsync(true);
        var (buyerId, buyer) = await ActorAsync(Buyer);
        var item = await ItemAsync();
        var order = Order();
        Short(order, item, 8m);
        await DeliverAsync(order, item, 8m);
        var line = (await LinesOfOrderAsync(order)).Single();
        await buyer.PutAsJsonAsync($"{Base}/purchase-requests/{line.RequestId}/lines/{line.Id}", new { quantity = 6m, neededBy = Today.AddDays(4), version = line.Version });

        var history = await JsonAsync(await buyer.GetAsync($"{Base}/purchase-requests/{line.RequestId}/history"));
        var entries = history.GetProperty("items").EnumerateArray().ToList();
        entries.Should().Contain(e => e.GetProperty("entity").GetString() == "PurchaseRequestLine" && e.GetProperty("origin").GetString() == $"Sistema · Falta da BOM {Orders[order].Number}");
        entries.Should().Contain(e => e.GetProperty("entity").GetString() == "PurchaseRequestLine" && e.GetProperty("action").GetString() == "Updated"
            && e.GetProperty("userId").GetGuid() == buyerId && e.GetProperty("changes").GetString()!.Contains("\"quantity\"", StringComparison.Ordinal));
    }
}
