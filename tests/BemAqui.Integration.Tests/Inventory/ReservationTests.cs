using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Commercial.Contracts;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Inventory;

/// <summary>
/// EST-02: reservas e faltas com PostgreSQL real (movimenta o reservado da posição). A BOM liberada e a OS vêm de fakes da fonte
/// (<see cref="IReleasedBomSource"/> e <see cref="IProductionOrders"/>), porque a ENG-02 corre em paralelo; a reconciliação é a de
/// produção. Banco desta classe; cada teste cria os próprios itens, locais e OS e filtra pelos ids que criou.
/// </summary>
public partial class ReservationTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Base = "/api/v1/inventory";

    /// <summary>OS que o <see cref="IProductionOrders"/> falso conhece.</summary>
    private static readonly ConcurrentDictionary<Guid, (ProductionOrderState Status, Guid ProjectId, string Number)> Orders = new();

    /// <summary>BOM liberada vigente por OS, como o <c>IBoms.ReleasedForOrderAsync</c> devolveria.</summary>
    private static readonly ConcurrentDictionary<Guid, ReleasedBom> Boms = new();

    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory => _factory ??= tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
    {
        s.AddScoped<IProjects, FakeProjects>();
        s.AddScoped<IProductionOrders, FakeOrders>();
        s.AddScoped<IReleasedBomSource, FakeBoms>();
    }));

    private static string Unique(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

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

    /// <summary>Usuário num grupo do seed (<c>owner</c>, <c>warehouse</c>) ou num grupo só de consulta (<c>view:production</c>).</summary>
    private async Task<(Guid Id, HttpClient Client)> ActorAsync(string? seedKey)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "r" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
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

    private async Task<Guid> ItemAsync(bool tracksLot = false, ItemType type = ItemType.RawMaterial)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var unit = await db.Units.Where(u => u.Code == "UN").Select(u => u.Id).SingleAsync();
        var item = new Item { Type = type, Code = Unique("MP"), Name = "Cabo " + Guid.NewGuid().ToString("N")[..4], UnitId = unit, Ncm = "84313110", TracksLot = tracksLot, CreatedAt = DateTimeOffset.UtcNow };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    private async Task<Guid> LocationAsync(string prefix = "L")
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var location = new Location { Code = Unique(prefix), Name = "Local " + Guid.NewGuid().ToString("N")[..4], CreatedAt = DateTimeOffset.UtcNow };
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        return location.Id;
    }

    /// <summary>O local padrão do seed (ALM).</summary>
    private Task<Guid> DefaultLocationAsync() => WithDbAsync(db => db.Locations.Where(l => l.IsDefault).Select(l => l.Id).SingleAsync());

    /// <summary>OS de fabricação conhecida pelo fake, aberta por padrão.</summary>
    private static Guid Order(ProductionOrderState status = ProductionOrderState.Open)
    {
        var id = Guid.CreateVersion7();
        Orders[id] = (status, Guid.CreateVersion7(), "OS-2026-" + Random.Shared.Next(1000, 9999).ToString(System.Globalization.CultureInfo.InvariantCulture));
        return id;
    }

    private static void SetStatus(Guid order, ProductionOrderState status) => Orders[order] = Orders[order] with { Status = status };

    /// <summary>Libera (ou revisa) a BOM da OS na fonte: a revisão nova substitui a anterior.</summary>
    private static ReleasedBom Release(Guid order, int revision, params (Guid Item, decimal Quantity)[] lines) =>
        ReleaseAt(order, revision, DateTimeOffset.UtcNow.AddMinutes(-60 + revision), lines);

    private static ReleasedBom ReleaseAt(Guid order, int revision, DateTimeOffset releasedAt, params (Guid Item, decimal Quantity)[] lines)
    {
        var bom = new ReleasedBom(Guid.CreateVersion7(), revision, releasedAt, lines.Select(l => new ReleasedBomLine(Guid.CreateVersion7(), l.Item, l.Quantity)).ToList());
        Boms[order] = bom;
        return bom;
    }

    /// <summary>O consumidor, como a inbox o roda: uma transação, a reconciliação pela fonte e o commit.</summary>
    private async Task ReconcileAsync(Guid order, string origin = "Evento BomReleased · mensagem teste")
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await scope.ServiceProvider.GetRequiredService<StockReservationService>().ReconcileOrderAsync(order, origin);
        await transaction.CommitAsync();
    }

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

    private async Task ReceiveAsync(Guid item, Guid location, decimal quantity, Guid? originOrder = null, Guid? lot = null)
    {
        var result = await PostAsync(new StockPosting(StockMovementType.Receipt, item, location, lot, quantity, 1m, StockSources.GoodsReceipt, Guid.NewGuid(), ProductionOrderId: originOrder));
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
    }

    private Task<List<Reservation>> ReservationsAsync(Guid order) => WithDbAsync(db => db.Reservations.AsNoTracking().Where(r => r.ProductionOrderId == order).ToListAsync());

    private Task<(decimal OnHand, decimal Reserved)> PositionAsync(Guid item, Guid location) => WithDbAsync(async db =>
        await db.StockPositions.AsNoTracking().Where(p => p.ItemId == item && p.LocationId == location).Select(p => new ValueTuple<decimal, decimal>(p.QuantityOnHand, p.QuantityReserved)).FirstOrDefaultAsync());

    /// <summary>Avisos de falta da OS no outbox (filtrados na memória pelo id da OS: payload é jsonb).</summary>
    private async Task<List<StockShortageChanged>> ShortageEventsAsync(Guid order)
    {
        var payloads = await WithDbAsync(db => db.OutboxMessages.AsNoTracking().Where(m => m.Type == typeof(StockShortageChanged).FullName).OrderBy(m => m.OccurredAt).Select(m => m.Payload).ToListAsync());
        return payloads.Select(p => JsonSerializer.Deserialize<StockShortageChanged>(p)!).Where(e => e.ProductionOrderId == order).ToList();
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

    private sealed class FakeProjects : IProjects
    {
        public Task<ProjectSummary?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProjectSummary?>(new ProjectSummary(id, "PRJ-2026-" + id.ToString("N")[..4], "Projeto de teste", Guid.NewGuid(), Guid.NewGuid(), "InProgress", null, null, new DateOnly(2026, 12, 1)));

        public async Task<IReadOnlyList<ProjectSummary>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        {
            var found = new List<ProjectSummary>();
            foreach (var id in ids)
            {
                found.Add((await FindAsync(id, cancellationToken))!);
            }

            return found;
        }

        public Task<IReadOnlyList<ProjectSummary>> SearchAsync(string? term, int limit = 20, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProjectSummary>>([]);

        public Task<IReadOnlyList<ProjectItemInfo>> ItemsAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProjectItemInfo>>([]);

        public Task<IReadOnlyList<ProjectItemInfo>> FindItemsAsync(IReadOnlyCollection<Guid> projectItemIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProjectItemInfo>>([]);

        public Task<IReadOnlyList<ProjectItemInfo>> FabricationCandidatesAsync(Guid? projectId, int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProjectItemInfo>>([]);

        public Task<decimal?> BudgetedAsync(Guid projectId, CostType costType, CancellationToken cancellationToken = default) => Task.FromResult<decimal?>(null);

        public Task<ProjectFabricationInfo?> FabricationAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<ProjectFabricationInfo?>(null);
    }
}
