using BemAqui.BuildingBlocks.Security;
using BemAqui.Catalog.Contracts;
using BemAqui.Identity.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Pos.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Pos.Application;

/// <summary>Disponível de um produto da loja no local de estoque dela (físico − reservado, <c>decimal(18,4)</c>; pode ser zero).</summary>
public sealed record PosStockLevel(Guid ProductId, decimal Available);

/// <summary>
/// Saldo da loja para o caixa (PDV-05, PP-02 de 18/11/2026). <c>SellWithoutStock</c>: a opção das Configurações da empresa (desligada, o caixa não
/// deixa vender item sem saldo; sem rede, pelo último saldo recebido). <c>Full</c>: a lista traz todos os produtos da loja (o caixa troca a dele
/// inteira); senão, só os que mudaram (o caixa aplica por cima). <c>Cursor</c>: o próximo <c>since</c>, em milissegundos Unix, já com folga para o
/// movimento confirmado tarde.
/// </summary>
public sealed record PosStockSnapshot(bool SellWithoutStock, bool Full, long Cursor, DateTimeOffset GeneratedAt, IReadOnlyList<PosStockLevel> Levels);

/// <summary>
/// Descida do saldo da loja para o aparelho (rota <c>/api/v1/pos/sync/stock</c>, esquema <c>Device</c>). A loja e o local vêm do principal do
/// aparelho, nunca da requisição; só produtos do catálogo da loja e só o disponível (sem custo). O saldo é só orientação para o caixa: o servidor
/// nunca recusa a venda concluída por saldo (a falta vira divergência no Estoque).
/// </summary>
public sealed class PosStockSyncService(
    IPosDb db, PosSyncService sync, IStockLocationFeed feed, ISellableProducts products, ITenantParameters parameters, TimeProvider clock)
{
    /// <summary>Folga do cursor: um movimento lançado há menos que isso, mas confirmado depois da leitura, ainda desce no próximo delta.</summary>
    public static readonly TimeSpan Overlap = TimeSpan.FromMinutes(2);

    /// <summary>Delta mais antigo que isso (o teto da janela sem sincronizar, PP-10) vira foto completa: o caixa nunca pede uma varredura longa.</summary>
    public static readonly TimeSpan MaxDeltaAge = TimeSpan.FromDays(7);

    /// <summary>Rate limit por tenant e aparelho (revisão de segurança B2); o caixa pede a cada 60 s.</summary>
    public const string RateLimitPolicy = "pos-sync-stock";

    public const int RateLimitPerMinute = 30;

    public async Task<Result<PosStockSnapshot>> StockAsync(DeviceIdentity device, long? since, CancellationToken cancellationToken = default)
    {
        var place = await sync.ResolveAsync(device, cancellationToken);
        if (place.IsFailure)
        {
            return place.Error!;
        }

        var store = place.Value.Store;
        var now = clock.GetUtcNow();
        var sellWithoutStock = await parameters.SellWithoutStockAsync(cancellationToken);
        var cursor = (now - Overlap).ToUnixTimeMilliseconds();
        var full = since is not { } ms || ms <= 0 || ms > now.ToUnixTimeMilliseconds() || ms < (now - MaxDeltaAge).ToUnixTimeMilliseconds();
        var storeProducts = db.CatalogItems.AsNoTracking().Where(i => i.StoreId == store.Id && i.Kind == CatalogItemKind.Product && !i.Deleted).Select(i => i.EntityId);

        if (full)
        {
            var productIds = await storeProducts.ToListAsync(cancellationToken);
            var links = await products.ItemLinksAsync(productIds, null, cancellationToken);
            var available = (await feed.LocationBalancesAsync(store.StockLocationId, null, cancellationToken)).ToDictionary(a => a.ItemId, a => a.Available);
            return new PosStockSnapshot(sellWithoutStock, true, cursor, now, links.Select(l => new PosStockLevel(l.ProductId, available.GetValueOrDefault(l.ItemId))).ToList());
        }

        var changed = await feed.LocationBalancesAsync(store.StockLocationId, DateTimeOffset.FromUnixTimeMilliseconds(since!.Value), cancellationToken);
        if (changed.Count == 0)
        {
            return new PosStockSnapshot(sellWithoutStock, false, cursor, now, []);
        }

        var byItem = changed.ToDictionary(a => a.ItemId, a => a.Available);
        var changedLinks = await products.ItemLinksAsync(null, byItem.Keys.ToList(), cancellationToken);
        var candidates = changedLinks.Select(l => l.ProductId).ToList();
        var inStore = (await storeProducts.Where(id => candidates.Contains(id)).ToListAsync(cancellationToken)).ToHashSet();
        return new PosStockSnapshot(
            sellWithoutStock, false, cursor, now, changedLinks.Where(l => inStore.Contains(l.ProductId)).Select(l => new PosStockLevel(l.ProductId, byItem[l.ItemId])).ToList());
    }
}
