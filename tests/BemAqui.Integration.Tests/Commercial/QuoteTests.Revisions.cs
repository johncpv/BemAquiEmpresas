using System.Net;
using System.Net.Http.Json;
using BemAqui.Commercial.Application;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteTests
{
    [Fact]
    public async Task Revisao_prorrogacao_expiracao_e_aviso()
    {
        var (ownerId, owner) = await ActorAsync(Group.OwnerSeedKey);
        var customer = await CustomerAsync(owner);
        var quote = await JsonAsync(await owner.SendAsync(Post(Base, new { partyId = customer, contactId = (Guid?)null, siteAddressId = (Guid?)null })));
        var id = quote.GetProperty("id").GetGuid();
        quote = await JsonAsync(await owner.PostAsJsonAsync($"{Base}/{id}/items", Item("Plataforma", 1m, Version(quote), price: 9_000m)));
        (await owner.SendAsync(Post($"{Base}/{id}/revisions", new { version = Version(quote) }))).StatusCode.Should().Be(HttpStatusCode.Conflict, "rascunho não ganha revisão (CA 3)");
        quote = await JsonAsync(await owner.PostAsJsonAsync($"{Base}/{id}/send", new { version = Version(quote) }));

        // CA 6 e 7: prorrogar só muda a data.
        (await owner.PostAsJsonAsync($"{Base}/{id}/extend-validity", new { validUntil = Iso(Today.AddDays(5)), version = Version(quote) })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        quote = await JsonAsync(await owner.PostAsJsonAsync($"{Base}/{id}/extend-validity", new { validUntil = Iso(Today.AddDays(30)), version = Version(quote) }));
        quote.GetProperty("validUntil").GetString().Should().Be(Iso(Today.AddDays(30)));

        // CA 1 e 5: nova revisão; duas ao mesmo tempo, só uma nasce.
        var results = await Task.WhenAll(owner.SendAsync(Post($"{Base}/{id}/revisions", new { version = Version(quote) })), owner.SendAsync(Post($"{Base}/{id}/revisions", new { version = Version(quote) })));
        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        var rev1 = await JsonAsync(results.Single(r => r.StatusCode == HttpStatusCode.Created));
        rev1.GetProperty("revision").GetInt32().Should().Be(1);
        rev1.GetProperty("number").GetString().Should().Be(quote.GetProperty("number").GetString());
        rev1.GetProperty("totalPrice").GetDecimal().Should().Be(9_000m);
        var old = await JsonAsync(await owner.GetAsync($"{Base}/{id}"));
        old.GetProperty("status").GetString().Should().Be("Superseded");

        // CA 4: a antiga não muda mais.
        (await (await owner.PostAsJsonAsync($"{Base}/{id}/extend-validity", new { validUntil = Iso(Today.AddDays(40)), version = Version(old) })).Content.ReadAsStringAsync())
            .Should().Contain("Só a revisão mais recente");
        var revisions = await JsonAsync(await owner.GetAsync($"{Base}/{rev1.GetProperty("id").GetGuid()}/revisions"));
        revisions.GetArrayLength().Should().Be(2);
        (await owner.GetAsync($"{Base}/{rev1.GetProperty("id").GetGuid()}/revisions/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "IDOR (CA 14)");

        // CA 10 e 11: o job expira o vencido e avisa uma vez; o que vence em 3 dias também avisa uma vez.
        var rev1Id = rev1.GetProperty("id").GetGuid();
        rev1 = await JsonAsync(await owner.PostAsJsonAsync($"{Base}/{rev1Id}/send", new { version = Version(rev1) }));
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE commercial.quotes SET valid_until = {Today.AddDays(-1)} WHERE id = {rev1Id}"));
        async Task RunAsync()
        {
            var info = (await tenancy.Factory.Services.GetRequiredService<BemAqui.BuildingBlocks.Tenancy.ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
            await using var scope = await tenancy.Factory.Services.GetRequiredService<BemAqui.BuildingBlocks.Tenancy.ITenantScopeFactory>().CreateScopeAsync(info);
            await scope.ServiceProvider.GetRequiredService<QuoteExpiry>().RunAsync();
        }

        await RunAsync();
        await RunAsync();
        (await JsonAsync(await owner.GetAsync($"{Base}/{rev1Id}"))).GetProperty("status").GetString().Should().Be("Expired");
        (await WithDbAsync(db => db.Notifications.CountAsync(n => n.UserId == ownerId && n.Title.Contains("expirou")))).Should().Be(1);
        var expired = await JsonAsync(await owner.GetAsync($"{Base}/{rev1Id}"));
        (await owner.PostAsJsonAsync($"{Base}/{rev1Id}/extend-validity", new { validUntil = Iso(Today.AddDays(10)), version = Version(expired) })).StatusCode.Should().Be(HttpStatusCode.Conflict, "CA 7");
        var reopened = await owner.SendAsync(Post($"{Base}/{rev1Id}/revisions", new { version = Version(expired) }));
        reopened.StatusCode.Should().Be(HttpStatusCode.Created, "reabrir com nova revisão (CA 2)");
        (await JsonAsync(await owner.GetAsync($"{Base}/{rev1Id}"))).GetProperty("status").GetString().Should().Be("Expired", "a expirada continua expirada");
    }
}
