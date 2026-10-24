using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// CMP-04 CA 2, 14, 16 e 17: três camadas (módulo <c>purchasing</c> ou <c>inventory</c> não liberado, sem grupo, sem a ação), o
/// Almoxarifado do seed recebendo sem editar o pedido, a segregação de quem pede, aprova ou corrige o preço, a correção de preço dentro e
/// acima do teto e dois recebimentos simultâneos do mesmo saldo. Banco desta classe (os testes ligam e desligam módulos).
/// </summary>
public class GoodsReceiptSecurityTests(TenancyFixture tenancy) : GoodsReceiptTestBase(tenancy), IClassFixture<TenancyFixture>
{
    private async Task SetModuleAsync(string module, bool released)
    {
        var tenantId = Tenancy.TenantA.Id;
        await using (var scope = Tenancy.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules
                .Where(m => m.TenantId == tenantId && m.ModuleCode == module).ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        Tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
        Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    [Fact]
    public async Task Tres_camadas_e_o_Almoxarifado_recebe_sem_editar_o_pedido()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (_, receiver) = await ActorAsync("warehouse");
        var (_, nobody) = await ActorAsync(null);
        var (_, creator) = await ActorAsync("purchasing:view,create;inventory:view");
        var item = await ItemAsync();
        var order = await ApprovedOrderAsync(owner, buyer, await SupplierAsync(owner), 0m, 0m, "À vista", null, (item, 2m, 10m, null));
        var body = ReceiptBody(order, null, null, (LineOf(order, item), 1m, null));
        var id = order.GetProperty("id").GetGuid();

        (await ReceiveAsync(creator, order, body)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "purchasing.create não registra recebimento");
        var none = await ReceiveAsync(nobody, order, body);
        none.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(none)).Should().Be("forbidden");
        (await buyer.PostAsJsonAsync($"{Base}/{id}/lines/{LineOf(order, item)}/correct-price", new { unitPrice = 9m, reason = "x", version = Version(order) }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "corrigir preço exige purchasing.approve");
        (await receiver.PutAsJsonAsync($"{Base}/{id}/lines/{LineOf(order, item)}", new { quantity = 3m, unitPrice = 10m, version = Version(order) }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "o Almoxarifado não edita o pedido");

        await SetModuleAsync("inventory", false);
        try
        {
            var blocked = await ReceiveAsync(receiver, order, body);
            blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(blocked)).Should().Be("module_not_released");
        }
        finally
        {
            await SetModuleAsync("inventory", true);
        }

        await SetModuleAsync("purchasing", false);
        try
        {
            var blocked = await receiver.GetAsync(Receipts);
            blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(blocked)).Should().Be("module_not_released");
        }
        finally
        {
            await SetModuleAsync("purchasing", true);
        }

        var receipt = await ReceivedAsync(receiver, order, body);
        (await receiver.PostAsJsonAsync($"{Receipts}/{receipt.GetProperty("id").GetGuid()}/reverse", new { reason = "x", version = Version(receipt) }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "estornar exige purchasing.approve");
        (await receiver.PostAsJsonAsync($"{Base}/{id}/close", new { reason = "x", version = Version(await GetAsync(owner, id)) }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "encerrar exige purchasing.edit");
    }

    /// <summary>CA 2: quem pediu ou aprovou não registra o recebimento (403 segregation_of_duties) quando existe outro recebedor.</summary>
    [Fact]
    public async Task Quem_pediu_ou_aprovou_nao_registra_o_recebimento()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync("purchasing:view,create,edit,receive;inventory:view");
        await ActorAsync("warehouse");
        var item = await ItemAsync();
        var order = await ApprovedOrderAsync(owner, buyer, await SupplierAsync(owner), 0m, 0m, "À vista", null, (item, 2m, 10m, null));
        var body = ReceiptBody(order, null, null, (LineOf(order, item), 2m, null));

        foreach (var client in new[] { buyer, owner })
        {
            var refused = await ReceiveAsync(client, order, body);
            refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var problem = await JsonAsync(refused);
            Code(problem).Should().Be("segregation_of_duties");
            problem.GetProperty("detail").GetString().Should().Be("Quem pediu ou aprovou este pedido não registra o recebimento dele.");
        }

        var detail = await GetAsync(buyer, order.GetProperty("id").GetGuid());
        detail.GetProperty("canReceive").GetBoolean().Should().BeFalse();
        detail.GetProperty("receiveBlocker").GetString().Should().Be("Quem pediu ou aprovou este pedido não registra o recebimento dele.");
    }

    /// <summary>
    /// CA 14: dentro do teto, o preço novo vale para o saldo e o previsto é replanejado; acima, o pedido volta à alçada e o recebimento
    /// fica desabilitado com "Pedido em reaprovação". Quem recebeu não corrige o preço; quem corrigiu não recebe.
    /// </summary>
    [Fact]
    public async Task Correcao_de_preco_dentro_e_acima_do_teto()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (_, receiver) = await ActorAsync("warehouse");
        var (_, approver) = await ActorAsync("purchasing:view,approve,receive;inventory:view");
        await ActorAsync("warehouse");
        var item = await ItemAsync();
        var order = await ApprovedOrderAsync(owner, buyer, await SupplierAsync(owner), 0m, 0m, "À vista", null, (item, 10m, 10m, null));
        var id = order.GetProperty("id").GetGuid();
        var line = LineOf(order, item);
        await ReceivedAsync(receiver, order, ReceiptBody(order, null, null, (line, 5m, null)));
        var partial = await GetAsync(owner, id);

        var lower = await approver.PostAsJsonAsync($"{Base}/{id}/lines/{line}/correct-price", new { unitPrice = 9m, reason = "Desconto do fornecedor", version = Version(partial) });

        lower.StatusCode.Should().Be(HttpStatusCode.OK, await lower.Content.ReadAsStringAsync());
        var corrected = await JsonAsync(lower);
        corrected.GetProperty("total").GetDecimal().Should().Be(95m, "50,00 recebidos + 5 × 9,00");
        corrected.GetProperty("status").GetString().Should().Be("PartiallyReceived");
        Planned(await PayablesAsync(BemAqui.SharedKernel.DocumentOrigin.Purchase, id)).Should().Be(45m);
        (await ReceiveAsync(approver, corrected, ReceiptBody(corrected, null, null, (line, 1m, null)))).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "quem corrigiu o preço não registra o recebimento");

        var higher = await approver.PostAsJsonAsync($"{Base}/{id}/lines/{line}/correct-price", new { unitPrice = 15m, reason = "Reajuste", version = Version(corrected) });

        higher.StatusCode.Should().Be(HttpStatusCode.OK, await higher.Content.ReadAsStringAsync());
        var reapproval = await JsonAsync(higher);
        reapproval.GetProperty("status").GetString().Should().Be("PendingApproval", "125,00 passa do aprovado de 100,00");
        (await GetAsync(receiver, id)).GetProperty("receiveBlocker").GetString().Should().Be("Pedido em reaprovação");
        var blocked = await ReceiveAsync(receiver, reapproval, ReceiptBody(reapproval, null, null, (line, 1m, null)));
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await receiver.PostAsJsonAsync($"{Base}/{id}/lines/{line}/correct-price", new { unitPrice = 8m, reason = "x", version = Version(reapproval) }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await WithDbAsync(db => db.PurchaseOrderPriceCorrections.CountAsync(c => c.OrderId == id))).Should().Be(2);
    }

    /// <summary>CA 17: dois recebimentos simultâneos do mesmo saldo: só um passa; o outro recebe 409 ou 422; o estoque entra uma vez.</summary>
    [Fact]
    public async Task Recebimentos_simultaneos_do_mesmo_saldo_so_um_passa()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (_, first) = await ActorAsync("warehouse");
        var (_, second) = await ActorAsync("warehouse");
        var item = await ItemAsync();
        var order = await ApprovedOrderAsync(owner, buyer, await SupplierAsync(owner), 0m, 0m, "À vista", null, (item, 6m, 10m, null));
        var body = ReceiptBody(order, null, null, (LineOf(order, item), 6m, null));

        var results = await Task.WhenAll(ReceiveAsync(first, order, body), ReceiveAsync(second, order, body));

        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        results.Count(r => r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity).Should().Be(1);
        var orderId = order.GetProperty("id").GetGuid();
        (await WithDbAsync(db => db.StockMovements.AsNoTracking().Where(m => m.ItemId == item).SumAsync(m => m.Quantity))).Should().Be(6m);
        (await WithDbAsync(db => db.GoodsReceipts.CountAsync(r => r.PurchaseOrderId == orderId))).Should().Be(1);
    }

    /// <summary>CA 18: o histórico do recebimento mostra a criação com o usuário; o movimento aponta para o recebimento e o título para o pedido.</summary>
    [Fact]
    public async Task Historico_e_links_do_recebimento()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (_, receiver) = await ActorAsync("warehouse");
        var item = await ItemAsync();
        var order = await ApprovedOrderAsync(owner, buyer, await SupplierAsync(owner), 0m, 0m, "À vista", null, (item, 2m, 10m, null));
        var receipt = await ReceivedAsync(receiver, order, ReceiptBody(order, null, null, (LineOf(order, item), 2m, null)));
        var receiptId = receipt.GetProperty("id").GetGuid();

        var history = await owner.GetFromJsonAsync<System.Text.Json.JsonElement>($"{Receipts}/{receiptId}/history");

        history.GetProperty("items").GetArrayLength().Should().BeGreaterThan(0);
        var movement = (await MovementsAsync(receiptId)).Single();
        movement.SourceNumber.Should().Be(receipt.GetProperty("number").GetString());
        var title = (await PayablesAsync(BemAqui.SharedKernel.DocumentOrigin.GoodsReceipt, receiptId)).Single();
        title.Description.Should().Contain(order.GetProperty("number").GetString()!).And.Contain(receipt.GetProperty("number").GetString()!);
    }
}
