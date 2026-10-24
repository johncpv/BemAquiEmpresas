using System.Net;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// CMP-03b CA 10: as três camadas (módulo não liberado, sem grupo, sem a ação) no PDF, nos documentos, no diálogo e no envio, e o IDOR
/// do envio lido por outro pedido. Banco desta classe (o teste do módulo desliga o Purchasing do tenant).
/// </summary>
public class PurchaseOrderDispatchSecurityTests(TenancyFixture tenancy) : PurchaseOrderDispatchTestBase(tenancy), IClassFixture<TenancyFixture>
{
    [Fact]
    public async Task Tres_camadas_e_IDOR_do_envio()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (_, viewer) = await ActorAsync("purchasing:view");
        var (_, nobody) = await ActorAsync(null);
        var supplier = await SupplierWithContactAsync(owner);
        var order = await ApprovedAsync(owner, buyer, supplier);
        var other = await ApprovedAsync(owner, buyer, supplier);
        var sent = await SendAsync(buyer, order, Dispatch(order, "compras@gama.com.br"));
        sent.StatusCode.Should().Be(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync());
        var documents = await JsonAsync(await buyer.GetAsync($"{Base}/{Id(order)}/documents"));
        var dispatchId = documents.GetProperty("dispatches")[0].GetProperty("id").GetGuid();

        // Só purchasing.view: baixa o PDF e vê os documentos; não abre o diálogo nem envia.
        (await viewer.GetAsync($"{Base}/{Id(order)}/pdf")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.GetAsync($"{Base}/{Id(order)}/documents")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.GetAsync($"{Base}/{Id(order)}/dispatches/{dispatchId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.GetAsync($"{Base}/{Id(other)}/dispatch-draft")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(viewer, other, Dispatch(other, "compras@gama.com.br"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Sem grupo: 403 forbidden em tudo.
        foreach (var url in new[] { $"{Base}/{Id(order)}/pdf", $"{Base}/{Id(order)}/documents", $"{Base}/{Id(order)}/dispatches/{dispatchId}" })
        {
            var denied = await nobody.GetAsync(url);
            denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
            Code(await JsonAsync(denied)).Should().Be("forbidden");
        }

        // IDOR: o envio de um pedido lido pela URL de outro é 404.
        (await buyer.GetAsync($"{Base}/{Id(other)}/dispatches/{dispatchId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await buyer.GetAsync($"{Base}/{Id(order)}/dispatches/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Módulo não liberado: 403 module_not_released.
        await SetReleasedAsync(false);
        try
        {
            var blocked = await buyer.GetAsync($"{Base}/{Id(order)}/pdf");
            blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(blocked)).Should().Be("module_not_released");
            Code(await JsonAsync(await SendAsync(buyer, other, Dispatch(other, "compras@gama.com.br")))).Should().Be("module_not_released");
        }
        finally
        {
            await SetReleasedAsync(true);
        }
    }
}
