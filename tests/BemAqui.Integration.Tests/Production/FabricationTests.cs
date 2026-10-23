using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Attachments;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Production;

/// <summary>PRD-01: OS de fabricação gerada do item do projeto, etapas, anexos, lista, cancelamento e contratos. Banco desta classe.</summary>
public partial class FabricationTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>, IDisposable
{
    private const string Host = "a.localhost";
    private const string Base = "/api/v1/production/production-orders";
    private const string Quotes = "/api/v1/quotes";
    private const string Routings = "/api/v1/engineering/routing-templates";

    private readonly FakeScanner _scanner = new();
    private readonly string _storage = Path.Combine(Path.GetTempPath(), "bqe-prd01-" + Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<Program>? _factory;

    /// <summary>
    /// Os testes da PRD-01 a PRD-06a1 andam as etapas sem BOM (Marco 3.1): aqui vale o <see cref="NoBomGate"/>. O bloqueio real da ENG-02
    /// (<see cref="EngineeringBomGate"/>) é testado em <c>FabricationTests.Bom</c>, com <see cref="BomFactory"/>.
    /// </summary>
    private WebApplicationFactory<Program> Factory => _factory ??= tenancy.Factory.WithWebHostBuilder(b => b
        .UseSetting("Storage:Root", _storage)
        .UseSetting("Attachments:MaxUploadsPerHour", "30")
        .ConfigureTestServices(s =>
        {
            s.AddSingleton<BemAqui.BuildingBlocks.Attachments.IVirusScanner>(_scanner);
            s.AddScoped<IBomGate, NoBomGate>();
        }));

    private static DateOnly Today => BusinessTime.Today(TimeProvider.System);

    public void Dispose()
    {
        _bomFactory?.Dispose();
        _factory?.Dispose();
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, true);
        }

        GC.SuppressFinalize(this);
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private static uint Version(JsonElement doc) => doc.GetProperty("version").GetUInt32();

    private static string Unique(string name) => name + " " + Guid.NewGuid().ToString("N")[..6];

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task<T> WithDbAsync<T>(Func<TenantDbContext, Task<T>> action)
    {
        await using var scope = await ScopeAsync();
        return await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    /// <summary>Usuário num grupo do seed (seed_key), num grupo com as ações dadas ("production:view,edit") ou sem grupo.</summary>
    private async Task<(Guid Id, HttpClient Client)> ActorAsync(string? seedKey)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "f" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            Guid groupId;
            if (seedKey.Contains(':', StringComparison.Ordinal))
            {
                var group = new Group { Name = "Grupo " + Guid.NewGuid().ToString("N")[..8], Description = "Teste" };
                db.Groups.Add(group);
                foreach (var part in seedKey.Split(';'))
                {
                    var (module, actions) = (part.Split(':')[0], part.Split(':')[1].Split(','));
                    db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
                    foreach (var action in actions)
                    {
                        db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = module, Action = action });
                    }
                }

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

    private static HttpRequestMessage Post(string url, object body, Guid? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        return request;
    }

    /// <summary>Processa o outbox do tenant até não sobrar mensagem pendente.</summary>
    private async Task DrainAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        for (var round = 0; round < 60; round++)
        {
            await using (var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info))
            {
                await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(info);
            }

            var pending = await WithDbAsync(db => db.OutboxMessages.CountAsync(m => m.ProcessedAt == null && m.DeadLetteredAt == null && m.NextAttemptAt <= DateTimeOffset.UtcNow.AddSeconds(1)));
            if (pending == 0)
            {
                var errors = await WithDbAsync(db => db.OutboxMessages.Where(m => m.ProcessedAt == null && m.DeadLetteredAt == null).Select(m => m.Type + ": " + m.Error).ToListAsync());
                errors.Should().BeEmpty("nenhum evento pode falhar na cadeia");
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("O outbox não esvaziou.");
    }

    private static async Task<Guid> CustomerAsync(HttpClient owner)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = (string?)null, legalName = "Cliente " + Guid.NewGuid().ToString("N")[..6], roles = new[] { "Customer" } });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    /// <summary>Produto acabado ativo direto no estoque (o cadastro tem os próprios testes).</summary>
    private async Task<Guid> ProductAsync(string name = "Elevador 6 paradas")
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IInventoryDb>();
        var unit = await db.Units.Where(u => u.Code == "UN").Select(u => u.Id).SingleAsync();
        var item = new BemAqui.Inventory.Domain.Item { Type = ItemType.Finished, Code = "PA-" + Guid.NewGuid().ToString("N")[..6], Name = name, UnitId = unit, Ncm = "84281000", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    /// <summary>Centro de trabalho ativo com o calendário padrão; a linha vai para o filtro da lista.</summary>
    private async Task<Guid> CenterAsync(string name, string? line = null, bool active = true)
    {
        await using var scope = await ScopeAsync();
        var calendar = await scope.ServiceProvider.GetRequiredService<CalendarService>().DefaultEntityAsync();
        var center = new WorkCenter { Name = Unique(name), Line = line, CalendarId = calendar.Id, IsActive = active, CreatedAt = DateTimeOffset.UtcNow };
        center.SetShifts([new Shift("07:00", "16:00")]);
        var db = scope.ServiceProvider.GetRequiredService<IProductionDb>();
        db.WorkCenters.Add(center);
        await db.SaveChangesAsync();
        return center.Id;
    }

    private static object Step(string name, string code, Guid? center, decimal hours = 1m, bool quality = false, string[]? checklist = null, Guid? id = null) =>
        new { id, name, code, workCenterId = center, standardHours = hours, isQualityStep = quality, requiresApproval = false, checklist = checklist ?? [] };

    /// <summary>Roteiro ativo pelo endpoint da Engenharia (Proprietário), opcionalmente vinculado ao produto.</summary>
    private static async Task<Guid> RoutingAsync(HttpClient owner, Guid center, Guid? product = null, string? name = null, params object[] steps)
    {
        object[] body = steps.Length > 0 ? steps : [Step("Corte", "C", center, 2m), Step("Montagem", "M", center, 3m, checklist: ["Conferir prumo"]), Step("Qualidade", "Q", center, 0.5m, quality: true)];
        var created = await owner.PostAsJsonAsync(Routings, new { name = name ?? Unique("Roteiro"), productType = "Elevador", steps = body });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = (await JsonAsync(created)).GetProperty("id").GetGuid();
        if (product is { } item)
        {
            (await owner.PostAsJsonAsync($"{Routings}/{id}/items", new { itemId = item })).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        return id;
    }

    /// <summary>
    /// Orçamento com os itens (tipo, produto, quantidade), enviado e aprovado (implantação opcional), e o outbox processado.
    /// Devolve o projeto e os itens do projeto na ordem.
    /// </summary>
    private async Task<(Guid Project, Guid Contract, List<Guid> Items)> ApprovedAsync(
        HttpClient owner, (string Kind, Guid? ItemId, decimal Quantity)[] lines, bool onboarding = false, Guid? routingOnItems = null, object[]? milestones = null)
    {
        var customer = await CustomerAsync(owner);
        var quote = await JsonAsync(await owner.SendAsync(Post(Quotes, new { partyId = customer, contactId = (Guid?)null, siteAddressId = (Guid?)null })));
        var id = quote.GetProperty("id").GetGuid();
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE commercial.quotes SET delivery_days = 90 WHERE id = {id}"));
        quote = await JsonAsync(await owner.GetAsync($"{Quotes}/{id}"));
        foreach (var (kind, itemId, quantity) in lines)
        {
            var added = await owner.PostAsJsonAsync($"{Quotes}/{id}/items", new { kind, itemId, description = kind + " " + Guid.NewGuid().ToString("N")[..4], quantity, unitPrice = 1000m, resetPrice = false, version = Version(quote) });
            added.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
            quote = await JsonAsync(added);
        }

        if (routingOnItems is { } routing)
        {
            // Coluna sem tela nesta fase (PRD-01 CA 22): o item do orçamento leva o roteiro.
            await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE commercial.quote_items SET routing_template_id = {routing} WHERE quote_id = {id}"));
        }

        var conditions = await owner.PutAsJsonAsync($"{Quotes}/{id}/conditions", new
        {
            milestones = milestones ?? [new { description = "Sinal", percent = 100m, trigger = "Event", triggerEvent = "Signature", dueDate = (string?)null, daysAfterEvent = 0 }],
            validUntil = Iso(Today.AddDays(15)), version = Version(quote),
        });
        conditions.StatusCode.Should().Be(HttpStatusCode.OK, await conditions.Content.ReadAsStringAsync());
        quote = await JsonAsync(await owner.GetAsync($"{Quotes}/{id}"));
        quote = await JsonAsync(await owner.PostAsJsonAsync($"{Quotes}/{id}/send", new { version = Version(quote) }));
        var preview = await JsonAsync(await owner.GetAsync($"{Quotes}/{id}/approval"));
        var account = preview.GetProperty("suggestedRevenueAccountId").GetGuid();
        object body = new { signedAt = Iso(Today.AddDays(-1)), revenueAccountId = account, version = Version(quote) };
        if (onboarding)
        {
            var cutover = Today.AddDays(-30);
            await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE finance.opening_setup SET cutover_date = {cutover}"));
            await WithDbAsync(db => db.Database.ExecuteSqlAsync($"INSERT INTO finance.opening_setup (id, cutover_date) SELECT {Guid.CreateVersion7()}, {cutover} WHERE NOT EXISTS (SELECT 1 FROM finance.opening_setup)"));
            var milestone = preview.GetProperty("milestones")[0].GetProperty("id").GetGuid();
            body = new { signedAt = Iso(Today.AddDays(-1)), revenueAccountId = account, version = Version(quote), isOnboarding = true, milestones = new object[] { new { milestoneId = milestone, dueDate = (string?)null, receivedBefore = false } } };
        }

        var approved = await owner.SendAsync(Post($"{Quotes}/{id}/approve", body));
        approved.StatusCode.Should().Be(HttpStatusCode.Created, await approved.Content.ReadAsStringAsync());
        var result = await JsonAsync(approved);
        var projectId = result.GetProperty("projectId").GetGuid();
        await DrainAsync();
        var items = await WithDbAsync(db => db.ProjectItems.AsNoTracking().Where(i => i.ProjectId == projectId).OrderBy(i => i.Sequence).Select(i => i.Id).ToListAsync());
        return (projectId, result.GetProperty("contractId").GetGuid(), items);
    }

    /// <summary>OS de fabricação não cancelada do item do projeto, direto no banco.</summary>
    private Task<ProductionOrder?> OrderOfAsync(Guid projectItemId) =>
        WithDbAsync(db => db.ProductionOrders.AsNoTracking().FirstOrDefaultAsync(o => o.ProjectItemId == projectItemId && o.Type == ProductionOrderType.Fabrication && o.Status != ProductionOrderStatus.Cancelled));

    private static async Task<JsonElement> GetOrderAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"{Base}/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    private static object StepsBody(JsonElement order, IEnumerable<object> steps) => new { steps, version = Version(order) };

    /// <summary>As etapas atuais da OS no formato do salvar (para editar a partir delas).</summary>
    private static List<object> CurrentSteps(JsonElement order) => order.GetProperty("steps").EnumerateArray().Select(s => (object)new
    {
        id = s.GetProperty("id").GetGuid(), name = s.GetProperty("name").GetString(), code = s.GetProperty("code").GetString(),
        workCenterId = s.GetProperty("workCenterId").ValueKind == JsonValueKind.Null ? (Guid?)null : s.GetProperty("workCenterId").GetGuid(),
        standardHours = s.GetProperty("standardHours").GetDecimal(), isQualityStep = s.GetProperty("isQualityStep").GetBoolean(),
        requiresApproval = s.GetProperty("requiresApproval").GetBoolean(), checklist = s.GetProperty("checklist").EnumerateArray().Select(c => c.GetString()).ToArray(),
    }).ToList();

    private Task<int> MarkStepAsync(Guid orderId, int sequence, ProductionStepStatus status) =>
        WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.production_order_steps SET status = {status.ToString()} WHERE order_id = {orderId} AND sequence = {sequence}"));
}