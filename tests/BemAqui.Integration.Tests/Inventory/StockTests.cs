using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Inventory;

/// <summary>
/// EST-01a: motor de estoque e lançamentos manuais, com PostgreSQL real (movimenta estoque). Banco desta classe; cada teste cria
/// os próprios itens e locais e filtra pelos ids que criou. A implantação concluída e o resumo diário ficam em classes próprias.
/// </summary>
public partial class StockTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Base = "/api/v1/inventory";

    /// <summary>Projetos que o <see cref="IProjects"/> falso conhece (id → situação), para a validação do CA 31.</summary>
    private static readonly ConcurrentDictionary<Guid, string> Projects = new();

    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory => _factory ??= tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<IProjects, FakeProjects>()));

    private static DateOnly Today => BusinessTime.Today(TimeProvider.System);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static string Unique(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private static string Detail(JsonElement problem) => problem.GetProperty("detail").GetString()!;

    private static string[] FieldErrors(JsonElement problem, string field) =>
        problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(e => e.GetString()!).ToArray();

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
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

    /// <summary>Usuário num grupo do seed (<c>owner</c>, <c>warehouse</c>, <c>production_supervision</c>) ou num grupo só de consulta (<c>view:inventory</c>).</summary>
    private async Task<(Guid Id, HttpClient Client)> ActorAsync(string? seedKey)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "e" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
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

    /// <summary>Item ativo de matéria-prima em UN (com lote, se pedido), direto no banco do tenant.</summary>
    private async Task<Guid> ItemAsync(bool tracksLot = false, string unitCode = "UN", decimal minStock = 0m)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var unit = await db.Units.Where(u => u.Code == unitCode).Select(u => u.Id).SingleAsync();
        var item = new Item { Type = ItemType.RawMaterial, Code = Unique("MP"), Name = "Chapa " + Guid.NewGuid().ToString("N")[..4], UnitId = unit, Ncm = "84313110", TracksLot = tracksLot, MinStock = minStock, CreatedAt = DateTimeOffset.UtcNow };
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

    /// <summary>Uma chamada ao motor numa transação própria, como faria o documento de outro módulo (commit só no sucesso).</summary>
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

    private Task<Result<StockMovementResult>> ReceiveAsync(Guid item, Guid location, decimal quantity, decimal cost, Guid? lot = null, Guid? source = null) =>
        PostAsync(new StockPosting(StockMovementType.Receipt, item, location, lot, quantity, cost, StockSources.GoodsReceipt, source ?? Guid.NewGuid()));

    private Task<Result<StockMovementResult>> IssueAsync(Guid item, Guid location, decimal quantity, Guid? lot = null, Guid? project = null, Guid? order = null, Guid? source = null) =>
        PostAsync(new StockPosting(StockMovementType.Issue, item, location, lot, -quantity, null, StockSources.Requisition, source ?? Guid.NewGuid(), ProjectId: project, ProductionOrderId: order));

    private Task<(decimal OnHand, decimal Average)> StateAsync(Guid item, Guid location) => WithDbAsync(async db =>
        (await db.StockPositions.AsNoTracking().Where(p => p.ItemId == item && p.LocationId == location).Select(p => p.QuantityOnHand).FirstOrDefaultAsync(),
         await db.Items.AsNoTracking().Where(i => i.Id == item).Select(i => i.AverageCost).SingleAsync()));

    /// <summary>Corpo da entrada manual, saída avulsa ou transferência.</summary>
    private static object Manual(string kind, Guid item, Guid location, decimal quantity, decimal? cost = null, Guid? to = null, string? lotNumber = null, Guid? lotId = null, Guid? project = null, string reason = "Teste") =>
        new { kind, itemId = item, locationId = location, toLocationId = to, quantity, unitId = (Guid?)null, unitCost = cost, lotId, lotNumber, reason, projectId = project };

    private sealed class FakeProjects : IProjects
    {
        public Task<ProjectSummary?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Projects.TryGetValue(id, out var status) ? new ProjectSummary(id, "PRJ-2026-" + id.ToString("N")[..4], "Projeto de teste", Guid.NewGuid(), Guid.NewGuid(), status, null) : null);

        public async Task<IReadOnlyList<ProjectSummary>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        {
            var found = new List<ProjectSummary>();
            foreach (var id in ids)
            {
                if (await FindAsync(id, cancellationToken) is { } project)
                {
                    found.Add(project);
                }
            }

            return found;
        }

        public Task<IReadOnlyList<ProjectSummary>> SearchAsync(string? term, int limit = 20, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProjectSummary>>([]);

        public Task<IReadOnlyList<ProjectItemInfo>> ItemsAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProjectItemInfo>>([]);

        public Task<IReadOnlyList<ProjectItemInfo>> FindItemsAsync(IReadOnlyCollection<Guid> projectItemIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProjectItemInfo>>([]);

        public Task<IReadOnlyList<ProjectItemInfo>> FabricationCandidatesAsync(Guid? projectId, int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProjectItemInfo>>([]);

        public Task<decimal?> BudgetedAsync(Guid projectId, CostType costType, CancellationToken cancellationToken = default) => Task.FromResult<decimal?>(null);
    }
}
