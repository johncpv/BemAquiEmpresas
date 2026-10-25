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
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.Platform.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using BemAqui.Production.Contracts;
using BemAqui.Purchasing.Application;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// CMP-01: solicitações de compra com PostgreSQL real. As faltas abertas (<see cref="IStockReservations"/>), o disponível
/// (<see cref="IStockBalances"/>), a OS e o projeto vêm de fakes da fonte, para o teste mandar na falta; a recomposição, o job e a tela
/// são os de produção. Banco desta classe; cada teste cria os próprios itens e OS e filtra pelos ids que criou.
/// </summary>
public partial class PurchaseRequestTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Base = "/api/v1/purchasing";

    /// <summary>Faltas abertas por (OS, item), como o <c>IStockReservations.OpenShortagesAsync</c> devolveria.</summary>
    private static readonly ConcurrentDictionary<(Guid Order, Guid Item), StockShortageInfo> Shortages = new();

    /// <summary>Disponível por item (todos os locais).</summary>
    private static readonly ConcurrentDictionary<Guid, decimal> Available = new();

    private static readonly ConcurrentDictionary<Guid, (ProductionOrderState Status, Guid ProjectId, string Number)> Orders = new();

    /// <summary>Projetos conhecidos: situação e data prometida.</summary>
    private static readonly ConcurrentDictionary<Guid, (string Status, DateOnly? Promised)> Projects = new();

    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory => _factory ??= tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
    {
        s.AddScoped<IProjects, FakeProjects>();
        s.AddScoped<IProductionOrders, FakeOrders>();
        s.AddScoped<IStockReservations, FakeReservations>();
        s.AddScoped<IStockBalances, FakeBalances>();
    }));

    private static DateOnly Today => BemAqui.BuildingBlocks.Time.BusinessTime.Today(TimeProvider.System);

    private static string Unique(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private async Task<AsyncServiceScope> ScopeAsync(Tenant? tenant = null)
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync((tenant ?? tenancy.TenantA).Id))!;
        return await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task<T> WithDbAsync<T>(Func<TenantDbContext, Task<T>> action, Tenant? tenant = null)
    {
        await using var scope = await ScopeAsync(tenant);
        return await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    /// <summary>
    /// Usuário num grupo do seed (<c>owner</c>, <c>warehouse</c>, <c>finance</c>), num grupo com as ações dadas de Compras
    /// (<c>purchasing:view,edit</c>) ou sem grupo (null).
    /// </summary>
    private async Task<(Guid Id, HttpClient Client)> ActorAsync(string? group)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "c" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (group is not null)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            Guid groupId;
            if (group.StartsWith("purchasing:", StringComparison.Ordinal))
            {
                var created = new Group { Name = "Compras " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
                db.Groups.Add(created);
                db.GroupModules.Add(new GroupModule { GroupId = created.Id, ModuleCode = "purchasing", Enabled = true });
                db.GroupPermissions.AddRange(group["purchasing:".Length..].Split(',').Select(a => new GroupGrant { GroupId = created.Id, ModuleCode = "purchasing", Action = a }));
                groupId = created.Id;
            }
            else
            {
                groupId = await db.Groups.Where(g => g.SeedKey == group).Select(g => g.Id).SingleAsync();
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

    private async Task<Guid> ItemAsync(ItemType type = ItemType.RawMaterial, decimal minStock = 0m, Tenant? tenant = null)
    {
        await using var scope = await ScopeAsync(tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var unit = await db.Units.Where(u => u.Code == "UN").Select(u => u.Id).SingleAsync();
        var item = new Item
        {
            Type = type, Code = Unique("MP"), Name = "Cabo " + Guid.NewGuid().ToString("N")[..4], UnitId = unit, Ncm = type == ItemType.Service ? null : "84313110", MinStock = minStock,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    private static Guid Project(string status = "InProgress", DateOnly? promised = null)
    {
        var id = Guid.CreateVersion7();
        Projects[id] = (status, promised);
        return id;
    }

    /// <summary>OS de fabricação aberta conhecida pelo fake, num projeto novo (ou no dado).</summary>
    private static Guid Order(Guid? project = null, ProductionOrderState status = ProductionOrderState.Open)
    {
        var id = Guid.CreateVersion7();
        Orders[id] = (status, project ?? Project(promised: Today.AddDays(40)), "OS-2026-" + Random.Shared.Next(10000, 99999).ToString(System.Globalization.CultureInfo.InvariantCulture));
        return id;
    }

    /// <summary>Muda a falta aberta da OS no item na fonte (0 = sem falta).</summary>
    private static void Short(Guid order, Guid item, decimal quantity, Guid? bomLine = null)
    {
        if (quantity <= 0m)
        {
            Shortages.TryRemove((order, item), out _);
            return;
        }

        Shortages[(order, item)] = new StockShortageInfo(order, Orders[order].ProjectId, item, bomLine ?? Guid.CreateVersion7(), quantity, DateTimeOffset.UtcNow.AddHours(-1));
    }

    /// <summary>O consumidor como a inbox o roda: uma transação, a recomposição pela fonte e o commit (o valor do evento é ignorado).</summary>
    private async Task DeliverAsync(Guid order, Guid item, decimal eventQty, Tenant? tenant = null)
    {
        await using var scope = await ScopeAsync(tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await scope.ServiceProvider.GetRequiredService<StockShortageChangedHandler>()
            .HandleAsync(new StockShortageChanged(item, order, Orders[order].ProjectId, null, eventQty), CancellationToken.None);
        await transaction.CommitAsync();
    }

    private Task<List<PurchaseRequestLine>> LinesOfOrderAsync(Guid order) =>
        WithDbAsync(db => db.PurchaseRequestLines.AsNoTracking().Where(l => l.ProductionOrderId == order).OrderBy(l => l.CreatedAt).ToListAsync());

    private Task<List<PurchaseRequestLine>> LinesOfItemAsync(Guid item, Tenant? tenant = null) =>
        WithDbAsync(db => db.PurchaseRequestLines.AsNoTracking().Where(l => l.ItemId == item).OrderBy(l => l.CreatedAt).ToListAsync(), tenant);

    /// <summary>Muda a situação da linha direto no banco (a cotação e o pedido são da CMP-02 e da CMP-03a).</summary>
    private Task<int> SetLineStatusAsync(Guid lineId, BemAqui.Purchasing.Contracts.PurchaseRequestStatus status) => WithDbAsync(async db =>
    {
        var line = await db.PurchaseRequestLines.SingleAsync(l => l.Id == lineId);
        line.Status = status;
        await db.SaveChangesAsync();
        var request = await db.PurchaseRequests.SingleAsync(r => r.Id == line.RequestId);
        request.Status = PurchaseRequestRules.DeriveStatus(await db.PurchaseRequestLines.Where(l => l.RequestId == request.Id).Select(l => l.Status).ToListAsync());
        return await db.SaveChangesAsync();
    });

    private async Task SetReleasedAsync(bool released, Tenant? tenant = null)
    {
        var tenantId = (tenant ?? tenancy.TenantA).Id;
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules
                .Where(m => m.TenantId == tenantId && m.ModuleCode == "purchasing").ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
        Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    private static HttpRequestMessage Post(string url, object body, Guid? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        return request;
    }

    private sealed class FakeReservations : IStockReservations
    {
        public Task<IReadOnlyList<StockReservationInfo>> ForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StockReservationInfo>>([]);

        public Task<IReadOnlyList<StockShortageInfo>> OpenShortagesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StockShortageInfo>>(Shortages.Values.OrderBy(s => s.BomReleasedAt).ToList());

        public Task ReleaseForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<Guid>> OrdersCompletedByEntriesAsync(IReadOnlyCollection<string> movementNumbers, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
    }

    private sealed class FakeBalances : IStockBalances
    {
        public Task<IReadOnlyList<StockAvailability>> GetAsync(IReadOnlyCollection<Guid> itemIds, Guid? locationId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StockAvailability>>(itemIds.Where(Available.ContainsKey)
                .Select(i => new StockAvailability(i, Guid.Empty, Available[i], 0m, Available[i])).ToList());
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

        public Task<IReadOnlyList<ProductionOrderInfo>> OpenAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProductionOrderInfo>>([]);
    }

    private sealed class FakeProjects : IProjects
    {
        public Task<ProjectSummary?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Projects.TryGetValue(id, out var p)
                ? new ProjectSummary(id, "PRJ-2026-" + id.ToString("N")[..4], "Projeto de teste", Guid.NewGuid(), Guid.NewGuid(), p.Status, null, null, p.Promised)
                : null);

        public async Task<IReadOnlyList<ProjectSummary>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        {
            var found = new List<ProjectSummary>();
            foreach (var id in ids)
            {
                if (await FindAsync(id, cancellationToken) is { } summary)
                {
                    found.Add(summary);
                }
            }

            return found;
        }

        public async Task<IReadOnlyList<ProjectSummary>> SearchAsync(string? term, int limit = 20, CancellationToken cancellationToken = default) =>
            await FindManyAsync(Projects.Keys.Take(limit).ToList(), cancellationToken);

        public Task<IReadOnlyList<ProjectItemInfo>> ItemsAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProjectItemInfo>>([]);

        public Task<IReadOnlyList<ProjectItemInfo>> FindItemsAsync(IReadOnlyCollection<Guid> projectItemIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProjectItemInfo>>([]);

        public Task<IReadOnlyList<ProjectItemInfo>> FabricationCandidatesAsync(Guid? projectId, int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProjectItemInfo>>([]);

        public Task<decimal?> BudgetedAsync(Guid projectId, CostType costType, CancellationToken cancellationToken = default) => Task.FromResult<decimal?>(null);

        public Task<ProjectFabricationInfo?> FabricationAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<ProjectFabricationInfo?>(null);
    }
}
