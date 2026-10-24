using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Finance.Contracts;
using BemAqui.Purchasing.Application;
using BemAqui.Purchasing.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Purchasing;

public partial class PurchaseOrderTests
{
    private static object UpdateBody(JsonElement order, decimal price, Guid? term = null, decimal? quantity = null)
    {
        var line = order.GetProperty("lines")[0];
        return new
        {
            partyId = order.GetProperty("partyId").GetGuid(), projectId = order.GetProperty("projectId").ValueKind == JsonValueKind.Null ? (Guid?)null : order.GetProperty("projectId").GetGuid(),
            paymentTermId = term ?? order.GetProperty("paymentTermId").GetGuid(), expectedDate = order.GetProperty("expectedDate").GetString(), freight = order.GetProperty("freight").GetDecimal(),
            ipiPct = order.GetProperty("ipiPct").GetDecimal(), notes = (string?)null,
            lines = new[] { new { itemId = line.GetProperty("itemId").GetGuid(), quantity = quantity ?? line.GetProperty("quantity").GetDecimal(), unitId = (Guid?)null, unitPrice = price, requestLineIds = Array.Empty<Guid>() } },
            version = Version(order),
        };
    }

    private async Task<JsonElement> ApprovedAsync(HttpClient owner, HttpClient buyer, string term = "30/60/90 dias", decimal quantity = 10m, decimal price = 100m)
    {
        var supplier = await SupplierAsync(owner, term);
        var order = await SubmitAsync(buyer, await CreateAsync(buyer, OrderBody(supplier, null, null, (await ItemAsync(), quantity, price, null))));
        var approved = await DecideAsync(owner, order, true);
        approved.StatusCode.Should().Be(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        return await JsonAsync(approved);
    }

    /// <summary>
    /// CA 15: editar o pedido aprovado volta a PendingApproval com nova resolução; os previstos de antes continuam até a reaprovação, que os
    /// troca na mesma transação (mesmas parcelas atualizadas; a que sobra é cancelada).
    /// </summary>
    [Fact]
    public async Task Editar_aprovado_volta_a_aprovacao_e_a_reaprovacao_troca_as_parcelas()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var approved = await ApprovedAsync(owner, buyer);
        var id = approved.GetProperty("id").GetGuid();
        var before = await PayablesOfAsync(id);

        var edited = await buyer.PutAsJsonAsync($"{Base}/{id}", UpdateBody(approved, 120m, await TermAsync("30/60 dias")));

        edited.StatusCode.Should().Be(HttpStatusCode.OK, await edited.Content.ReadAsStringAsync());
        var pending = await JsonAsync(edited);
        pending.GetProperty("status").GetString().Should().Be("PendingApproval");
        pending.GetProperty("attempt").GetInt32().Should().Be(2);
        pending.GetProperty("total").GetDecimal().Should().Be(1_200m);
        (await PayablesOfAsync(id)).Should().BeEquivalentTo(before, o => o.Excluding(p => p.Version), "os previstos de antes continuam até a reaprovação");

        (await DecideAsync(owner, pending, true)).StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await PayablesOfAsync(id);
        var active = after.Where(p => p.Status != PayableStatus.Cancelled).OrderBy(p => p.Installment).ToList();
        active.Select(p => p.Amount).Should().Equal(600m, 600m);
        active.Select(p => p.Id).Should().Equal(before.Take(2).Select(p => p.Id), "a parcela que já existia é atualizada no lugar");
        after.Single(p => p.Installment == 3).Status.Should().Be(PayableStatus.Cancelled, "a terceira parcela sobrou");
        var decisions = await JsonAsync(await buyer.GetAsync($"{Base}/{id}/decisions"));
        decisions.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("decision").GetString()).Should().Contain("Changed");
    }

    /// <summary>CA 15: pedido enviado editado volta aprovado com "Alterado depois do envio".</summary>
    [Fact]
    public async Task Pedido_enviado_editado_fica_marcado_para_reenvio()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var approved = await ApprovedAsync(owner, buyer, "À vista");
        var id = approved.GetProperty("id").GetGuid();
        // O envio ao fornecedor é da CMP-03b: o teste põe o pedido em Sent direto no banco.
        await WithDbAsync(async db =>
        {
            var order = await db.PurchaseOrders.SingleAsync(o => o.Id == id);
            order.Status = PurchaseOrderStatus.Sent;
            return await db.SaveChangesAsync();
        });
        var sent = await GetAsync(buyer, id);

        var edited = await JsonAsync(await buyer.PutAsJsonAsync($"{Base}/{id}", UpdateBody(sent, 90m)));
        edited.GetProperty("status").GetString().Should().Be("PendingApproval");
        var reapproved = await JsonAsync(await DecideAsync(owner, edited, true));

        reapproved.GetProperty("status").GetString().Should().Be("Approved");
        reapproved.GetProperty("changedAfterSent").GetBoolean().Should().BeTrue();
        (await PayablesOfAsync(id)).Where(p => p.Status != PayableStatus.Cancelled).Select(p => p.Amount).Should().Equal(900m);
    }

    /// <summary>CA 15: algum previsto numa OP recusa a edição com 409 payable_in_payment_order e o id da OP.</summary>
    [Fact]
    public async Task Previsto_em_OP_recusa_a_edicao()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var approved = await ApprovedAsync(owner, buyer);
        var id = approved.GetProperty("id").GetGuid();
        // A OP é da FIN-03a: o teste põe a primeira parcela em AwaitingApproval direto no banco.
        await WithDbAsync(async db =>
        {
            var first = await db.Payables.SingleAsync(p => p.Origin == BemAqui.SharedKernel.DocumentOrigin.Purchase && p.OriginId == id && p.Installment == 1);
            first.Status = PayableStatus.AwaitingApproval;
            return await db.SaveChangesAsync();
        });

        var refused = await buyer.PutAsJsonAsync($"{Base}/{id}", UpdateBody(approved, 101m));

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(refused)).Should().Be("payable_in_payment_order");
        (await GetAsync(buyer, id)).GetProperty("status").GetString().Should().Be("Approved", "a edição não gravou nada");
        var cancel = await buyer.PostAsJsonAsync($"{Base}/{id}/cancel", new { reason = "Desistimos", version = Version(approved) });
        cancel.StatusCode.Should().Be(HttpStatusCode.Conflict, "título em OP impede o cancelamento (CA 16)");
    }

    /// <summary>CA 16: cancelar (sem recebimento) cancela os previstos pela origem e devolve as linhas de SC para Open.</summary>
    [Fact]
    public async Task Cancelar_cancela_os_previstos_e_devolve_as_linhas_de_SC()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierAsync(owner);
        var item = await ItemAsync();
        var (requestId, lineId) = await RequestAsync(buyer, item, 5m);
        var order = await SubmitAsync(buyer, await CreateAsync(buyer, OrderBody(supplier, null, null, (item, 5m, 40m, [lineId]))));
        var approved = await JsonAsync(await DecideAsync(owner, order, true));
        var id = approved.GetProperty("id").GetGuid();

        (await buyer.PostAsJsonAsync($"{Base}/{id}/cancel", new { reason = " ", version = Version(approved) })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var cancelled = await buyer.PostAsJsonAsync($"{Base}/{id}/cancel", new { reason = "Fornecedor sem estoque", version = Version(approved) });

        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        (await JsonAsync(cancelled)).GetProperty("status").GetString().Should().Be("Cancelled");
        (await PayablesOfAsync(id)).Should().OnlyContain(p => p.Status == PayableStatus.Cancelled);
        (await RequestLineAsync(lineId)).Status.Should().Be(PurchaseRequestStatus.Open);
        (await WithDbAsync(db => db.PurchaseRequests.AsNoTracking().SingleAsync(r => r.Id == requestId))).Status.Should().Be(PurchaseRequestStatus.Open);
        var outbox = await WithDbAsync(db => db.OutboxMessages.AsNoTracking().Where(m => m.Type.Contains("PurchaseOrderCancelled")).ToListAsync());
        outbox.Select(m => JsonDocument.Parse(m.Payload).RootElement.GetProperty("purchaseOrderId").GetGuid()).Should().Contain(id);
    }

    /// <summary>CA 16: pedido com recebimento não é cancelado nem editado (409).</summary>
    [Fact]
    public async Task Pedido_com_recebimento_nao_cancela()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var approved = await ApprovedAsync(owner, buyer);
        var id = approved.GetProperty("id").GetGuid();
        // O recebimento é da CMP-04: o teste grava a quantidade recebida direto no banco.
        await WithDbAsync(async db =>
        {
            var line = await db.PurchaseOrderLines.SingleAsync(l => l.OrderId == id);
            line.QuantityReceived = 1m;
            return await db.SaveChangesAsync();
        });

        var refused = await buyer.PostAsJsonAsync($"{Base}/{id}/cancel", new { reason = "Desistimos", version = Version(approved) });

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(refused);
        Code(problem).Should().Be("purchase_order_received");
        problem.GetProperty("detail").GetString().Should().Be("Já houve recebimento: encerre o saldo do pedido.");
        (await buyer.PutAsJsonAsync($"{Base}/{id}", UpdateBody(approved, 50m))).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>CA 22: duas aprovações ao mesmo tempo: só uma grava (xmin) e os títulos não duplicam.</summary>
    [Fact]
    public async Task Duas_aprovacoes_simultaneas_so_uma_grava()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, other) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierAsync(owner);
        var order = await SubmitAsync(buyer, await CreateAsync(buyer, OrderBody(supplier, null, null, (await ItemAsync(), 3m, 100m, null))));

        var results = await Task.WhenAll(DecideAsync(owner, order, true), DecideAsync(other, order, true));

        results.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        (await PayablesOfAsync(order.GetProperty("id").GetGuid())).Should().HaveCount(3, "uma parcela por vencimento, sem duplicar");
    }

    /// <summary>CA 22: aprovação e edição ao mesmo tempo: só uma grava; os títulos ficam coerentes com o vencedor.</summary>
    [Fact]
    public async Task Aprovacao_e_edicao_simultaneas_so_uma_grava()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierAsync(owner);
        var order = await SubmitAsync(buyer, await CreateAsync(buyer, OrderBody(supplier, null, null, (await ItemAsync(), 3m, 100m, null))));
        var id = order.GetProperty("id").GetGuid();

        var results = await Task.WhenAll(DecideAsync(owner, order, true), buyer.PutAsJsonAsync($"{Base}/{id}", UpdateBody(order, 110m)));

        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
        var final = await GetAsync(buyer, id);
        var payables = await PayablesOfAsync(id);
        if (final.GetProperty("status").GetString() == "Approved")
        {
            payables.Should().HaveCount(3);
        }
        else
        {
            final.GetProperty("total").GetDecimal().Should().Be(330m);
            payables.Should().BeEmpty();
        }
    }

    /// <summary>CA 24: saldo a receber por item, na unidade de estoque, dos pedidos aprovados, enviados e parcialmente recebidos, por destino.</summary>
    [Fact]
    public async Task Saldo_a_receber_por_item_por_situacao_e_destino()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var supplier = await SupplierAsync(owner);
        var item = await ItemAsync();
        var project = Project();
        async Task<JsonElement> Approve(Guid? destination, decimal quantity) =>
            await JsonAsync(await DecideAsync(owner, await SubmitAsync(buyer, await CreateAsync(buyer, OrderBody(supplier, destination, null, (item, quantity, 10m, null)))), true));
        var general = await Approve(null, 5m);
        await Approve(project, 7m);
        await CreateAsync(buyer, OrderBody(supplier, null, null, (item, 100m, 10m, null)));
        var cancelled = await Approve(null, 50m);
        (await buyer.PostAsJsonAsync($"{Base}/{cancelled.GetProperty("id").GetGuid()}/cancel", new { reason = "Não", version = Version(cancelled) })).StatusCode.Should().Be(HttpStatusCode.OK);
        await WithDbAsync(async db =>
        {
            var line = await db.PurchaseOrderLines.SingleAsync(l => l.OrderId == general.GetProperty("id").GetGuid());
            line.QuantityReceived = 2m;
            var order = await db.PurchaseOrders.SingleAsync(o => o.Id == general.GetProperty("id").GetGuid());
            order.Status = PurchaseOrderStatus.PartiallyReceived;
            return await db.SaveChangesAsync();
        });

        await using var scope = await ScopeAsync();
        var contract = scope.ServiceProvider.GetRequiredService<IPurchaseOrders>();
        (await contract.OpenQuantityByItemAsync([item], PurchaseOrderDestination.GeneralStock))[item].Should().Be(3m, "5 − 2 recebidos; rascunho e cancelado fora");
        (await contract.OpenQuantityByItemAsync([item], PurchaseOrderDestination.Project(project)))[item].Should().Be(7m);
        (await contract.OpenQuantityByItemAsync([item], PurchaseOrderDestination.All))[item].Should().Be(10m);
        (await scope.ServiceProvider.GetRequiredService<IOpenPurchaseOrderQuantities>().GeneralStockOpenQuantityAsync([item]))[item]
            .Should().Be(3m, "o job do mínimo da CMP-01 desconta o saldo a receber do estoque geral");
        (await contract.CommittedByProjectAsync(project)).Should().ContainSingle().Which.Amount.Should().Be(70m);
    }
}
