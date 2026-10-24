using System.Collections.Concurrent;
using System.Net;
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
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// Base dos testes do pedido de compra (CMP-03a) com PostgreSQL real: o projeto vem de um fake da fonte (o teste manda na situação);
/// fornecedor, condição, plano de contas, alçadas, títulos a pagar e e-mail são os de produção. Cada classe derivada tem o próprio banco
/// (as regras de alçada mudam todos os pedidos dela); cada teste cria os próprios fornecedores, itens e pedidos e filtra pelos ids dele.
/// </summary>
public abstract class PurchaseOrderTestBase(TenancyFixture tenancy)
{
    protected const string Host = "a.localhost";
    protected const string Base = "/api/v1/purchasing/purchase-orders";

    /// <summary>Projetos conhecidos pelo fake: a situação.</summary>
    protected static readonly ConcurrentDictionary<Guid, string> Projects = new();

    private WebApplicationFactory<Program>? _factory;

    protected TenancyFixture Tenancy => tenancy;

    protected WebApplicationFactory<Program> Factory => _factory ??= tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(Configure));

    protected static DateOnly Today => BemAqui.BuildingBlocks.Time.BusinessTime.Today(TimeProvider.System);

    protected static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    protected static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    protected static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    protected static uint Version(JsonElement order) => order.GetProperty("version").GetUInt32();

    protected static string Unique(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    /// <summary>Serviços extras da classe (ex.: um <c>IPayables</c> que falha); por padrão só o projeto falso.</summary>
    protected virtual void Configure(IServiceCollection services) => services.AddScoped<IProjects, FakeProjects>();

    protected async Task<AsyncServiceScope> ScopeAsync(Tenant? tenant = null)
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync((tenant ?? tenancy.TenantA).Id))!;
        return await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    protected async Task<T> WithDbAsync<T>(Func<TenantDbContext, Task<T>> action, Tenant? tenant = null)
    {
        await using var scope = await ScopeAsync(tenant);
        return await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    /// <summary>
    /// Usuário num grupo do seed (<c>owner</c>, <c>finance</c>, <c>warehouse</c>), num grupo com as ações dadas
    /// (<c>purchasing:view,create;finance:view,approve</c>) ou sem grupo (null). Token de teste: quem aprova não passa pelo 2FA do login.
    /// </summary>
    protected async Task<(Guid Id, HttpClient Client)> ActorAsync(string? group)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "p" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (group is not null)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            Guid groupId;
            if (group.Contains(':', StringComparison.Ordinal))
            {
                var created = new Group { Name = "Grupo " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
                db.Groups.Add(created);
                foreach (var part in group.Split(';'))
                {
                    var (module, actions) = (part[..part.IndexOf(':', StringComparison.Ordinal)], part[(part.IndexOf(':', StringComparison.Ordinal) + 1)..]);
                    db.GroupModules.Add(new GroupModule { GroupId = created.Id, ModuleCode = module, Enabled = true });
                    db.GroupPermissions.AddRange(actions.Split(',').Select(a => new GroupGrant { GroupId = created.Id, ModuleCode = module, Action = a }));
                }

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

    /// <summary>Fornecedor ativo (papel Fornecedor), com a condição padrão dada (pelo nome do seed).</summary>
    protected async Task<Guid> SupplierAsync(HttpClient owner, string? defaultTerm = "30/60/90 dias", bool supplierRole = true)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/parties", new
        {
            kind = "PJ", document = (string?)null, legalName = "Fornecedor " + Guid.NewGuid().ToString("N")[..6], roles = supplierRole ? new[] { "Supplier" } : new[] { "Customer" },
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await JsonAsync(created)).GetProperty("id").GetGuid();
        if (defaultTerm is not null)
        {
            var term = await TermAsync(defaultTerm);
            await WithDbAsync(async db =>
            {
                var party = await db.Parties.SingleAsync(p => p.Id == id);
                party.DefaultPaymentTermId = term;
                return await db.SaveChangesAsync();
            });
        }

        return id;
    }

    protected Task<Guid> TermAsync(string name) => WithDbAsync(db => db.PaymentTerms.Where(t => t.Name == name).Select(t => t.Id).SingleAsync());

    protected async Task<Guid> ItemAsync(ItemType type = ItemType.RawMaterial, bool active = true)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var unit = await db.Units.Where(u => u.Code == "UN").Select(u => u.Id).SingleAsync();
        var item = new Item
        {
            Type = type, Code = Unique("MP"), Name = "Chapa " + Guid.NewGuid().ToString("N")[..4], UnitId = unit, Ncm = type == ItemType.Service ? null : "72085100", CreatedAt = DateTimeOffset.UtcNow,
            IsActive = active,
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    protected static Guid Project(string status = "InProgress")
    {
        var id = Guid.CreateVersion7();
        Projects[id] = status;
        return id;
    }

    /// <summary>SC manual aberta com um item (CMP-01), pelo endpoint; devolve as linhas.</summary>
    protected static async Task<(Guid RequestId, Guid LineId)> RequestAsync(HttpClient buyer, Guid item, decimal quantity, Guid? project = null)
    {
        var created = await buyer.SendAsync(Post("/api/v1/purchasing/purchase-requests", new
        {
            projectId = project, productionOrderId = (Guid?)null, notes = (string?)null, lines = new[] { new { itemId = item, quantity, unitId = (Guid?)null, neededBy = Iso(Today.AddDays(10)) } },
        }));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var body = await JsonAsync(created);
        return (body.GetProperty("id").GetGuid(), body.GetProperty("lines")[0].GetProperty("id").GetGuid());
    }

    protected static object OrderBody(Guid party, Guid? project, Guid? term, params (Guid Item, decimal Quantity, decimal Price, Guid[]? Requests)[] lines) => new
    {
        partyId = party, projectId = project, paymentTermId = term, expectedDate = Iso(Today.AddDays(20)), freight = 0m, ipiPct = 0m, notes = (string?)null,
        lines = lines.Select(l => new { itemId = l.Item, quantity = l.Quantity, unitId = (Guid?)null, unitPrice = l.Price, requestLineIds = l.Requests ?? [] }),
    };

    protected static async Task<JsonElement> CreateAsync(HttpClient buyer, object body, Guid? key = null)
    {
        var created = await buyer.SendAsync(Post(Base, body, key));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return await JsonAsync(created);
    }

    protected static async Task<JsonElement> SubmitAsync(HttpClient buyer, JsonElement order, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await buyer.PostAsJsonAsync($"{Base}/{order.GetProperty("id").GetGuid()}/submit", new { version = Version(order) });
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    protected static Task<HttpResponseMessage> DecideAsync(HttpClient approver, JsonElement order, bool approve, string? comment = null) =>
        approver.PostAsJsonAsync($"{Base}/{order.GetProperty("id").GetGuid()}/{(approve ? "approve" : "reject")}", new { comment, version = Version(order) });

    protected static async Task<JsonElement> GetAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"{Base}/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    /// <summary>Títulos de origem <c>Purchase</c> do pedido, direto no banco (o teste não depende da tela do Financeiro).</summary>
    protected Task<List<BemAqui.Finance.Domain.Payable>> PayablesOfAsync(Guid orderId) =>
        WithDbAsync(db => db.Payables.AsNoTracking().Where(p => p.Origin == DocumentOrigin.Purchase && p.OriginId == orderId).OrderBy(p => p.Installment).ThenBy(p => p.CreatedAt).ToListAsync());

    protected Task<PurchaseRequestLine> RequestLineAsync(Guid lineId) => WithDbAsync(db => db.PurchaseRequestLines.AsNoTracking().SingleAsync(l => l.Id == lineId));

    protected async Task SetReleasedAsync(bool released)
    {
        var tenantId = tenancy.TenantA.Id;
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules
                .Where(m => m.TenantId == tenantId && m.ModuleCode == "purchasing").ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
        Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    /// <summary>Regra de alçada de pedido de compra pela tela de Alçadas (só o Proprietário).</summary>
    protected static async Task<Guid> RuleAsync(HttpClient owner, int level, decimal min, decimal? max, Guid? approverUser = null, Guid? approverGroup = null)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/finance/approval-rules", new
        {
            minAmount = min, maxAmount = max, accountId = (Guid?)null, approverGroupId = approverGroup, approverUserId = approverUser, level, documentType = "PurchaseOrder",
        });
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    protected static HttpRequestMessage Post(string url, object body, Guid? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        return request;
    }

    protected sealed class FakeProjects : IProjects
    {
        public Task<ProjectSummary?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Projects.TryGetValue(id, out var status)
                ? new ProjectSummary(id, "PRJ-2026-" + id.ToString("N")[..4], "Projeto de teste", Guid.NewGuid(), Guid.NewGuid(), status, null, null, null)
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
