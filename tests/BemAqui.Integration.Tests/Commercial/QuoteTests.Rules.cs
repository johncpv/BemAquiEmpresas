using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteTests
{
    /// <summary>Grupo só com <c>quotes.view</c> e <c>quotes.edit</c>, sem <c>quotes.view_cost</c>.</summary>
    private async Task<HttpClient> NoCostActorAsync()
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "c" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        await using (var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info))
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            var group = new Group { Name = "Vendas sem custo " + Guid.NewGuid().ToString("N")[..4], Description = "Sem custo" };
            db.Groups.Add(group);
            db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = "quotes", Enabled = true });
            db.GroupPermissions.AddRange(new GroupGrant { GroupId = group.Id, ModuleCode = "quotes", Action = "view" }, new GroupGrant { GroupId = group.Id, ModuleCode = "quotes", Action = "edit" });
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = group.Id });
            await db.SaveChangesAsync();
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return client;
    }

    [Fact]
    public async Task Sem_ver_custo_o_DTO_nao_tem_custo_e_a_composicao_responde_403()
    {
        (await WithDbAsync(db => db.GroupPermissions.AnyAsync(p => p.ModuleCode == "quotes" && p.Action == "view_cost" && db.Groups.Any(g => g.Id == p.GroupId && g.SeedKey == "commercial"))))
            .Should().BeTrue("seed patch view-cost-default-permissions (CA 1)");
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var seller = await NoCostActorAsync();
        var customer = await CustomerAsync(owner);
        var quote = await JsonAsync(await owner.SendAsync(Post(Base, new { partyId = customer, contactId = (Guid?)null, siteAddressId = (Guid?)null })));
        var id = quote.GetProperty("id").GetGuid();
        quote = await JsonAsync(await owner.PostAsJsonAsync($"{Base}/{id}/items", Item("Plataforma", 1m, Version(quote), price: 5_000m)));
        var itemId = quote.GetProperty("items")[0].GetProperty("id").GetGuid();

        var body = await (await seller.GetAsync($"{Base}/{id}")).Content.ReadAsStringAsync();
        body.Should().NotContainAny("totalCost", "unitCost", "marginPct", "\"pct\"");
        (await seller.GetAsync($"{Base}/{id}/costs")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await seller.GetAsync($"{Base}/costs?ids={id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var noCosts = new { lines = Array.Empty<object>(), pct = (decimal?)null, version = Version(quote) };
        (await seller.PutAsJsonAsync($"{Base}/{id}/items/{itemId}/costs", noCosts)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "CA 3");
        var priced = await seller.PutAsJsonAsync($"{Base}/{id}/items/{itemId}", Item("Plataforma", 3m, Version(quote), price: 4_500m));
        priced.StatusCode.Should().Be(HttpStatusCode.OK, "muda quantidade e preço sem ver custo (CA 3)");

        // CA 21: versão antiga, 409; CA 20: item de outro orçamento, 404.
        var stale = await owner.PutAsJsonAsync($"{Base}/{id}", new { contactId = (Guid?)null, siteAddressId = (Guid?)null, deliveryDays = 30, notes = "x", version = Version(quote) });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(stale)).Should().Be("concurrency_conflict");
        var other = await JsonAsync(await owner.SendAsync(Post(Base, new { partyId = customer, contactId = (Guid?)null, siteAddressId = (Guid?)null })));
        (await owner.PutAsJsonAsync($"{Base}/{other.GetProperty("id").GetGuid()}/items/{itemId}", Item("x", 1m, Version(other)))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // CA 18: o rascunho rev. 0 é excluído logicamente.
        (await owner.DeleteAsync($"{Base}/{other.GetProperty("id").GetGuid()}?version={Version(other)}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.GetAsync($"{Base}/{other.GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
