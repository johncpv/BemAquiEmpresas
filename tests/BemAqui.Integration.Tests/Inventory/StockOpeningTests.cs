using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Inventory;

/// <summary>Base dos testes do estoque que mudam o estado do tenant inteiro (implantação, resumo do dia, carga): banco próprio por classe.</summary>
public abstract class StockTenantTests(TenancyFixture tenancy)
{
    protected const string Host = "a.localhost";
    protected const string Base = "/api/v1/inventory";

    protected TenancyFixture Tenancy => tenancy;

    protected static DateOnly Today => BusinessTime.Today(TimeProvider.System);

    protected static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    protected static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    protected static HttpRequestMessage Command(string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return request;
    }

    protected async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    protected async Task<T> WithDbAsync<T>(Func<TenantDbContext, Task<T>> action)
    {
        await using var scope = await ScopeAsync();
        return await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    protected async Task<(Guid Id, HttpClient Client)> ActorAsync(string seedKey)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "t" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync() });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([id]);
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return (id, client);
    }

    protected async Task<(Guid Item, Guid Location)> ItemAndLocationAsync()
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var unit = await db.Units.Where(u => u.Code == "UN").Select(u => u.Id).SingleAsync();
        var item = new Item { Type = ItemType.RawMaterial, Code = "T-" + Guid.NewGuid().ToString("N")[..8], Name = "Item de teste", UnitId = unit, Ncm = "84313110", CreatedAt = DateTimeOffset.UtcNow };
        var location = new Location { Code = "T-" + Guid.NewGuid().ToString("N")[..6], Name = "Local de teste", CreatedAt = DateTimeOffset.UtcNow };
        db.Items.Add(item);
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        return (item.Id, location.Id);
    }

    protected static object Opening(Guid item, Guid location, decimal quantity = 2m, decimal cost = 5m) =>
        new { cutoffDate = Iso(Today), itemId = item, locationId = location, quantity, unitId = (Guid?)null, unitCost = cost, lotNumber = (string?)null };
}

/// <summary>EST-01a CA 3 e 5: "Concluir implantação do estoque" fecha a janela do saldo inicial e não se desfaz.</summary>
public class StockOpeningTests(TenancyFixture tenancy) : StockTenantTests(tenancy), IClassFixture<TenancyFixture>
{
    [Fact]
    public async Task Concluir_implantacao_fecha_o_saldo_inicial_e_nao_se_desfaz()
    {
        var (_, warehouse) = await ActorAsync("warehouse");
        var (ownerId, owner) = await ActorAsync("owner");
        var (item, location) = await ItemAndLocationAsync();
        (await warehouse.SendAsync(Command($"{Base}/opening-balances", Opening(item, location, 4m, 2.5m)))).StatusCode.Should().Be(HttpStatusCode.Created);
        var status = await JsonAsync(await owner.GetAsync($"{Base}/opening"));
        status.GetProperty("completed").GetBoolean().Should().BeFalse();
        status.GetProperty("itemCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        status.GetProperty("totalValue").GetDecimal().Should().BeGreaterThanOrEqualTo(10m);

        (await warehouse.PostAsJsonAsync($"{Base}/opening/complete", new { version = status.GetProperty("version").GetUInt32() })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "só com inventory.approve");
        var done = await owner.PostAsJsonAsync($"{Base}/opening/complete", new { version = status.GetProperty("version").GetUInt32() });
        done.StatusCode.Should().Be(HttpStatusCode.OK, await done.Content.ReadAsStringAsync());
        var closed = await JsonAsync(done);
        closed.GetProperty("completed").GetBoolean().Should().BeTrue();
        closed.GetProperty("completedByName").ValueKind.Should().Be(JsonValueKind.String);
        (await WithDbAsync(db => db.InventoryParameters.AsNoTracking().SingleAsync())).OpeningCompletedBy.Should().Be(ownerId);

        var twice = await owner.PostAsJsonAsync($"{Base}/opening/complete", new { version = closed.GetProperty("version").GetUInt32() });
        twice.StatusCode.Should().Be(HttpStatusCode.Conflict, "o fechamento não se desfaz");

        var (other, otherLocation) = await ItemAndLocationAsync();
        var late = await warehouse.SendAsync(Command($"{Base}/opening-balances", Opening(other, otherLocation)));
        late.StatusCode.Should().Be(HttpStatusCode.Conflict, "CA 3");
        (await JsonAsync(late)).GetProperty("detail").GetString().Should().Be(
            $"A implantação do estoque foi concluída em {BusinessTime.ToBusiness(closed.GetProperty("completedAt").GetDateTimeOffset()):dd/MM/yyyy}. Use um ajuste de estoque.");
        var lateBatch = await warehouse.SendAsync(Command($"{Base}/opening-balances/batch", new { cutoffDate = Iso(Today), lines = new[] { new { itemId = other, locationId = otherLocation, quantity = 1m, unitId = (Guid?)null, unitCost = 1m, lotNumber = (string?)null } } }));
        lateBatch.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await WithDbAsync(db => db.StockMovements.CountAsync(m => m.ItemId == other))).Should().Be(0, "nada é gravado");
    }
}

/// <summary>EST-01a CA 12: resumo diário dos lançamentos manuais ao Proprietário, idempotente por tenant e dia.</summary>
public class StockDigestTests(TenancyFixture tenancy) : StockTenantTests(tenancy), IClassFixture<TenancyFixture>
{
    [Fact]
    public async Task Resumo_do_dia_vai_ao_Proprietario_uma_vez_e_dia_sem_lancamento_nao_avisa()
    {
        var (_, warehouse) = await ActorAsync("warehouse");
        var (ownerId, _) = await ActorAsync("owner");
        var (item, location) = await ItemAndLocationAsync();
        var receipt = await JsonAsync(await warehouse.SendAsync(Command($"{Base}/manual-postings", new { kind = "Receipt", itemId = item, locationId = location, toLocationId = (Guid?)null, quantity = 10m, unitId = (Guid?)null, unitCost = 10m, lotId = (Guid?)null, lotNumber = (string?)null, reason = "Doação", projectId = (Guid?)null })));
        (await warehouse.SendAsync(Command($"{Base}/manual-postings", new { kind = "Issue", itemId = item, locationId = location, toLocationId = (Guid?)null, quantity = 4m, unitId = (Guid?)null, unitCost = (decimal?)null, lotId = (Guid?)null, lotNumber = (string?)null, reason = "Perda", projectId = (Guid?)null }))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await warehouse.SendAsync(Command($"{Base}/movements/{receipt.GetProperty("lines")[0].GetProperty("movementId").GetGuid()}/reverse", new { reason = "Errado" }))).StatusCode.Should().Be(HttpStatusCode.Conflict, "saldo 6 não cobre o estorno de 10");

        await using var scope = await ScopeAsync();
        var notifier = scope.ServiceProvider.GetRequiredService<ManualDigestNotifier>();
        (await notifier.NotifyAsync(Today)).Should().Be(2);
        (await notifier.NotifyAsync(Today)).Should().Be(2, "rodar de novo não duplica o aviso");
        var titles = await WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.UserId == ownerId && n.Type == ManualDigestNotifier.Type).Select(n => new { n.Title, n.Link }).ToListAsync());
        titles.Should().ContainSingle();
        titles[0].Title.Should().StartWith($"Lançamentos manuais de {Today:dd/MM/yyyy}: 1 entradas (R$ 100,00), 1 saídas (R$ 40,00), 0 estornos (R$ 0,00).");
        titles[0].Link.Should().Be($"/app/estoque/movimentos?origem=Manual&de={Iso(Today)}&ate={Iso(Today)}");
        (await notifier.NotifyAsync(Today.AddDays(-30))).Should().Be(0, "dia sem lançamento manual não gera aviso");
    }
}

/// <summary>EST-01a CA 24 e 28: Saldos com 50 mil posições e kardex com 10 mil movimentos, paginados em menos de 1 s.</summary>
public class StockPerformanceTests(TenancyFixture tenancy) : StockTenantTests(tenancy), IClassFixture<TenancyFixture>
{
    [Fact]
    [Trait(LoadTest.Trait, LoadTest.Value)]
    public async Task Saldos_com_50_mil_posicoes_e_kardex_com_10_mil_movimentos_abaixo_de_1_segundo()
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var unit = await db.Units.Where(u => u.Code == "UN").Select(u => u.Id).SingleAsync();
        var (item, location) = await ItemAndLocationAsync();
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO inventory.items (id, type, code, name, search_text, unit_id, origin, min_stock, tracks_lot, tracks_serial, average_cost, is_active, created_at, is_deleted)
            SELECT gen_random_uuid(), 'Component', 'SALDO-' || g, 'Peça ' || g, 'saldo-' || g || ' peca ' || g, {unit}, 0, 5, false, false, 1, true, now(), false
            FROM generate_series(1, 50000) g;
            INSERT INTO inventory.stock_positions (id, item_id, location_id, quantity_on_hand, quantity_reserved)
            SELECT gen_random_uuid(), i.id, {location}, (random() * 10)::numeric(18,4), 0 FROM inventory.items i WHERE i.code LIKE 'SALDO-%';
            INSERT INTO inventory.stock_movements (id, number, type, item_id, location_id, quantity, unit_cost, total_cost, balance_after, average_cost_after, source_type, source_id, leg, origin, at, posted_at)
            SELECT gen_random_uuid(), 'MOV-2026-' || lpad(g::text, 6, '0'), 'Receipt', {item}, {location}, 1, 1, 1, g, 1, 'GoodsReceipt', gen_random_uuid(), 1, 'Document', now(), now()
            FROM generate_series(1, 10000) g;
            """);
        await db.Database.ExecuteSqlRawAsync("ANALYZE inventory.items; ANALYZE inventory.stock_positions; ANALYZE inventory.stock_movements;");
        var queries = scope.ServiceProvider.GetRequiredService<StockQueryService>();
        await queries.BalancesAsync(new BalanceListQuery(null, null, null, null, null), new PageRequest());
        await queries.KardexAsync(item, null, new PageRequest(1, 50));

        var watch = Stopwatch.StartNew();
        var balances = await queries.BalancesAsync(new BalanceListQuery(null, location, null, null, null), new PageRequest(1, 50));
        var low = await queries.BalancesAsync(new BalanceListQuery(null, null, null, null, StockSituation.Low), new PageRequest(1, 50));
        watch.Stop();
        balances.Total.Should().BeGreaterThanOrEqualTo(50000);
        low.Items.Should().NotBeEmpty();
        (watch.ElapsedMilliseconds / 2).Should().BeLessThan(1000, "Saldos com 50 mil posições em menos de 1 s (CA 28)");

        watch.Restart();
        var last = await queries.KardexAsync(item, null, new PageRequest(200, 50));
        watch.Stop();
        last.Value.Total.Should().Be(10000);
        last.Value.Items.Select(r => r.PostedSeq).Should().BeInAscendingOrder();
        watch.ElapsedMilliseconds.Should().BeLessThan(1000, "kardex de 10 mil movimentos paginado em menos de 1 s");
    }
}
