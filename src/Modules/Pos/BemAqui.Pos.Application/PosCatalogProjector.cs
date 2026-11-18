using System.Text.Json;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Catalog.Contracts;
using BemAqui.Finance.Contracts;
using BemAqui.Pos.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Pos.Application;

/// <summary>Código de barras como desce para o caixa.</summary>
public sealed record PosSyncBarcode(string Gtin, decimal PackQuantity);

/// <summary>Conteúdo da linha de produto em <c>pos.catalog_items.payload</c> (OFF-01): já com o preço da tabela da loja, em centavos.</summary>
public sealed record PosItemPayload(
    Guid ProductId,
    Guid? ParentProductId,
    string Description,
    string UnitCode,
    bool IsWeighable,
    string? Plu,
    IReadOnlyList<PosSyncBarcode> Barcodes,
    long? PriceCents,
    Guid PriceListId,
    long? PriceListVersion);

/// <summary>Conteúdo da linha de forma de pagamento em <c>pos.catalog_items.payload</c> (OFF-01). Sem taxa da adquirente (é custo).</summary>
public sealed record PosPaymentMethodPayload(Guid Id, string Name, PaymentMethodType Type, bool AllowsChange, string NfePaymentCode, bool Confirmed);

/// <summary>
/// Gravação do modelo de leitura do caixa (<c>pos.catalog_items</c>, OFF-01 CA 9): pelos eventos <see cref="ProductChanged"/>,
/// <see cref="PriceChanged"/> e <see cref="PaymentMethodChanged"/> e pela carga inicial de cada loja. Lê os outros módulos só pelos contratos
/// (<see cref="IPosCatalogFeed"/>, <see cref="IPaymentMethods"/>), sempre a fonte atual, nunca o conteúdo do evento: o evento repetido ou fora de
/// ordem grava o mesmo resultado (idempotente). A linha só ganha <c>change_seq</c> novo quando o conteúdo muda.
/// <para>
/// <b>Cursor sem buraco:</b> toda gravação roda numa transação com a trava <c>pg_advisory_xact_lock</c> do catálogo do tenant, tomada antes do
/// primeiro <c>nextval</c>. Assim as gravações entram em série e um <c>change_seq</c> menor nunca é confirmado depois de um maior; o aparelho que
/// leu até N não perde uma mudança N-1 que ainda estava em andamento.
/// </para>
/// </summary>
public sealed class PosCatalogProjector(IPosDb db, IPosCatalogFeed feed, IPaymentMethods paymentMethods, TimeProvider clock)
    : IIntegrationEventConsumer<ProductChanged>, IIntegrationEventConsumer<PriceChanged>, IIntegrationEventConsumer<PaymentMethodChanged>
{
    public const string LockKey = "pos.catalog";
    private const int Page = 1000;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task HandleAsync(ProductChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        await InTransactionAsync(async () =>
        {
            var stores = await StoresAsync(null, cancellationToken);
            await RefreshProductsAsync([integrationEvent.ProductId], stores, cancellationToken);
        }, cancellationToken);
    }

    public async Task HandleAsync(PriceChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        await InTransactionAsync(async () =>
        {
            // Só as lojas desta tabela; o preço é relido da fonte (o evento fora de ordem não volta preço velho).
            var stores = await StoresAsync(integrationEvent.PriceListId, cancellationToken);
            if (stores.Count > 0)
            {
                await RefreshProductsAsync([integrationEvent.ProductId], stores, cancellationToken);
            }
        }, cancellationToken);
    }

    public async Task HandleAsync(PaymentMethodChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        await InTransactionAsync(async () =>
        {
            var stores = await StoresAsync(null, cancellationToken);
            await RefreshPaymentMethodsAsync(stores.Select(s => s.StoreId).ToList(), cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// Carga inicial da loja (backfill, OFF-01): na primeira sincronização da loja, na loja que já existia antes da OFF-01 e quando a loja troca
    /// de tabela de preço. Idempotente: refeita, só muda o que mudou. Produto que saiu do caixa sem evento (dado antigo) vira removido.
    /// </summary>
    public async Task EnsureBuiltAsync(Store store, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (await IsBuiltAsync(store, cancellationToken))
        {
            return;
        }

        await InTransactionAsync(async () =>
        {
            // Outra sincronização da mesma loja pode ter montado enquanto esta esperava a trava.
            if (await IsBuiltAsync(store, cancellationToken))
            {
                return;
            }

            var target = new[] { (store.Id, store.DefaultPriceListId) };
            var kept = new List<Guid>();
            Guid? after = null;
            while (true)
            {
                var ids = await feed.SellableIdsAsync(after, Page, cancellationToken);
                if (ids.Count == 0)
                {
                    break;
                }

                kept.AddRange(ids);
                await RefreshProductsAsync(ids, target, cancellationToken);
                after = ids[^1];
            }

            await RemoveMissingAsync(store.Id, CatalogItemKind.Product, kept, cancellationToken);
            await RefreshPaymentMethodsAsync([store.Id], cancellationToken);
            var build = await db.CatalogStoreBuilds.FirstOrDefaultAsync(b => b.StoreId == store.Id, cancellationToken);
            if (build is null)
            {
                db.CatalogStoreBuilds.Add(new CatalogStoreBuild { StoreId = store.Id, PriceListId = store.DefaultPriceListId, BuiltAt = clock.GetUtcNow() });
            }
            else
            {
                build.PriceListId = store.DefaultPriceListId;
                build.BuiltAt = clock.GetUtcNow();
            }

            await db.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    private Task<bool> IsBuiltAsync(Store store, CancellationToken cancellationToken) =>
        db.CatalogStoreBuilds.AsNoTracking().AnyAsync(b => b.StoreId == store.Id && b.PriceListId == store.DefaultPriceListId, cancellationToken);

    private async Task<List<(Guid StoreId, Guid PriceListId)>> StoresAsync(Guid? priceListId, CancellationToken cancellationToken)
    {
        var query = db.Stores.AsNoTracking();
        if (priceListId is { } list)
        {
            query = query.Where(s => s.DefaultPriceListId == list);
        }

        return (await query.Select(s => new { s.Id, s.DefaultPriceListId }).ToListAsync(cancellationToken))
            .Select(s => (s.Id, s.DefaultPriceListId)).ToList();
    }

    private async Task RefreshProductsAsync(IReadOnlyCollection<Guid> productIds, IReadOnlyCollection<(Guid StoreId, Guid PriceListId)> stores, CancellationToken cancellationToken)
    {
        if (productIds.Count == 0 || stores.Count == 0)
        {
            return;
        }

        var products = (await feed.FindAsync(productIds, stores.Select(s => s.PriceListId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.ProductId);
        var upserts = new List<Row>();
        var removals = new List<(Guid StoreId, Guid EntityId)>();
        foreach (var (storeId, priceListId) in stores)
        {
            foreach (var id in productIds)
            {
                if (!products.TryGetValue(id, out var product) || !product.SellableAtPos)
                {
                    removals.Add((storeId, id));
                    continue;
                }

                var price = product.Prices.FirstOrDefault(p => p.PriceListId == priceListId);
                var payload = new PosItemPayload(
                    product.ProductId, product.ParentProductId, product.Description, product.UnitCode, product.IsWeighable, product.Plu,
                    product.Barcodes.Select(b => new PosSyncBarcode(b.Gtin, b.PackQuantity)).ToList(),
                    price is null ? null : ToCents(price.Price), priceListId, price?.PriceListVersion);
                upserts.Add(new Row(storeId, CatalogItemKind.Product, id, JsonSerializer.Serialize(payload, Json), price?.PriceListVersion));
            }
        }

        await UpsertAsync(upserts, cancellationToken);
        await RemoveAsync(CatalogItemKind.Product, removals, cancellationToken);
    }

    private async Task RefreshPaymentMethodsAsync(List<Guid> storeIds, CancellationToken cancellationToken)
    {
        if (storeIds.Count == 0)
        {
            return;
        }

        var active = await paymentMethods.ActiveAsync(DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), cancellationToken);
        var rows = storeIds.SelectMany(store => active.Select(m => new Row(store, CatalogItemKind.PaymentMethod, m.Id,
            JsonSerializer.Serialize(new PosPaymentMethodPayload(m.Id, m.Name, m.Type, m.AllowsChange, m.NfePaymentCode, m.Confirmed), Json), null))).ToList();
        await UpsertAsync(rows, cancellationToken);
        foreach (var store in storeIds)
        {
            await RemoveMissingAsync(store, CatalogItemKind.PaymentMethod, active.Select(m => m.Id).ToList(), cancellationToken);
        }
    }

    private sealed record Row(Guid StoreId, CatalogItemKind Kind, Guid EntityId, string Payload, long? PriceListVersion);

    /// <summary>Insere ou atualiza em lote; linha igual (mesmo jsonb e não removida) não ganha <c>change_seq</c> novo.</summary>
    private async Task UpsertAsync(IReadOnlyList<Row> rows, CancellationToken cancellationToken)
    {
        foreach (var chunk in rows.Chunk(Page))
        {
            var stores = chunk.Select(r => r.StoreId).ToArray();
            var kinds = chunk.Select(r => r.Kind.ToString()).ToArray();
            var ids = chunk.Select(r => r.EntityId).ToArray();
            var payloads = chunk.Select(r => r.Payload).ToArray();
            var versions = chunk.Select(r => r.PriceListVersion).ToArray();
            var now = clock.GetUtcNow();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO pos.catalog_items (store_id, kind, entity_id, payload, price_list_version, deleted, updated_at)
                SELECT t.store_id, t.kind, t.entity_id, t.payload::jsonb, t.version, false, {now}
                FROM unnest({stores}::uuid[], {kinds}::text[], {ids}::uuid[], {payloads}::text[], {versions}::bigint[]) AS t(store_id, kind, entity_id, payload, version)
                ON CONFLICT (store_id, kind, entity_id) DO UPDATE
                SET payload = excluded.payload, price_list_version = excluded.price_list_version, deleted = false, updated_at = excluded.updated_at,
                    change_seq = nextval('pos.catalog_change_seq')
                WHERE catalog_items.deleted OR catalog_items.payload IS DISTINCT FROM excluded.payload
                """, cancellationToken);
        }
    }

    /// <summary>Marca como removidas as linhas indicadas que existem e ainda não estão removidas (sem linha nova para o que nunca desceu).</summary>
    private async Task RemoveAsync(CatalogItemKind kind, IReadOnlyList<(Guid StoreId, Guid EntityId)> rows, CancellationToken cancellationToken)
    {
        foreach (var chunk in rows.Chunk(Page))
        {
            var stores = chunk.Select(r => r.StoreId).ToArray();
            var ids = chunk.Select(r => r.EntityId).ToArray();
            var now = clock.GetUtcNow();
            var kindName = kind.ToString();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE pos.catalog_items c SET deleted = true, change_seq = nextval('pos.catalog_change_seq'), updated_at = {now}
                FROM unnest({stores}::uuid[], {ids}::uuid[]) AS t(store_id, entity_id)
                WHERE c.store_id = t.store_id AND c.entity_id = t.entity_id AND c.kind = {kindName} AND NOT c.deleted
                """, cancellationToken);
        }
    }

    /// <summary>Marca como removidas as linhas da loja e da coleção fora de <paramref name="keep"/>.</summary>
    private async Task RemoveMissingAsync(Guid storeId, CatalogItemKind kind, IReadOnlyList<Guid> keep, CancellationToken cancellationToken)
    {
        var ids = keep.ToArray();
        var now = clock.GetUtcNow();
        var kindName = kind.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE pos.catalog_items SET deleted = true, change_seq = nextval('pos.catalog_change_seq'), updated_at = {now}
            WHERE store_id = {storeId} AND kind = {kindName} AND NOT deleted AND entity_id <> ALL({ids}::uuid[])
            """, cancellationToken);
    }

    /// <summary>Roda na transação corrente (a do consumidor do outbox) ou abre uma; a trava do catálogo vale até o commit.</summary>
    private async Task InTransactionAsync(Func<Task> work, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            await LockAsync(cancellationToken);
            await work();
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(cancellationToken);
        await work();
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task LockAsync(CancellationToken cancellationToken) =>
        await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({LockKey}, 0))) AS l").ToListAsync(cancellationToken);

    /// <summary>Dinheiro em centavos inteiros (OFF-01 CA 8): o preço é <c>numeric(18,2)</c>.</summary>
    public static long ToCents(decimal value) => (long)decimal.Round(value * 100m, 0, MidpointRounding.AwayFromZero);
}
