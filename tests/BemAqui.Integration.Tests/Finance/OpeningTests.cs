using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>
/// FIN-00: implantação financeira. A implantação é única por tenant e a conclusão é definitiva, por isso o fluxo inteiro
/// fica num teste só, em ordem; o banco é desta classe (fixture própria).
/// </summary>
public partial class OpeningTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Base = "/api/v1/finance/opening";

    private static DateOnly Today => BusinessTime.Today(TimeProvider.System);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private async Task<T> QueryAsync<T>(Func<TenantDbContext, Task<T>> query)
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        return await query(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    private async Task<HttpClient> ActorAsync(string? seedKey)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "o" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
            await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync() });
            await db.SaveChangesAsync();
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return client;
    }

    private static HttpRequestMessage Post(string url, object body, Guid? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        return request;
    }

    private static async Task<Guid> PartyAsync(HttpClient owner, string role)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = (string?)null, legalName = role + " " + Guid.NewGuid().ToString("N")[..6], roles = new[] { role } });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> BankAsync(HttpClient owner)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/finance/bank-accounts", new { name = "Banco " + Guid.NewGuid().ToString("N")[..6], kind = "Bank", bankCode = "077", agency = "1234-5", account = "12345678-9", openingBalance = (decimal?)null, openingDate = (string?)null, isDefault = false });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    private static object Receivable(Guid party, decimal balance, string? reference = null) =>
        new { partyId = party, description = "Fatura antiga", reference, issueDate = (string?)null, dueDate = Iso(Today.AddDays(-3)), balance, accountId = (Guid?)null, costCenterId = (Guid?)null };

    private static object Payable(Guid party, decimal balance) =>
        new { partyId = party, description = "Nota antiga", reference = "FF-12", dueDate = Iso(Today.AddDays(5)), balance, accountId = (Guid?)null, costCenterId = (Guid?)null, paymentDetails = (object?)null };

    [Fact]
    public async Task Sem_modulo_ou_grupo_a_implantacao_responde_403()
    {
        var nobody = await ActorAsync(null);
        var denied = await nobody.GetAsync(Base);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(denied)).Should().Be("forbidden");
        (await nobody.SendAsync(Post(Base + "/receivables", new { lines = Array.Empty<object>() }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
