using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Inventory;

/// <summary>
/// EST-05: inventário por local com PostgreSQL real (gera ajuste de estoque, Testcontainers obrigatório). O bloqueio do local é o real
/// (<see cref="InventoryCountLock"/>); o reservado vem da reconciliação da EST-02 sobre fakes da BOM liberada e da OS. Banco desta
/// classe; cada teste cria os próprios itens e locais e filtra pelos ids que criou.
/// </summary>
public partial class InventoryCountTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Base = "/api/v1/inventory";
    private const string Counts = Base + "/inventory-counts";
    private const string Adjustments = Base + "/adjustments";

    private static readonly ConcurrentDictionary<Guid, (ProductionOrderState Status, Guid ProjectId, string Number)> Orders = new();

    private static readonly ConcurrentDictionary<Guid, ReleasedBom> Boms = new();

    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory => _factory ??= tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
    {
        s.AddScoped<IProductionOrders, FakeOrders>();
        s.AddScoped<IReleasedBomSource, FakeBoms>();
    }));

    private static string Unique(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private static string Detail(JsonElement problem) => problem.GetProperty("detail").GetString()!;

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK)
    {
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    private static HttpRequestMessage Command(string url, object body, Guid? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        return request;
    }

    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private static uint Version(JsonElement element) => element.GetProperty("version").GetUInt32();

    private static object Open(Guid location, bool withMovement = false) => new { locationId = location, scope = new { includeZeroWithMovement = withMovement, categoryId = (Guid?)null, itemType = (string?)null } };

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

    /// <summary>
    /// Usuário num grupo do seed (<c>owner</c>, <c>warehouse</c>), num grupo só de consulta (<c>view:inventory</c>) ou sem grupo (null).
    /// Token direto (<c>TestTokens</c>): quem tem <c>inventory.approve</c> não passa pelo login com 2FA.
    /// </summary>
    private async Task<(Guid Id, HttpClient Client)> ActorAsync(string? seedKey)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "i" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            Guid groupId;
            if (seedKey.StartsWith("view:", StringComparison.Ordinal))
            {
                var group = new Group { Name = "Consulta " + Guid.NewGuid().ToString("N")[..6], Description = "Só vê" };
                db.Groups.Add(group);
                db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = seedKey[5..], Enabled = true });
                db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = seedKey[5..], Action = "view" });
                groupId = group.Id;
            }
            else
            {
                groupId = await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync();
            }

            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = groupId });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([id]);
        }

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return (id, client);
    }

    private async Task<Guid> ItemAsync(bool tracksLot = false)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var unit = await db.Units.Where(u => u.Code == "UN").Select(u => u.Id).SingleAsync();
        var item = new Item { Type = ItemType.RawMaterial, Code = Unique("MP"), Name = "Polia " + Guid.NewGuid().ToString("N")[..4], UnitId = unit, Ncm = "84313110", TracksLot = tracksLot, CreatedAt = DateTimeOffset.UtcNow };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    private async Task<Guid> LocationAsync()
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var location = new Location { Code = Unique("L"), Name = "Local " + Guid.NewGuid().ToString("N")[..4], CreatedAt = DateTimeOffset.UtcNow };
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        return location.Id;
    }

    private Task<Guid> DefaultLocationAsync() => WithDbAsync(db => db.Locations.Where(l => l.IsDefault).Select(l => l.Id).SingleAsync());

    private async Task<Result<StockMovementResult>> PostAsync(StockPosting posting)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var result = await scope.ServiceProvider.GetRequiredService<IStockMovements>().PostAsync(posting);
        if (result.IsSuccess)
        {
            await transaction.CommitAsync();
        }

        return result;
    }

    private async Task MoveAsync(Guid item, Guid location, decimal quantity, decimal cost = 10m)
    {
        var posting = quantity > 0
            ? new StockPosting(StockMovementType.Receipt, item, location, null, quantity, cost, StockSources.GoodsReceipt, Guid.NewGuid())
            : new StockPosting(StockMovementType.Issue, item, location, null, quantity, null, StockSources.Requisition, Guid.NewGuid());
        var result = await PostAsync(posting);
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
    }

    /// <summary>Reserva <paramref name="quantity"/> do item para uma OS nova, com a BOM liberada em <paramref name="releasedAt"/>.</summary>
    private async Task<Guid> ReserveAsync(Guid item, decimal quantity, DateTimeOffset releasedAt)
    {
        var order = Guid.CreateVersion7();
        Orders[order] = (ProductionOrderState.Open, Guid.CreateVersion7(), "OS-2026-" + Random.Shared.Next(1000, 9999).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Boms[order] = new ReleasedBom(Guid.CreateVersion7(), 1, releasedAt, [new ReleasedBomLine(Guid.CreateVersion7(), item, quantity)]);
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await scope.ServiceProvider.GetRequiredService<StockReservationService>().ReconcileOrderAsync(order, "Evento BomReleased · teste do inventário");
        await transaction.CommitAsync();
        return order;
    }

    private Task<(decimal OnHand, decimal Reserved)> StateAsync(Guid item, Guid location) => WithDbAsync(async db =>
    {
        var position = await db.StockPositions.AsNoTracking().Where(p => p.ItemId == item && p.LocationId == location).Select(p => new { p.QuantityOnHand, p.QuantityReserved }).FirstOrDefaultAsync();
        return (position?.QuantityOnHand ?? 0m, position?.QuantityReserved ?? 0m);
    });

    /// <summary>A linha vigente do item na contagem (cega).</summary>
    private static JsonElement LineOf(JsonElement counting, Guid item) =>
        counting.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("itemId").GetGuid() == item);

    private static HttpRequestMessage CountRequest(Guid count, JsonElement line, decimal quantity) =>
        new(HttpMethod.Put, $"{Counts}/{count}/lines/{Id(line)}") { Content = JsonContent.Create(new { quantity, unitId = (Guid?)null, version = Version(line) }) };

    /// <summary>Abre o inventário do local, conta o item com <paramref name="counted"/> e devolve o inventário.</summary>
    private static async Task<JsonElement> OpenAndCountAsync(HttpClient adjuster, HttpClient counter, Guid location, Guid item, decimal counted)
    {
        var count = await OkAsync(await adjuster.SendAsync(Command(Counts, Open(location))), System.Net.HttpStatusCode.Created);
        var counting = await OkAsync(await counter.GetAsync($"{Counts}/{Id(count)}/counting"));
        await OkAsync(await counter.SendAsync(CountRequest(Id(count), LineOf(counting, item), counted)));
        return await OkAsync(await adjuster.GetAsync($"{Counts}/{Id(count)}"));
    }

    private static async Task<JsonElement> CloseAsync(HttpClient adjuster, JsonElement count, string? policy = null) =>
        await OkAsync(await adjuster.SendAsync(Command($"{Counts}/{Id(count)}/close", new { uncountedPolicy = policy, version = Version(count) })));

    private async Task SetReleasedAsync(bool released)
    {
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == "inventory").ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    /// <summary>Proprietário único (SoleOwnerRule): só <paramref name="keep"/> fica ativo enquanto a ação roda.</summary>
    private async Task AloneAsync(Guid keep, Func<Task> action)
    {
        var others = await WithDbAsync(db => db.Users.Where(u => u.Id != keep && u.Status == UserStatus.Active).Select(u => u.Id).ToListAsync());
        await WithDbAsync(db => db.Users.Where(u => others.Contains(u.Id)).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserStatus.Inactive)));
        try
        {
            await action();
        }
        finally
        {
            await WithDbAsync(db => db.Users.Where(u => others.Contains(u.Id)).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserStatus.Active)));
        }
    }

    private sealed class FakeOrders : IProductionOrders
    {
        public Task<ProductionOrderInfo?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Orders.TryGetValue(id, out var o)
                ? new ProductionOrderInfo(id, o.Number, ProductionOrderKind.Fabrication, o.ProjectId, Guid.Empty, null, 1m, o.Status, null, null)
                : null);

        public async Task<IReadOnlyList<ProductionOrderInfo>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        {
            var found = new List<ProductionOrderInfo>();
            foreach (var id in ids)
            {
                if (await FindAsync(id, cancellationToken) is { } info)
                {
                    found.Add(info);
                }
            }

            return found;
        }

        public Task<IReadOnlyList<ProductionOrderInfo>> ByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProductionOrderInfo>>([]);

        public Task<ProductionOrderState?> StatusAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Orders.TryGetValue(id, out var o) ? o.Status : (ProductionOrderState?)null);

        public async Task<IReadOnlyList<ProductionOrderInfo>> OpenAsync(CancellationToken cancellationToken = default) =>
            await FindManyAsync(Orders.Where(o => o.Value.Status is not (ProductionOrderState.Completed or ProductionOrderState.Cancelled)).Select(o => o.Key).ToList(), cancellationToken);
    }

    private sealed class FakeBoms : IReleasedBomSource
    {
        public Task<ReleasedBom?> ReleasedForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Boms.TryGetValue(productionOrderId, out var bom) ? bom : null);

        public Task<IReadOnlyList<Guid>> OpenOrdersWithReleasedBomAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>(Boms.Keys.Where(k => Orders.TryGetValue(k, out var o) && o.Status is not (ProductionOrderState.Cancelled or ProductionOrderState.Completed)).ToList());
    }
}
