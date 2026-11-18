using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Catalog.Contracts;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Catalog;

/// <summary>
/// CAT-01: catálogo de produtos. O tenant A recebe o perfil Comércio uma vez por classe (o catálogo é oculto para a fábrica); o tenant B
/// fica só com a Fábrica, como a Konebras. Os testes compartilham o tenant A: nomes e códigos únicos por teste.
/// </summary>
public partial class CatalogTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Products = "/api/v1/catalog/products";

    /// <summary>Produtos "já vendidos" (o provedor real vem na VND-01).</summary>
    private static readonly ConcurrentDictionary<Guid, bool> Sold = new();

    private sealed class FakeSales : IProductUsageProvider
    {
        public Task<string?> UsageAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult(Sold.ContainsKey(productId) ? "vendas" : null);
    }

    private WebApplicationFactory<Program>? _factory;
    private bool _retail;

    private WebApplicationFactory<Program> Factory => _factory ??= tenancy.Derive(b => b.ConfigureTestServices(s => s.AddScoped<IProductUsageProvider, FakeSales>()));

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private static string Unique(string name) => name + " " + Guid.NewGuid().ToString("N")[..6];

    /// <summary>GTIN-13 válido e único: 12 dígitos aleatórios e o verificador.</summary>
    private static string NewGtin(int length = 13)
    {
        var body = string.Concat(Enumerable.Range(0, length - 1).Select(_ => Random.Shared.Next(0, 10).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return body + BemAqui.Catalog.Domain.GtinRules.CheckDigit(body).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>PLU de 6 dígitos único o bastante para a classe.</summary>
    private static string NewPlu() => Random.Shared.Next(100_000, 999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Perfil Comércio no tenant A (uma vez): o catálogo passa a ser liberado (plano completo + perfil).</summary>
    private async Task RetailAsync()
    {
        if (_retail)
        {
            return;
        }

        await using var scope = tenancy.Factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<BemAqui.Platform.Application.Modules.TenantModuleService>();
        (await service.ApplyProfileAsync(tenancy.TenantA.Id, BusinessProfileCatalog.Retail, "counter", null)).IsSuccess.Should().BeTrue();
        scope.ServiceProvider.GetRequiredService<ITenantResolver>().Invalidate();
        Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
        _retail = true;
    }

    private async Task<AsyncServiceScope> ScopeAsync(Tenant? tenant = null)
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync((tenant ?? tenancy.TenantA).Id))!;
        return await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    /// <summary>
    /// Usuário do tenant A no Proprietário (<c>owner</c>) ou num grupo só do catálogo com as ações dadas ("view,create,edit"). Outro tenant
    /// (o B, só fábrica) com <paramref name="tenant"/>.
    /// </summary>
    private async Task<(Guid Id, HttpClient Client)> ActorAsync(string actions, Tenant? tenant = null, string host = Host)
    {
        await RetailAsync();
        var target = tenant ?? tenancy.TenantA;
        var (id, stamp) = await tenancy.SeedUserAsync(target, "c" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using (var scope = await ScopeAsync(target))
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            Guid groupId;
            if (actions == "owner")
            {
                groupId = await db.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync();
            }
            else
            {
                var group = new Group { Name = "Catálogo " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
                db.Groups.Add(group);
                db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = "catalog", Enabled = true });
                db.GroupPermissions.AddRange(actions.Split(',').Select(a => new GroupGrant { GroupId = group.Id, ModuleCode = "catalog", Action = a }));
                groupId = group.Id;
            }

            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = groupId });
            await db.SaveChangesAsync();
        }

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(target.Id, host, userId: id, stamp: stamp));
        return (id, client);
    }

    /// <summary>Uma unidade ativa do seed (UN).</summary>
    private static async Task<Guid> UnitAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/catalog/units")).EnumerateArray().First(u => u.GetProperty("code").GetString() == "UN").GetProperty("id").GetGuid();

    private static object NewProduct(string name, Guid unitId, decimal? price = null, string? gtin = null, decimal pack = 1m, string? plu = null, string? ncm = null) => new
    {
        saleName = name, categoryId = (Guid?)null, itemId = (Guid?)null, newItem = new { code = (string?)null, unitId, ncm }, plu, isWeighable = false, sellablePos = true,
        sellableOnline = false, price, barcodes = gtin is null ? Array.Empty<object>() : [new { gtin, packQuantity = pack }],
    };

    private static async Task<JsonElement> CreatedAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<T> QueryAsync<T>(Func<BemAqui.Host.Persistence.TenantDbContext, Task<T>> query)
    {
        await using var scope = await ScopeAsync();
        return await query(scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>());
    }
}
