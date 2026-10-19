using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Inventory;

/// <summary>CAD-02a: unidades, categorias, garantia padrão e itens.</summary>
public partial class ItemsTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";

    /// <summary>Itens "com movimento", "com saldo" ou "em uso" (os provedores reais vêm no EST-01, ENG-02 e CAD-02b).</summary>
    private static readonly ConcurrentDictionary<Guid, string> State = new();

    private sealed class FakeUsage : IItemUsageProvider
    {
        public Task<bool> HasMovementAsync(Guid itemId, CancellationToken cancellationToken = default) => Task.FromResult(State.TryGetValue(itemId, out var s) && s == "movimento");

        public Task<bool> HasBalanceAsync(Guid itemId, CancellationToken cancellationToken = default) => Task.FromResult(State.TryGetValue(itemId, out var s) && s == "saldo");

        public Task<string?> UsageAsync(Guid itemId, CancellationToken cancellationToken = default) => Task.FromResult(State.TryGetValue(itemId, out var s) && s == "uso" ? "estruturas de produto" : null);
    }

    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory => _factory ??= tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<IItemUsageProvider, FakeUsage>()));

    private static string Code(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<Guid> CreatedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task<Guid> UnitIdAsync(string code = "UN")
    {
        await using var scope = await ScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<IInventoryDb>().Units.Where(u => u.Code == code).Select(u => u.Id).SingleAsync();
    }

    /// <summary>Corpo do item; <paramref name="extra"/> entra no JSON (ex.: averageCost, para o CA 13).</summary>
    private static Dictionary<string, object?> Item(Guid unitId, string type = "RawMaterial", string? code = null, string? ncm = "84313110", bool lot = false, bool serial = false, int? warranty = null) => new()
    {
        ["type"] = type, ["code"] = code ?? Code("MP"), ["name"] = "Chapa de aço 3 mm", ["unitId"] = unitId, ["categoryId"] = null, ["ncm"] = ncm, ["cest"] = null,
        ["origin"] = 0, ["minStock"] = 10.5m, ["tracksLot"] = lot, ["tracksSerial"] = serial, ["warrantyMonths"] = warranty,
    };

    private async Task<HttpClient> ActorAsync(string? seedKey = null)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "i" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync() });
            await db.SaveChangesAsync();
        }

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return client;
    }
}
