using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Catalog.Contracts;
using BemAqui.Pos.Application;
using BemAqui.Pos.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Pos;

/// <summary>
/// OFF-01 CA 9 a 12: modelo de leitura do caixa (<c>pos.catalog_items</c>) pelos eventos do Catálogo e do Financeiro, foto e delta por cursor no
/// esquema Device, carga inicial das lojas e o isolamento entre lojas (preço e operador), sem custo nem margem.
/// </summary>
public partial class PosTests
{
    private async Task DrainOutboxAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        for (var round = 0; round < 40; round++)
        {
            await using (var scope = await ScopeAsync())
            {
                await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(info);
            }

            if (await WithDbAsync(db => db.OutboxMessages.CountAsync(m => m.ProcessedAt == null && m.DeadLetteredAt == null && m.NextAttemptAt <= DateTimeOffset.UtcNow.AddSeconds(1))) == 0)
            {
                return;
            }

            await Task.Delay(250);
        }
    }

    private static async Task<Guid> ProductAsync(HttpClient owner, string name, decimal price, string? gtin = null)
    {
        var unit = (await OkAsync(await owner.GetAsync("/api/v1/catalog/units"))).EnumerateArray().First(u => u.GetProperty("code").GetString() == "UN").GetProperty("id").GetGuid();
        var created = await OkAsync(await owner.PostAsJsonAsync("/api/v1/catalog/products", new
        {
            saleName = name, categoryId = (Guid?)null, itemId = (Guid?)null, newItem = new { code = (string?)null, unitId = unit, ncm = (string?)null }, plu = (string?)null,
            isWeighable = false, sellablePos = true, sellableOnline = false, price, barcodes = gtin is null ? Array.Empty<object>() : [new { gtin, packQuantity = 1m }],
        }), HttpStatusCode.Created);
        return created.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> SnapshotAsync(Device device) =>
        await OkAsync(await Anonymous().SendAsync(SignedGet(device, "/api/v1/pos/sync/snapshot")));

    private static JsonElement? Item(JsonElement collection, Guid productId) =>
        collection.EnumerateArray().Where(i => i.GetProperty("productId").GetGuid() == productId).Select(i => (JsonElement?)i).FirstOrDefault();

    /// <summary>GTIN-13 válido e único (prefixo 789 + 9 dígitos aleatórios + dígito verificador).</summary>
    private static string Gtin13()
    {
        var digits = "789" + string.Concat(Enumerable.Range(0, 9).Select(_ => Random.Shared.Next(10).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var sum = digits.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum();
        return digits + ((10 - (sum % 10)) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task Foto_e_delta_por_cursor_com_consumidor_idempotente_e_produto_desativado_como_removido()
    {
        await CommerceAsync(true);
        var (owner, store, device) = await RegisterAsync();
        using var registered = device;
        var gtin = Gtin13();
        var p1 = await ProductAsync(owner, Unique("Café "), 12.90m, gtin);
        var p2 = await ProductAsync(owner, Unique("Açúcar "), 5m);
        await DrainOutboxAsync();

        // CA 10: foto da loja com a lista fechada de coleções, os parâmetros da loja (PP-10 e PP-22) e o preço em centavos.
        var snapshot = await SnapshotAsync(device);
        snapshot.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        snapshot.GetProperty("collections").EnumerateArray().Select(c => c.GetString()).Should().Equal("store", "priceList", "items", "paymentMethods", "operators", "printers");
        var storeJson = snapshot.GetProperty("store");
        storeJson.GetProperty("id").GetGuid().Should().Be(store);
        storeJson.GetProperty("offlineWindowHours").GetInt32().Should().Be(168);
        storeJson.GetProperty("cashBreakToleranceCents").GetInt64().Should().Be(500);
        storeJson.GetProperty("exchangeDays").GetInt32().Should().Be(30);
        storeJson.GetProperty("cashDropAlertCents").GetInt64().Should().Be(100000);
        snapshot.GetProperty("printers").GetProperty("version").GetInt32().Should().Be(0);
        snapshot.GetProperty("printers").GetProperty("items").GetArrayLength().Should().Be(0);
        var first = Item(snapshot.GetProperty("items"), p1)!.Value;
        first.GetProperty("priceCents").GetInt64().Should().Be(1290);
        first.GetProperty("barcodes")[0].GetProperty("gtin").GetString().Should().Be(gtin);
        Item(snapshot.GetProperty("items"), p2).Should().NotBeNull();
        snapshot.GetProperty("priceList").GetProperty("version").GetInt64().Should().BeGreaterThan(0);
        var cursor = snapshot.GetProperty("cursor").GetInt64();

        // CA 9: o mesmo evento de novo (e fora de ordem) não muda a linha nem o change_seq.
        var before = await WithDbAsync(db => db.CatalogItems.AsNoTracking().SingleAsync(i => i.StoreId == store && i.Kind == CatalogItemKind.Product && i.EntityId == p1));
        await using (var scope = await ScopeAsync())
        {
            var projector = scope.ServiceProvider.GetRequiredService<PosCatalogProjector>();
            await projector.HandleAsync(new ProductChanged(p1), CancellationToken.None);
            await projector.HandleAsync(new ProductChanged(p1), CancellationToken.None);
        }

        var after = await WithDbAsync(db => db.CatalogItems.AsNoTracking().SingleAsync(i => i.StoreId == store && i.Kind == CatalogItemKind.Product && i.EntityId == p1));
        after.ChangeSeq.Should().Be(before.ChangeSeq);
        after.Payload.Should().Be(before.Payload);
        (await OkAsync(await Anonymous().SendAsync(SignedGet(device, $"/api/v1/pos/sync/changes?cursor={cursor}")))).GetProperty("items").GetArrayLength().Should().Be(0);

        // CA 11: preço novo e produto desativado chegam no delta; o desativado como removido.
        await OkAsync(await owner.PutAsJsonAsync($"/api/v1/catalog/products/{p1}/price", new { price = 13.50m, priceListId = (Guid?)null, applyToVariants = false }));
        await OkAsync(await owner.PostAsync($"/api/v1/catalog/products/{p2}/inactivate", null));
        await DrainOutboxAsync();
        var changes = await OkAsync(await Anonymous().SendAsync(SignedGet(device, $"/api/v1/pos/sync/changes?cursor={cursor}")));
        Item(changes.GetProperty("items"), p1)!.Value.GetProperty("priceCents").GetInt64().Should().Be(1350);
        changes.GetProperty("removedItemIds").EnumerateArray().Select(i => i.GetGuid()).Should().Contain(p2);
        Item(changes.GetProperty("items"), p2).Should().BeNull();
        changes.GetProperty("hasMore").GetBoolean().Should().BeFalse();
        var next = changes.GetProperty("cursor").GetInt64();
        next.Should().BeGreaterThan(cursor);
        var empty = await OkAsync(await Anonymous().SendAsync(SignedGet(device, $"/api/v1/pos/sync/changes?cursor={next}")));
        empty.GetProperty("items").GetArrayLength().Should().Be(0);
        empty.GetProperty("removedItemIds").GetArrayLength().Should().Be(0);
        empty.GetProperty("cursor").GetInt64().Should().Be(next);

        // Cursor à frente do servidor (outra loja ou banco restaurado): baixa a foto de novo.
        var ahead = await Anonymous().SendAsync(SignedGet(device, $"/api/v1/pos/sync/changes?cursor={next + 1_000_000}"));
        ahead.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(ahead)).Should().Be("snapshot_required");

        // Sem o aparelho (usuário logado), a rota de sincronização não responde.
        (await owner.GetAsync("/api/v1/pos/sync/snapshot")).IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task Aparelho_da_loja_A_nao_recebe_preco_nem_operador_da_loja_B_e_custo_e_margem_nunca_descem()
    {
        await CommerceAsync(true);
        var owner = (await OwnerAsync(otp: true)).Client;

        // Produto criado antes das lojas e sem rodar o outbox: entra pela carga inicial da loja (backfill).
        var product = await ProductAsync(owner, Unique("Biscoito "), 10m);
        var (storeA, registerA, _) = await StoreAsync(owner);
        var (location, cash, _, _) = await SetupAsync();
        var listB = await WithDbAsync(async db =>
        {
            var list = new BemAqui.Catalog.Domain.PriceList { Name = Unique("Tabela B "), CreatedAt = DateTimeOffset.UtcNow };
            db.PriceLists.Add(list);
            await db.SaveChangesAsync();
            return list.Id;
        });
        var codeB = Unique("S", 5);
        var storeB = (await OkAsync(await owner.PostAsJsonAsync("/api/v1/pos/stores", new { code = codeB, name = "Loja " + codeB, stockLocationId = location, defaultPriceListId = listB }), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        var registerB = (await OkAsync(await owner.PostAsJsonAsync($"/api/v1/pos/stores/{storeB}/registers", new { number = 1, cashAccountId = cash, uiMode = "Keyboard" }), HttpStatusCode.Created))
            .GetProperty("registers")[0].GetProperty("id").GetGuid();
        using var deviceA = new Device();
        using var deviceB = new Device();
        await OkAsync(await RegisterDeviceAsync(owner, deviceA, storeA, registerA), HttpStatusCode.Created);
        await OkAsync(await RegisterDeviceAsync(owner, deviceB, storeB, registerB, name: "Caixa da loja B"), HttpStatusCode.Created);

        // Preço próprio na tabela da loja B e um usuário de operação só da loja B.
        await OkAsync(await owner.PutAsJsonAsync($"/api/v1/catalog/products/{product}/price", new { price = 7.77m, priceListId = listB, applyToVariants = false }));
        var operatorB = await CreateOperationUserAsync(owner, await CashierGroupAsync(), storeB, "Bruno da Loja B");

        var a = await SnapshotAsync(deviceA);
        Item(a.GetProperty("items"), product)!.Value.GetProperty("priceCents").GetInt64().Should().Be(1000, "a loja A usa a tabela padrão");
        a.GetProperty("operators").EnumerateArray().Select(o => o.GetProperty("userId").GetGuid()).Should().NotContain(operatorB);
        a.GetProperty("store").GetProperty("id").GetGuid().Should().Be(storeA);

        // Delta depois do evento de preço da tabela B: a loja A não recebe nada desse preço.
        await DrainOutboxAsync();
        var deltaA = await OkAsync(await Anonymous().SendAsync(SignedGet(deviceA, $"/api/v1/pos/sync/changes?cursor={a.GetProperty("cursor").GetInt64()}")));
        Item(deltaA.GetProperty("items"), product).Should().BeNull();

        var b = await SnapshotAsync(deviceB);
        Item(b.GetProperty("items"), product)!.Value.GetProperty("priceCents").GetInt64().Should().Be(777);
        b.GetProperty("priceList").GetProperty("id").GetGuid().Should().Be(listB);
        b.GetProperty("operators").EnumerateArray().Select(o => o.GetProperty("userId").GetGuid()).Should().Contain(operatorB);

        // Nenhum nome de campo com custo ou margem em toda a resposta (foto e delta).
        foreach (var body in new[] { a, b, deltaA })
        {
            PropertyNames(body).Where(n => n.Contains("cost", StringComparison.OrdinalIgnoreCase) || n.Contains("margin", StringComparison.OrdinalIgnoreCase)
                || n.Contains("markup", StringComparison.OrdinalIgnoreCase) || n.Contains("fee", StringComparison.OrdinalIgnoreCase)).Should().BeEmpty();
        }
    }

    private static IEnumerable<string> PropertyNames(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => PropertyNames(p.Value).Prepend(p.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(PropertyNames),
        _ => [],
    };
}
