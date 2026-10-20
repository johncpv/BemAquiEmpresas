using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>FIN-06a: aging por cliente (dataset fixo) e aviso diário dos vencidos de ontem. Banco desta classe.</summary>
public partial class AgingTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";

    private static DateOnly Today => BusinessTime.Today(TimeProvider.System);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<AsyncServiceScope> ScopeAsync(Tenant? tenant = null)
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync((tenant ?? tenancy.TenantA).Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task<T> WithDbAsync<T>(Func<TenantDbContext, Task<T>> action, Tenant? tenant = null)
    {
        await using var scope = await ScopeAsync(tenant);
        return await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    private async Task<(Guid Id, HttpClient Client)> ActorAsync(string? seedKey, Tenant? tenant = null)
    {
        var t = tenant ?? tenancy.TenantA;
        var (id, stamp) = await tenancy.SeedUserAsync(t, "g" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            await using var scope = await ScopeAsync(t);
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync() });
            await db.SaveChangesAsync();
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(t.Id, Host, userId: id, stamp: stamp));
        return (id, client);
    }

    private static async Task<Guid> CustomerAsync(HttpClient owner, string name)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = (string?)null, legalName = name, roles = new[] { "Customer" } });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    private static Receivable Rec(Guid party, Guid account, decimal amount, DateOnly? due, ReceivableStatus status = ReceivableStatus.Open, DocumentOrigin origin = DocumentOrigin.Manual) => new()
    {
        Number = "A-" + Guid.NewGuid().ToString("N")[..10], PartyId = party, AccountId = account, Description = "Venda", Amount = amount, IssueDate = Today.AddDays(-200), DueDate = due,
        Status = status, Origin = origin, CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Aging_por_cliente_com_o_dataset_fixo()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (alfa, beta, gama) = (await CustomerAsync(owner, "Alfa Condomínio"), await CustomerAsync(owner, "Beta Engenharia"), await CustomerAsync(owner, "Gama Shopping"));
        await WithDbAsync(async db =>
        {
            var account = await db.Accounts.Where(a => a.Code == "1.1").Select(a => a.Id).SingleAsync();
            var partial = Rec(alfa, account, 500m, Today.AddDays(-35), ReceivableStatus.PartiallyPaid);
            db.Receivables.AddRange(Rec(alfa, account, 1_000m, Today.AddDays(-5)), partial, Rec(beta, account, 2_000m, Today.AddDays(-70), origin: DocumentOrigin.Opening),
                Rec(gama, account, 400m, Today.AddDays(-120)), Rec(gama, account, 999m, null, ReceivableStatus.Planned), Rec(gama, account, 777m, Today.AddDays(-10), ReceivableStatus.Cancelled),
                Rec(gama, account, 300m, Today.AddDays(5)));
            db.Entry(partial).Property(r => r.PaidAmount).CurrentValue = 200m;
            return await db.SaveChangesAsync();
        });

        var response = await owner.GetAsync("/api/v1/finance/aging");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var aging = await JsonAsync(response);
        aging.GetProperty("total").GetDecimal().Should().Be(3_700m);
        aging.GetProperty("count").GetInt32().Should().Be(4);
        aging.GetProperty("customers").GetInt32().Should().Be(3);
        aging.GetProperty("buckets").EnumerateArray().Select(b => b.GetProperty("total").GetDecimal()).Should().Equal(1_000m, 300m, 2_000m, 400m);
        aging.GetProperty("buckets").EnumerateArray().Select(b => b.GetProperty("label").GetString()).Should().Equal("0–30", "31–60", "61–90", "90+");
        var rows = aging.GetProperty("rows").EnumerateArray().ToList();
        rows.Select(r => r.GetProperty("partyName").GetString()).Should().Equal("Beta Engenharia", "Alfa Condomínio", "Gama Shopping");
        rows[1].GetProperty("total").GetDecimal().Should().Be(1_300m, "parcial conta pelo saldo (CA 3)");
        rows[1].GetProperty("maxDaysLate").GetInt32().Should().Be(35);
        rows[1].GetProperty("buckets").EnumerateArray().Select(b => b.GetDecimal()).Should().Equal(1_000m, 300m, 0m, 0m);

        var byName = (await JsonAsync(await owner.GetAsync("/api/v1/finance/aging?sort=name"))).GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("partyName").GetString());
        byName.Should().Equal("Alfa Condomínio", "Beta Engenharia", "Gama Shopping");
        (await owner.GetAsync("/api/v1/finance/aging?sort=total;drop")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "CA 6");
        var (_, nobody) = await ActorAsync(null);
        (await nobody.GetAsync("/api/v1/finance/aging")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(30, 0)]
    [InlineData(31, 1)]
    [InlineData(60, 1)]
    [InlineData(61, 2)]
    [InlineData(90, 2)]
    [InlineData(91, 3)]
    public void Faixa_pelos_dias_de_atraso(int days, int bucket) => AgingService.BucketOf(days).Should().Be(bucket);
}
