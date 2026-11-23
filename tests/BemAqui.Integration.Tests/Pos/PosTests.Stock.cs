using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Inventory.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Pos;

/// <summary>
/// PDV-05 (PP-02 de 18/11/2026): a opção "vender sem saldo" das Configurações (padrão desligada) e o saldo da loja descem ao caixa pelo esquema
/// Device, inteiros na primeira vez e só o que mudou depois, para o caixa conferir sem rede pelo último saldo recebido.
/// </summary>
public partial class PosTests
{
    private static decimal? Level(JsonElement stock, Guid productId) =>
        stock.GetProperty("levels").EnumerateArray().Where(l => l.GetProperty("productId").GetGuid() == productId).Select(l => (decimal?)l.GetProperty("available").GetDecimal()).FirstOrDefault();

    [Fact]
    public async Task Caixa_recebe_o_saldo_da_loja_e_a_opcao_de_vender_sem_saldo()
    {
        await CommerceAsync(true);
        var (owner, store, device) = await RegisterAsync();
        using var registered = device;
        var product = await ProductAsync(owner, Unique("Arroz "), 7.5m);
        var empty = await ProductAsync(owner, Unique("Feijão "), 9m);
        await DrainOutboxAsync();
        var location = await WithDbAsync(db => db.Stores.Where(s => s.Id == store).Select(s => s.StockLocationId).SingleAsync());
        var item = await WithDbAsync(db => db.Products.Where(p => p.Id == product).Select(p => p.ItemId).SingleAsync());
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            (await scope.ServiceProvider.GetRequiredService<IStockMovements>()
                .PostAsync(new StockPosting(StockMovementType.Receipt, item, location, null, 4m, 2m, StockSources.GoodsReceipt, Guid.NewGuid()))).IsSuccess.Should().BeTrue();
            await transaction.CommitAsync();
        }

        // Padrão: não vender sem saldo. A lista inteira traz todo produto da loja, o sem posição com zero.
        var full = await OkAsync(await Anonymous().SendAsync(SignedGet(device, "/api/v1/pos/sync/stock")));
        (full.GetProperty("sellWithoutStock").GetBoolean(), full.GetProperty("full").GetBoolean()).Should().Be((false, true));
        (Level(full, product), Level(full, empty)).Should().Be((4m, 0m));
        full.TryGetProperty("averageCost", out _).Should().BeFalse("o caixa nunca recebe custo");

        // Ligada nas Configurações, a opção desce; o delta traz só o produto que teve movimento.
        (await owner.PutAsJsonAsync("/api/v1/settings/parameters/sell-without-stock", new { enabled = true })).StatusCode.Should().Be(HttpStatusCode.OK);
        var cursor = full.GetProperty("cursor").GetInt64();
        var delta = await OkAsync(await Anonymous().SendAsync(SignedGet(device, $"/api/v1/pos/sync/stock?since={cursor}")));
        (delta.GetProperty("sellWithoutStock").GetBoolean(), delta.GetProperty("full").GetBoolean()).Should().Be((true, false));
        Level(delta, product).Should().Be(4m);
        Level(delta, empty).Should().BeNull();
        // Delta mais antigo que 7 dias vira a lista inteira (revisão de segurança B2).
        var old = DateTimeOffset.UtcNow.AddDays(-8).ToUnixTimeMilliseconds();
        (await OkAsync(await Anonymous().SendAsync(SignedGet(device, $"/api/v1/pos/sync/stock?since={old}")))).GetProperty("full").GetBoolean().Should().BeTrue();
        (await owner.PutAsJsonAsync("/api/v1/settings/parameters/sell-without-stock", new { enabled = false })).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
