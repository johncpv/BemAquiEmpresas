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
/// EST-01b: ajuste de estoque com PostgreSQL real (movimenta estoque, Testcontainers obrigatório). O reservado vem da reconciliação de
/// verdade da EST-02 sobre fakes da BOM liberada e da OS; o bloqueio do local é um dublê do <see cref="ILocationCountLock"/> (CA 18).
/// Banco desta classe; cada teste cria os próprios itens, locais e OS e filtra pelos ids que criou.
/// </summary>
public partial class StockAdjustmentTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Base = "/api/v1/inventory";
    private const string Adjustments = Base + "/adjustments";

    /// <summary>Locais com inventário em contagem para o dublê do bloqueio (CA 18).</summary>
    private static readonly ConcurrentDictionary<Guid, LocationCountBlock> Counting = new();

    private static readonly ConcurrentDictionary<Guid, (ProductionOrderState Status, Guid ProjectId, string Number)> Orders = new();

    private static readonly ConcurrentDictionary<Guid, ReleasedBom> Boms = new();

    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory => _factory ??= tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
    {
        s.AddScoped<ILocationCountLock, FakeCountLock>();
        s.AddScoped<IProductionOrders, FakeOrders>();
        s.AddScoped<IReleasedBomSource, FakeBoms>();
    }));

    private static string Unique(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private static string Detail(JsonElement problem) => problem.GetProperty("detail").GetString()!;

    private static string[] FieldErrors(JsonElement problem, string field) =>
        problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString()!).ToArray();

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

    /// <summary>Corpo do "Enviar para aprovação": modo do ajuste e as linhas (item, lote, quantidade).</summary>
    private static object Adjustment(Guid location, string mode, params object[] lines) =>
        new { locationId = location, mode, reason = "Loss", reasonDetail = (string?)null, lines };

    private static object Line(Guid item, decimal quantity, Guid? lot = null) => new { itemId = item, lotId = lot, quantity, unitId = (Guid?)null };

    private static object Decision(JsonElement adjustment, string? comment = null) => new { comment, version = adjustment.GetProperty("version").GetUInt32() };

    private static Guid Id(JsonElement adjustment) => adjustment.GetProperty("id").GetGuid();

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
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "j" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
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

    private async Task<Guid> ItemAsync(bool tracksLot = false, decimal minStock = 0m)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var unit = await db.Units.Where(u => u.Code == "UN").Select(u => u.Id).SingleAsync();
        var item = new Item { Type = ItemType.RawMaterial, Code = Unique("MP"), Name = "Guia " + Guid.NewGuid().ToString("N")[..4], UnitId = unit, Ncm = "84313110", TracksLot = tracksLot, MinStock = minStock, CreatedAt = DateTimeOffset.UtcNow };
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

    /// <summary>O local padrão do seed (ALM): a reserva da EST-02 começa por ele.</summary>
    private Task<Guid> DefaultLocationAsync() => WithDbAsync(db => db.Locations.Where(l => l.IsDefault).Select(l => l.Id).SingleAsync());

    /// <summary>Uma chamada ao motor numa transação própria, como faria o documento de outro módulo.</summary>
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

    private async Task ReceiveAsync(Guid item, Guid location, decimal quantity, decimal cost, Guid? lot = null)
    {
        var result = await PostAsync(new StockPosting(StockMovementType.Receipt, item, location, lot, quantity, cost, StockSources.GoodsReceipt, Guid.NewGuid()));
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
    }

    private async Task<Guid> LotAsync(Guid item, string number)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var lot = await scope.ServiceProvider.GetRequiredService<IStockMovements>().EnsureLotAsync(item, number);
        await transaction.CommitAsync();
        return lot;
    }

    /// <summary>Reserva <paramref name="quantity"/> do item para uma OS nova pela reconciliação da EST-02 (local padrão primeiro).</summary>
    private async Task<Guid> ReserveAsync(Guid item, decimal quantity)
    {
        var order = Guid.CreateVersion7();
        Orders[order] = (ProductionOrderState.Open, Guid.CreateVersion7(), "OS-2026-" + Random.Shared.Next(1000, 9999).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Boms[order] = new ReleasedBom(Guid.CreateVersion7(), 1, DateTimeOffset.UtcNow.AddMinutes(-30), [new ReleasedBomLine(Guid.CreateVersion7(), item, quantity)]);
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await scope.ServiceProvider.GetRequiredService<StockReservationService>().ReconcileOrderAsync(order, "Evento BomReleased · teste do ajuste");
        await transaction.CommitAsync();
        return order;
    }

    private Task<(decimal OnHand, decimal Reserved, decimal Average)> StateAsync(Guid item, Guid location) => WithDbAsync(async db =>
    {
        var position = await db.StockPositions.AsNoTracking().Where(p => p.ItemId == item && p.LocationId == location).Select(p => new { p.QuantityOnHand, p.QuantityReserved }).FirstOrDefaultAsync();
        var average = await db.Items.AsNoTracking().Where(i => i.Id == item).Select(i => i.AverageCost).SingleAsync();
        return (position?.QuantityOnHand ?? 0m, position?.QuantityReserved ?? 0m, average);
    });

    private Task<List<StockMovement>> MovementsAsync(Guid adjustmentId) =>
        WithDbAsync(db => db.StockMovements.AsNoTracking().Where(m => m.SourceType == StockSources.Adjustment && m.SourceId == adjustmentId).OrderBy(m => m.PostedSeq).ToListAsync());

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

    private sealed class FakeCountLock : ILocationCountLock
    {
        public Task<LocationCountBlock?> FindAsync(IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(locationIds.Select(id => Counting.TryGetValue(id, out var block) ? block : null).FirstOrDefault(b => b is not null));
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
