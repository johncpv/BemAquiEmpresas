using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Engineering.Contracts;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using BemAqui.Production.Contracts;
using BemAqui.Purchasing.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Inventory;

/// <summary>
/// EST-06 com PostgreSQL real: o disponível vem da posição agregada (entrada pelo motor e reserva pela reconciliação da EST-02), a BOM, a
/// OS e os pedidos em aberto vêm de fakes dos contratos (<see cref="IBoms"/>, <see cref="IProductionOrders"/>, <see cref="IPurchaseOrders"/>).
/// Banco desta classe; cada teste cria os próprios itens, OS e BOMs e filtra pelos ids que criou.
/// </summary>
public class ProductionCapacityTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Url = "/api/v1/inventory/production-capacity";

    private static readonly ConcurrentDictionary<Guid, ProductionOrderInfo> Orders = new();
    private static readonly ConcurrentDictionary<Guid, ReleasedBomInfo> Boms = new();
    private static readonly ConcurrentDictionary<Guid, decimal> Open = new();

    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory => _factory ??= tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
    {
        s.AddScoped<IBoms, FakeBoms>();
        s.AddScoped<IProductionOrders, FakeOrders>();
        s.AddScoped<IPurchaseOrders, FakePurchaseOrders>();
    }));

    private static string Unique(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    /// <summary>Usuário num grupo com as ações dadas (<c>modulo.acao</c>); sem nenhuma, sem grupo.</summary>
    private async Task<HttpClient> ActorAsync(params string[] permissions)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "c" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (permissions.Length > 0)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            var group = new Group { Name = "Capacidade " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
            db.Groups.Add(group);
            foreach (var module in permissions.Select(p => p.Split('.')[0]).Distinct())
            {
                db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
            }

            foreach (var permission in permissions)
            {
                db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = permission.Split('.')[0], Action = permission.Split('.')[1] });
            }

            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = group.Id });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([id]);
        }

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return client;
    }

    private Task<HttpClient> ViewerAsync(params string[] extra) => ActorAsync(["inventory.view", "engineering.view", .. extra]);

    private async Task<Guid> UnitAsync(string code)
    {
        await using var scope = await ScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Units.Where(u => u.Code == code).Select(u => u.Id).SingleAsync();
    }

    private async Task<Guid> ItemAsync(string unit = "UN", bool active = true, decimal averageCost = 0m, ItemType type = ItemType.RawMaterial)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var unitId = await db.Units.Where(u => u.Code == unit).Select(u => u.Id).SingleAsync();
        var item = new Item { Type = type, Code = Unique("MP"), Name = "Material " + Guid.NewGuid().ToString("N")[..4], UnitId = unitId, Ncm = "84313110", IsActive = active, CreatedAt = DateTimeOffset.UtcNow };
        if (averageCost > 0)
        {
            item.ApplyAverageCost(averageCost);
        }

        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    private async Task ConversionAsync(Guid item, string from, string to, decimal factor)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var units = await db.Units.Where(u => u.Code == from || u.Code == to).ToDictionaryAsync(u => u.Code, u => u.Id);
        db.ItemUnitConversions.Add(new ItemUnitConversion { ItemId = item, FromUnitId = units[from], ToUnitId = units[to], Factor = factor, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> DefaultLocationAsync()
    {
        await using var scope = await ScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Locations.Where(l => l.IsDefault).Select(l => l.Id).SingleAsync();
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

    /// <summary>Entrada pelo motor, numa transação própria (como o recebimento faria).</summary>
    private async Task ReceiveAsync(Guid item, Guid location, decimal quantity)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var result = await scope.ServiceProvider.GetRequiredService<IStockMovements>()
            .PostAsync(new StockPosting(StockMovementType.Receipt, item, location, null, quantity, 1m, StockSources.GoodsReceipt, Guid.NewGuid()));
        result.IsSuccess.Should().BeTrue(result.Error?.Message);
        await transaction.CommitAsync();
    }

    /// <summary>Reserva de verdade (EST-02): a reconciliação da OS a partir da BOM liberada do fake.</summary>
    private async Task ReserveAsync(Guid order)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await scope.ServiceProvider.GetRequiredService<StockReservationService>().ReconcileOrderAsync(order, "Teste EST-06");
        await transaction.CommitAsync();
    }

    /// <summary>OS de fabricação (quantidade 1 por padrão) com a BOM liberada; cada linha com a quantidade por unidade, a unidade e a perda.</summary>
    private static Guid OrderWithBom(Guid? product, DateTimeOffset releasedAt, decimal orderQuantity = 1m, ProductionOrderState status = ProductionOrderState.Open,
        params (Guid Item, decimal PerUnit, Guid Unit, decimal Scrap)[] lines)
    {
        var id = Guid.CreateVersion7();
        Orders[id] = new ProductionOrderInfo(id, "OS-2026-" + Random.Shared.Next(10000, 99999).ToString(System.Globalization.CultureInfo.InvariantCulture), ProductionOrderKind.Fabrication,
            Guid.CreateVersion7(), Guid.CreateVersion7(), product, orderQuantity, status, null, null);
        Boms[id] = new ReleasedBomInfo(Guid.CreateVersion7(), id, 0, releasedAt,
            lines.Select(l => new BomRequirement(Guid.CreateVersion7(), l.Item, Math.Round(l.PerUnit * orderQuantity * (1 + (l.Scrap / 100m)), 4), l.PerUnit, l.Unit, l.Scrap)).ToList());
        return id;
    }

    private static JsonElement Line(JsonElement view, Guid item) => view.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("itemId").GetGuid() == item);

    private static decimal? Dec(JsonElement element, string name) => element.GetProperty(name).ValueKind == JsonValueKind.Null ? null : element.GetProperty(name).GetDecimal();

    [Fact]
    public async Task Disponivel_desconta_a_reserva_e_o_menor_item_limita()
    {
        var un = await UnitAsync("UN");
        var (cable, guide) = (await ItemAsync(), await ItemAsync());
        var (alm, other) = (await DefaultLocationAsync(), await LocationAsync());
        await ReceiveAsync(cable, alm, 8m);
        await ReceiveAsync(cable, other, 2m);
        await ReceiveAsync(guide, alm, 9m);
        // Outra OS aberta reserva 4 do cabo: o disponível de hoje cai de 10 para 6.
        var busy = OrderWithBom(null, DateTimeOffset.UtcNow.AddHours(-3), lines: [(cable, 4m, un, 0m)]);
        await ReserveAsync(busy);
        var reference = OrderWithBom(Guid.CreateVersion7(), DateTimeOffset.UtcNow.AddHours(-2), lines: [(cable, 2m, un, 0m), (guide, 2m, un, 10m)]);

        var view = await JsonAsync(await (await ViewerAsync()).GetAsync($"{Url}?productionOrderId={reference}"));
        view.GetProperty("quantity").GetDecimal().Should().Be(1m, "CA 2: quantidade padrão 1");
        var c = Line(view, cable);
        (c.GetProperty("onHand").GetDecimal(), c.GetProperty("reserved").GetDecimal(), c.GetProperty("available").GetDecimal()).Should().Be((10m, 4m, 6m), "CA 1: todos os locais, sem o reservado");
        (Dec(c, "perUnit"), Dec(c, "possible")).Should().Be((2m, 3m));
        var g = Line(view, guide);
        (Dec(g, "perUnit"), Dec(g, "possible")).Should().Be((2.2m, 4m), "com a perda de 10%");
        view.GetProperty("possible").GetDecimal().Should().Be(3m, "o menor dos itens");
        view.GetProperty("limitingItemId").GetGuid().Should().Be(cable, "\"Limita: {item}\"");
        c.GetProperty("limiting").GetBoolean().Should().BeTrue();
        view.GetProperty("reference").GetProperty("productionOrderId").GetGuid().Should().Be(reference);

        var five = await JsonAsync(await (await ViewerAsync()).GetAsync($"{Url}?productionOrderId={reference}&quantity=5"));
        (Dec(Line(five, cable), "required"), Dec(Line(five, cable), "shortage")).Should().Be((10m, 4m), "CA 2: 2 × 5 − 6");
        (Dec(Line(five, guide), "required"), Dec(Line(five, guide), "shortage")).Should().Be((11m, 2m));
        five.GetProperty("shortageCount").GetInt32().Should().Be(2);
        five.GetProperty("costShown").GetBoolean().Should().BeFalse();
        five.GetProperty("estimatedShortageCost").ValueKind.Should().Be(JsonValueKind.Null, "sem projects.view_cost não há valor");
    }

    [Fact]
    public async Task Produto_usa_a_ultima_BOM_liberada_com_Sugerido()
    {
        var un = await UnitAsync("UN");
        var item = await ItemAsync();
        await ReceiveAsync(item, await DefaultLocationAsync(), 6m);
        var product = Guid.CreateVersion7();
        var old = OrderWithBom(product, DateTimeOffset.UtcNow.AddDays(-10), status: ProductionOrderState.Completed, lines: [(item, 3m, un, 0m)]);
        var latest = OrderWithBom(product, DateTimeOffset.UtcNow.AddDays(-1), status: ProductionOrderState.Completed, lines: [(item, 2m, un, 0m)]);
        var viewer = await ViewerAsync();

        var view = await JsonAsync(await viewer.GetAsync($"{Url}?productId={product}"));
        view.GetProperty("reference").GetProperty("productionOrderId").GetGuid().Should().Be(latest, "CA 1: a última do produto, mesmo de OS concluída");
        view.GetProperty("reference").GetProperty("suggested").GetBoolean().Should().BeTrue("tag \"Sugerido\"");
        view.GetProperty("possible").GetDecimal().Should().Be(3m);

        var references = (await JsonAsync(await viewer.GetAsync($"{Url}/references?productId={product}"))).EnumerateArray().ToList();
        references.Select(r => r.GetProperty("productionOrderId").GetGuid()).Should().Equal(latest, old);
        references.Select(r => r.GetProperty("suggested").GetBoolean()).Should().Equal(true, false);
        var chosen = await JsonAsync(await viewer.GetAsync($"{Url}?productionOrderId={old}"));
        chosen.GetProperty("reference").GetProperty("suggested").GetBoolean().Should().BeFalse("escolhida à mão");
        chosen.GetProperty("possible").GetDecimal().Should().Be(2m);

        var none = await viewer.GetAsync($"{Url}?productId={Guid.CreateVersion7()}");
        none.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeAsync(none)).Should().Be("capacity_reference_not_found");
        var noBom = Guid.CreateVersion7();
        Orders[noBom] = Orders[old] with { Id = noBom };
        var withoutBom = await viewer.GetAsync($"{Url}?productionOrderId={noBom}");
        withoutBom.StatusCode.Should().Be(HttpStatusCode.NotFound, "CA 6: a BOM é validada pelo IBoms");
        (await CodeAsync(withoutBom)).Should().Be("bom_not_released");
        (await viewer.GetAsync($"{Url}?productionOrderId={latest}&quantity=0")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "validação é 422");
    }

    [Fact]
    public async Task Pedidos_em_aberto_so_com_purchasing_view_e_o_modulo_liberado()
    {
        var un = await UnitAsync("UN");
        var item = await ItemAsync();
        await ReceiveAsync(item, await DefaultLocationAsync(), 4m);
        Open[item] = 6m;
        var order = OrderWithBom(null, DateTimeOffset.UtcNow.AddHours(-1), lines: [(item, 2m, un, 0m)]);

        var withPurchasing = await JsonAsync(await (await ViewerAsync("purchasing.view")).GetAsync($"{Url}?productionOrderId={order}&quantity=5"));
        withPurchasing.GetProperty("openOrdersShown").GetBoolean().Should().BeTrue();
        withPurchasing.GetProperty("possibleWithOpenOrders").GetDecimal().Should().Be(5m, "CA 3: (4 + 6) ÷ 2");
        withPurchasing.GetProperty("possible").GetDecimal().Should().Be(2m, "ponto 92: o total continua só com o disponível de hoje");
        Dec(Line(withPurchasing, item), "shortage").Should().Be(6m, "a falta também");
        Dec(Line(withPurchasing, item), "openQuantity").Should().Be(6m);

        var without = await JsonAsync(await (await ViewerAsync()).GetAsync($"{Url}?productionOrderId={order}&quantity=5"));
        without.GetProperty("openOrdersShown").GetBoolean().Should().BeFalse("sem purchasing.view");
        without.GetProperty("possibleWithOpenOrders").ValueKind.Should().Be(JsonValueKind.Null);
        Line(without, item).GetProperty("openQuantity").ValueKind.Should().Be(JsonValueKind.Null);
        without.GetProperty("possible").GetDecimal().Should().Be(2m, "o resto funciona");

        var buyer = await ViewerAsync("purchasing.view");
        await SetReleasedAsync("purchasing", false);
        try
        {
            var notReleased = await JsonAsync(await buyer.GetAsync($"{Url}?productionOrderId={order}&quantity=5"));
            notReleased.GetProperty("openOrdersShown").GetBoolean().Should().BeFalse("módulo purchasing não liberado");
            notReleased.GetProperty("possible").GetDecimal().Should().Be(2m);
        }
        finally
        {
            await SetReleasedAsync("purchasing", true);
        }
    }

    [Fact]
    public async Task Item_inativo_ou_sem_conversao_fica_nao_calculado_sem_derrubar_o_resto()
    {
        var (un, m) = (await UnitAsync("UN"), await UnitAsync("M"));
        var (ok, inactive, noConversion, converted) = (await ItemAsync(), await ItemAsync(active: false), await ItemAsync("KG"), await ItemAsync("KG"));
        await ConversionAsync(converted, "M", "KG", 2m);
        var alm = await DefaultLocationAsync();
        await ReceiveAsync(ok, alm, 10m);
        await ReceiveAsync(converted, alm, 9m);
        var order = OrderWithBom(null, DateTimeOffset.UtcNow.AddHours(-1), lines: [(ok, 1m, un, 0m), (inactive, 1m, un, 0m), (noConversion, 1m, m, 0m), (converted, 1.5m, m, 0m)]);

        var view = await JsonAsync(await (await ViewerAsync()).GetAsync($"{Url}?productionOrderId={order}"));
        Line(view, inactive).GetProperty("warning").GetString().Should().StartWith("Item inativo");
        Dec(Line(view, inactive), "possible").Should().BeNull("CA 5: \"não calculado\"");
        Line(view, noConversion).GetProperty("warning").GetString().Should().Contain("Sem conversão de M para KG");
        Dec(Line(view, noConversion), "perUnit").Should().BeNull();
        (Dec(Line(view, converted), "perUnit"), Dec(Line(view, converted), "possible")).Should().Be((3m, 3m), "1,5 M × 2 KG");
        view.GetProperty("notCalculatedCount").GetInt32().Should().Be(2);
        view.GetProperty("possible").GetDecimal().Should().Be(3m, "o resto calcula");
        view.GetProperty("limitingItemId").GetGuid().Should().Be(converted);
    }

    [Fact]
    public async Task Valor_da_falta_so_com_projects_view_cost()
    {
        var un = await UnitAsync("UN");
        var item = await ItemAsync(averageCost: 12.5m);
        var order = OrderWithBom(null, DateTimeOffset.UtcNow.AddHours(-1), lines: [(item, 2m, un, 0m)]);

        var view = await JsonAsync(await (await ViewerAsync("projects.view_cost")).GetAsync($"{Url}?productionOrderId={order}&quantity=3"));
        view.GetProperty("costShown").GetBoolean().Should().BeTrue();
        Dec(Line(view, item), "estimatedShortageCost").Should().Be(75m, "falta 6 × 12,50 pelo custo médio");
        view.GetProperty("estimatedShortageCost").GetDecimal().Should().Be(75m);
        view.GetProperty("possible").GetDecimal().Should().Be(0m);
    }

    [Fact]
    public async Task Tres_camadas()
    {
        var un = await UnitAsync("UN");
        var item = await ItemAsync();
        var order = OrderWithBom(null, DateTimeOffset.UtcNow.AddHours(-1), lines: [(item, 1m, un, 0m)]);
        var url = $"{Url}?productionOrderId={order}";

        var nobody = await ActorAsync();
        var noGroup = await nobody.GetAsync(url);
        noGroup.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(noGroup)).Should().Be("forbidden");
        (await (await ActorAsync("inventory.view")).GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "sem engineering.view");
        (await (await ActorAsync("engineering.view")).GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "sem inventory.view");
        (await (await ActorAsync("engineering.view")).GetAsync($"{Url}/references")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var viewer = await ViewerAsync();
        foreach (var module in new[] { "inventory", "engineering" })
        {
            await SetReleasedAsync(module, false);
            try
            {
                var notReleased = await viewer.GetAsync(url);
                notReleased.StatusCode.Should().Be(HttpStatusCode.Forbidden, module);
                (await CodeAsync(notReleased)).Should().Be("module_not_released", module);
            }
            finally
            {
                await SetReleasedAsync(module, true);
            }
        }

        (await viewer.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
        var endpoints = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw && raw.Contains("/production-capacity", StringComparison.Ordinal)).ToList();
        endpoints.Should().HaveCount(2);
        foreach (var endpoint in endpoints)
        {
            endpoint.Metadata.GetOrderedMetadata<RequiresModuleMetadata>().Select(m => m.ModuleCode).Should().Equal("inventory"); // o engineering é conferido pela perm: e pelo serviço
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).Should().Contain("perm:inventory.view").And.Contain("perm:engineering.view");
        }
    }

    [Fact]
    public async Task BOM_com_100_itens_responde_em_menos_de_1_segundo()
    {
        var un = await UnitAsync("UN");
        var alm = await DefaultLocationAsync();
        var items = new List<Guid>();
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            for (var i = 0; i < 100; i++)
            {
                var item = new Item { Type = ItemType.RawMaterial, Code = Unique("P"), Name = "Peça " + i, UnitId = un, Ncm = "84313110", CreatedAt = DateTimeOffset.UtcNow };
                db.Items.Add(item);
                items.Add(item.Id);
            }

            await db.SaveChangesAsync();
        }

        foreach (var item in items.Take(10))
        {
            await ReceiveAsync(item, alm, 50m);
        }

        foreach (var item in items)
        {
            Open[item] = 3m;
        }

        var order = OrderWithBom(null, DateTimeOffset.UtcNow.AddHours(-1), lines: items.Select(i => (i, 1m, un, 5m)).ToArray());
        var viewer = await ViewerAsync("purchasing.view", "projects.view_cost");
        var url = $"{Url}?productionOrderId={order}&quantity=10";
        (await JsonAsync(await viewer.GetAsync(url))).GetProperty("lines").GetArrayLength().Should().Be(100);

        var best = TimeSpan.MaxValue;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var watch = Stopwatch.StartNew();
            (await viewer.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
            watch.Stop();
            best = watch.Elapsed < best ? watch.Elapsed : best;
        }

        best.Should().BeLessThan(TimeSpan.FromSeconds(1), "CA 7: saldos e pedidos em aberto em lote (o melhor de 3, com a suíte em carga)");
    }

    private async Task SetReleasedAsync(string module, bool released)
    {
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == module)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    private sealed class FakeBoms : IBoms
    {
        public Task<ReleasedBomInfo?> ReleasedForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Boms.TryGetValue(productionOrderId, out var bom) ? bom : null);

        public Task<bool> IsReleasedAsync(Guid productionOrderId, CancellationToken cancellationToken = default) => Task.FromResult(Boms.ContainsKey(productionOrderId));

        public Task<IReadOnlyList<ReleasedBomReference>> ReleasedAsync(int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReleasedBomReference>>(Boms.Values.OrderByDescending(b => b.ReleasedAt).Take(take)
                .Select(b => new ReleasedBomReference(b.BomId, b.ProductionOrderId, b.Revision, b.ReleasedAt, b.Lines.Count)).ToList());
    }

    private sealed class FakeOrders : IProductionOrders
    {
        public Task<ProductionOrderInfo?> FindAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Orders.GetValueOrDefault(id));

        public Task<IReadOnlyList<ProductionOrderInfo>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductionOrderInfo>>(ids.Where(Orders.ContainsKey).Select(id => Orders[id]).ToList());

        public Task<IReadOnlyList<ProductionOrderInfo>> ByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductionOrderInfo>>(Orders.Values.Where(o => o.ProjectId == projectId).ToList());

        public Task<ProductionOrderState?> StatusAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Orders.TryGetValue(id, out var o) ? o.Status : (ProductionOrderState?)null);

        public Task<IReadOnlyList<ProductionOrderInfo>> OpenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductionOrderInfo>>(Orders.Values.Where(o => o.Status is not (ProductionOrderState.Completed or ProductionOrderState.Cancelled)).ToList());
    }

    private sealed class FakePurchaseOrders : IPurchaseOrders
    {
        public Task<PurchaseOrderInfo?> FindAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<PurchaseOrderInfo?>(null);

        public Task<IReadOnlyDictionary<Guid, decimal>> OpenQuantityByItemAsync(IReadOnlyCollection<Guid> itemIds, PurchaseOrderDestination destination, CancellationToken cancellationToken = default)
        {
            destination.AllDestinations.Should().BeTrue("EST-06: saldo a receber de todos os destinos");
            return Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(itemIds.Where(Open.ContainsKey).ToDictionary(i => i, i => Open[i]));
        }

        public Task<IReadOnlyList<PurchaseCommitment>> CommittedByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PurchaseCommitment>>([]);

        public Task<LastPurchaseInfo?> LastPurchaseAsync(Guid itemId, CancellationToken cancellationToken = default) => Task.FromResult<LastPurchaseInfo?>(null);
    }
}
