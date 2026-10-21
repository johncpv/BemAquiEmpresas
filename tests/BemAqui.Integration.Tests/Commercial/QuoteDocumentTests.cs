using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Attachments;
using BemAqui.Integration.Tests.Observability;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Commercial;

/// <summary>COM-03: PDF do orçamento (sem custo, só Archivo) e envio por e-mail com anexo, cópia e limite. Banco desta classe.</summary>
public partial class QuoteDocumentTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>, IDisposable
{
    private const string Host = "a.localhost";
    private const string Base = "/api/v1/quotes";

    private readonly FakeScanner _scanner = new();
    private readonly CapturingTransport _transport = new();
    private readonly string _storage = Path.Combine(Path.GetTempPath(), "bqe-com03-" + Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory => _factory ??= tenancy.Factory.WithWebHostBuilder(b => b
        .UseSetting("Storage:Root", _storage)
        .ConfigureTestServices(s => { s.AddSingleton<IVirusScanner>(_scanner); s.AddSingleton<IEmailTransport>(_transport); }));

    public void Dispose()
    {
        _factory?.Dispose();
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, true);
        }

        GC.SuppressFinalize(this);
    }

    private static DateOnly Today => BusinessTime.Today(TimeProvider.System);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static uint Version(JsonElement e) => e.GetProperty("version").GetUInt32();

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task<T> WithDbAsync<T>(Func<TenantDbContext, Task<T>> action)
    {
        await using var scope = await ScopeAsync();
        return await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    private async Task<HttpClient> OwnerAsync()
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "d" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = await db.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync() });
            await db.SaveChangesAsync();
        }

        var client = Factory.CreateClient();
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

    /// <summary>Cliente com contato de cobrança e um orçamento com item de custo R$ 600,00 e preço R$ 1.400,00.</summary>
    private static async Task<(Guid Quote, JsonElement Detail)> QuoteAsync(HttpClient owner)
    {
        var party = await owner.PostAsJsonAsync("/api/v1/parties", new
        {
            kind = "PJ", document = (string?)null, legalName = "Aurora Condomínio " + Guid.NewGuid().ToString("N")[..4], roles = new[] { "Customer" },
            mainContact = new { name = "Marta Síndica", role = "Síndica", email = "marta@aurora.com.br", phone = (string?)null, receivesBilling = true },
        });
        party.StatusCode.Should().Be(HttpStatusCode.Created, await party.Content.ReadAsStringAsync());
        var partyId = (await JsonAsync(party)).GetProperty("id").GetGuid();
        var quote = await JsonAsync(await owner.SendAsync(Post(Base, new { partyId, contactId = (Guid?)null, siteAddressId = (Guid?)null })));
        var id = quote.GetProperty("id").GetGuid();
        quote = await JsonAsync(await owner.PostAsJsonAsync($"{Base}/{id}/items", new { kind = "Equipment", itemId = (Guid?)null, description = "Elevador 6 paradas", quantity = 1m, unitPrice = (decimal?)null, resetPrice = false, version = Version(quote) }));
        var itemId = quote.GetProperty("items")[0].GetProperty("id").GetGuid();
        var costs = new { lines = new object[] { new { costType = "Freight", description = "Frete especial", itemId = (Guid?)null, workCenterId = (Guid?)null, positionId = (Guid?)null, partyId = (Guid?)null, quantity = 1m, unitCost = 600m } }, pct = (decimal?)null, version = Version(quote) };
        (await owner.PutAsJsonAsync($"{Base}/{id}/items/{itemId}", new { kind = "Equipment", itemId = (Guid?)null, description = "Elevador 6 paradas", quantity = 1m, unitPrice = 1_400m, resetPrice = false, version = Version(quote) })).EnsureSuccessStatusCode();
        quote = await JsonAsync(await owner.GetAsync($"{Base}/{id}"));
        (await owner.PutAsJsonAsync($"{Base}/{id}/items/{itemId}/costs", costs with { version = Version(quote) })).EnsureSuccessStatusCode();
        return (id, await JsonAsync(await owner.GetAsync($"{Base}/{id}")));
    }
}
