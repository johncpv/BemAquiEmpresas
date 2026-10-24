using System.Net;
using System.Net.Http.Json;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// CMP-02 CA 13: três camadas (módulo <c>purchasing</c> não liberado, sem grupo, sem a ação), o Almoxarifado do seed (só <c>view</c> +
/// <c>receive</c>) consulta mas não cota, e o IDOR no fornecedor da cotação. Banco desta classe (o teste desliga o módulo).
/// </summary>
public class RfqSecurityTests(TenancyFixture tenancy) : RfqTestBase(tenancy), IClassFixture<TenancyFixture>
{
    [Fact]
    public async Task Tres_camadas_e_o_Almoxarifado_consulta_mas_nao_cota()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (_, warehouse) = await ActorAsync("warehouse");
        var (_, nobody) = await ActorAsync(null);
        var (_, viewer) = await ActorAsync("purchasing:view");
        var (_, creator) = await ActorAsync("purchasing:view,create");
        var supplier = await SupplierAsync(owner);
        var item = await ItemAsync();
        var rfq = await CreateTypedRfqAsync(buyer, [(item, 1m)], [supplier]);
        var body = new { requestLineIds = Array.Empty<Guid>(), lines = new[] { new { itemId = item, quantity = 1m, unitId = (Guid?)null } }, supplierIds = new[] { supplier } };

        (await warehouse.GetAsync($"{Rfqs}/{Id(rfq)}")).StatusCode.Should().Be(HttpStatusCode.OK, "o Almoxarifado consulta");
        (await warehouse.SendAsync(Post(Rfqs, body))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "mas não cota");
        (await RespondRawAsync(warehouse, rfq, SupplierRow(rfq, supplier), [new Quote(LineOf(rfq, item), 1m)])).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var none = await nobody.GetAsync(Rfqs);
        none.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(none)).Should().Be("forbidden");
        (await viewer.SendAsync(Post(Rfqs, body))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "criar exige purchasing.create");
        (await creator.PostAsJsonAsync($"{Rfqs}/{Id(rfq)}/close", new { selections = Array.Empty<object>(), version = Version(rfq) }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "concluir exige purchasing.edit");
        (await viewer.SendAsync(Post($"{Rfqs}/{Id(rfq)}/purchase-orders", new { version = Version(rfq) }))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "gerar exige purchasing.create");
        (await viewer.PostAsync($"{Rfqs}/{Id(rfq)}/purchase-orders/submit", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "enviar exige a permissão do envio da CMP-03a");
        var detail = await GetRfqAsync(viewer, rfq);
        detail.GetProperty("canEdit").GetBoolean().Should().BeFalse("o botão some para quem não tem a ação");

        await SetReleasedAsync(false);
        try
        {
            var blocked = await buyer.GetAsync(Rfqs);
            blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(blocked)).Should().Be("module_not_released");
        }
        finally
        {
            await SetReleasedAsync(true);
        }
    }

    /// <summary>CA 13 (IDOR): o fornecedor de outra cotação na URL dá 404; outra cotação inexistente também.</summary>
    [Fact]
    public async Task Fornecedor_de_outra_cotacao_na_URL_da_404()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (alfa, beta) = (await SupplierAsync(owner), await SupplierAsync(owner));
        var item = await ItemAsync();
        var mine = await CreateTypedRfqAsync(buyer, [(item, 1m)], [alfa, beta]);
        var other = await CreateTypedRfqAsync(buyer, [(await ItemAsync(), 1m)], [alfa]);

        (await buyer.PostAsJsonAsync($"{Rfqs}/{Id(mine)}/suppliers/{SupplierRow(other, alfa)}/decline", new { version = Version(mine) })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await buyer.PostAsJsonAsync($"{Rfqs}/{Id(mine)}/suppliers/{SupplierRow(other, alfa)}/remove", new { version = Version(mine) })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await buyer.GetAsync($"{Rfqs}/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        mine = await RespondAsync(buyer, mine, SupplierRow(mine, alfa), [new Quote(LineOf(mine, item), 5m)]);
        var close = await CloseRawAsync(buyer, mine,new Pick(LineOf(other, other.GetProperty("lines")[0].GetProperty("itemId").GetGuid()), SupplierRow(mine, alfa)));
        close.StatusCode.Should().Be(HttpStatusCode.NotFound, "linha de outra cotação");
    }
}
