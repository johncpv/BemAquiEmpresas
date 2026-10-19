using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Inventory;

/// <summary>CAD-03: locais de estoque e a ação <c>delete</c> em inventory e production.</summary>
public partial class LocationsTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";

    /// <summary>Locais "com saldo" ou "em uso" (o provedor real vem no EST-01).</summary>
    private static readonly ConcurrentDictionary<Guid, string> State = new();

    private sealed class FakeUsage : ILocationUsageProvider
    {
        public Task<bool> HasBalanceAsync(Guid locationId, CancellationToken cancellationToken = default) => Task.FromResult(State.TryGetValue(locationId, out var s) && s == "saldo");

        public Task<string?> UsageAsync(Guid locationId, CancellationToken cancellationToken = default) => Task.FromResult(State.TryGetValue(locationId, out var s) && s == "uso" ? "movimentos" : null);
    }

    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory => _factory ??= tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<ILocationUsageProvider, FakeUsage>()));

    private static string Code(string prefix) => prefix + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

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

    private async Task<HttpClient> ActorAsync(string? seedKey = null)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "l" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
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

    [Fact]
    public async Task Delete_entra_no_catalogo_sem_ir_para_os_grupos_do_seed()
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
        (await db.CatalogModuleActions.Where(a => a.Action == "delete" && (a.ModuleCode == "inventory" || a.ModuleCode == "production")).CountAsync()).Should().Be(2);
        (await db.GroupPermissions.Where(p => p.Action == "delete" && (p.ModuleCode == "inventory" || p.ModuleCode == "production"))
            .Join(db.Groups.Where(g => g.SeedKey != null && g.SeedKey != Group.OwnerSeedKey), p => p.GroupId, g => g.Id, (p, g) => g.Name).ToListAsync())
            .Should().BeEmpty("nenhum grupo do seed recebe delete (CA 1)");
    }

    [Fact]
    public async Task Tres_camadas_em_todo_endpoint_de_estoque()
    {
        var nobody = await ActorAsync();
        var noGroup = await nobody.GetAsync("/api/v1/inventory/locations");
        noGroup.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(noGroup)).GetProperty("code").GetString().Should().Be("forbidden");
        var finance = await ActorAsync("finance");
        (await finance.GetAsync("/api/v1/inventory/locations")).StatusCode.Should().Be(HttpStatusCode.OK, "o Financeiro vê o estoque pelo seed");
        (await finance.PostAsJsonAsync("/api/v1/inventory/locations", new { code = Code("NAO"), name = "Sem permissão" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var endpoints = tenancy.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1/inventory", StringComparison.Ordinal) == true).ToList();
        endpoints.Should().HaveCountGreaterThan(6);
        foreach (var endpoint in endpoints)
        {
            endpoint.Metadata.GetMetadata<RequiresModuleMetadata>()!.ModuleCode.Should().Be("inventory", endpoint.DisplayName);
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().Contain(a => a.Policy != null && a.Policy.StartsWith(ModuleEndpoints.PermissionPolicyPrefix + "inventory.", StringComparison.Ordinal), endpoint.DisplayName);
        }
    }
}
