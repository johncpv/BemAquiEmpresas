using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Inventory;

public partial class ItemsTests
{
    [Fact]
    public async Task Lista_filtra_por_tipo_e_busca_sem_acento_e_a_topbar_traz_ate_5()
    {
        var warehouse = await ActorAsync("warehouse");
        var un = await UnitIdAsync();
        var tag = Guid.NewGuid().ToString("N")[..5];
        foreach (var n in Enumerable.Range(1, 6))
        {
            var body = Item(un, type: "Component", code: $"POL-{tag}-{n}");
            body["name"] = $"Polia de tração {tag} {n}";
            await CreatedAsync(await warehouse.PostAsJsonAsync("/api/v1/inventory/items", body));
        }

        var list = await JsonAsync(await warehouse.GetAsync($"/api/v1/inventory/items?q=tracao%20{tag}&type=Component"));
        list.GetProperty("total").GetInt32().Should().Be(6, "busca sem acento no nome");
        list.GetProperty("items")[0].GetProperty("unitCode").GetString().Should().Be("UN");
        (await JsonAsync(await warehouse.GetAsync($"/api/v1/inventory/items?q={tag}&type=Finished"))).GetProperty("total").GetInt32().Should().Be(0);

        var hits = await JsonAsync(await warehouse.GetAsync($"/api/v1/inventory/items/search?q=pol-{tag}"));
        hits.GetArrayLength().Should().Be(5, "a topbar mostra até 5 (CA 20)");
        (await JsonAsync(await warehouse.GetAsync("/api/v1/inventory/items/search?q=p"))).GetArrayLength().Should().Be(0, "a partir de 2 caracteres");
        var commercial = await ActorAsync("commercial");
        (await commercial.GetAsync($"/api/v1/inventory/items/search?q={tag}")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "sem inventory.view, nenhum item (ADR 0010 item 9)");
    }

    [Fact]
    public async Task Tres_camadas_em_todo_endpoint_de_itens()
    {
        var nobody = await ActorAsync();
        var noGroup = await nobody.GetAsync("/api/v1/inventory/items");
        noGroup.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(noGroup)).GetProperty("code").GetString().Should().Be("forbidden");
        var finance = await ActorAsync("finance");
        (await finance.GetAsync("/api/v1/inventory/items")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await finance.PostAsJsonAsync("/api/v1/inventory/items", Item(await UnitIdAsync()))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await finance.PostAsJsonAsync("/api/v1/inventory/units", new { code = "XX", name = "X" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var endpoints = tenancy.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1/inventory", StringComparison.Ordinal) == true).ToList();
        endpoints.Should().HaveCountGreaterThan(28);
        foreach (var endpoint in endpoints)
        {
            endpoint.Metadata.GetMetadata<RequiresModuleMetadata>()!.ModuleCode.Should().Be("inventory", endpoint.DisplayName);
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().Contain(a => a.Policy != null && a.Policy.StartsWith(ModuleEndpoints.PermissionPolicyPrefix + "inventory.", StringComparison.Ordinal), endpoint.DisplayName);
        }
    }
}
