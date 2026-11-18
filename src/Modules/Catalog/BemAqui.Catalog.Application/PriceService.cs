using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Catalog.Contracts;
using BemAqui.Catalog.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Catalog.Application;

/// <summary>
/// Preço na tabela (CA 8): a mudança trava a linha da tabela (<c>SELECT … FOR UPDATE</c>), sobe a versão uma vez por gravação, guarda no
/// histórico o preço anterior, o novo, quem e quando, e publica <see cref="PriceChanged"/> no outbox, tudo na mesma transação. A rota exige
/// <c>catalog.edit_price</c>; quem chama por dentro (criar produto, gerar grade) já conferiu a permissão.
/// </summary>
public sealed class PriceService(ICatalogDb db, CatalogDefaults defaults, ICurrentUser currentUser, IUserDirectory users, IEventPublisher events, TimeProvider clock)
{
    /// <summary>"Alterar preço" (CA 8 e 16): no pai de grade, <c>ApplyToVariants</c> grava o mesmo preço nas variações.</summary>
    public async Task<Result> SetAsync(Guid productId, PriceInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
        if (product is null)
        {
            return Error.NotFound("product_not_found", "Produto não encontrado.");
        }

        if (!ProductRules.IsValidPrice(input.Price))
        {
            return Error.Validation("validation", "Informe um preço maior que zero, com até 2 casas.", new Dictionary<string, string[]> { ["price"] = ["Informe um preço maior que zero, com até 2 casas."] });
        }

        await defaults.DefaultPriceListAsync(cancellationToken);
        var ids = new List<Guid> { productId };
        if (input.ApplyToVariants && product.HasVariants)
        {
            ids.AddRange(await db.Products.Where(v => v.VariantOfProductId == productId).Select(v => v.Id).ToListAsync(cancellationToken));
        }

        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        var applied = await ApplyAsync(ids, input.Price, input.PriceListId, onlyIfChanged: true, cancellationToken);
        if (applied.IsFailure)
        {
            return applied.Error!;
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return Result.Success();
    }

    /// <summary>
    /// Grava o preço dos produtos numa tabela (nula = padrão), dentro da transação aberta por quem chama. Uma versão nova por chamada; produto
    /// com o mesmo preço fica de fora quando <paramref name="onlyIfChanged"/>.
    /// </summary>
    public async Task<Result<IReadOnlyList<PriceChange>>> ApplyAsync(
        IReadOnlyCollection<Guid> productIds, decimal price, Guid? priceListId, bool onlyIfChanged = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productIds);
        var listId = priceListId ?? (await defaults.DefaultPriceListAsync(cancellationToken)).Id;
        var list = await db.PriceLists.FromSql($"SELECT * FROM catalog_products.price_lists WHERE id = {listId} FOR UPDATE").FirstOrDefaultAsync(cancellationToken);
        if (list is not null)
        {
            // A linha pode já estar no contexto (lida antes da trava): relê a versão depois de travar.
            await db.Entry(list).ReloadAsync(cancellationToken);
        }

        if (list is null || !list.IsActive)
        {
            return Error.Validation("validation", "Escolha uma tabela de preço ativa.", new Dictionary<string, string[]> { ["priceListId"] = ["Escolha uma tabela de preço ativa."] });
        }

        var current = await db.PriceListItems.Where(i => i.PriceListId == list.Id && productIds.Contains(i.ProductId) && i.MinQuantity == 1m)
            .ToDictionaryAsync(i => i.ProductId, cancellationToken);
        var targets = productIds.Where(id => !onlyIfChanged || !current.TryGetValue(id, out var row) || row.Price != price).ToList();
        if (targets.Count == 0)
        {
            return Result<IReadOnlyList<PriceChange>>.Success([]);
        }

        var now = clock.GetUtcNow();
        var version = list.Bump();
        var changes = new List<PriceChange>();
        foreach (var productId in targets)
        {
            decimal? previous = null;
            if (current.TryGetValue(productId, out var row))
            {
                previous = row.Price;
                row.Price = price;
                row.UpdatedAt = now;
                row.UpdatedBy = currentUser.UserId;
            }
            else
            {
                db.PriceListItems.Add(new PriceListItem { PriceListId = list.Id, ProductId = productId, Price = price, UpdatedAt = now, UpdatedBy = currentUser.UserId });
            }

            var change = new PriceChange
            {
                PriceListId = list.Id, ProductId = productId, PreviousPrice = previous, NewPrice = price, ListVersion = version, ChangedAt = now, ChangedBy = currentUser.UserId,
            };
            db.PriceChanges.Add(change);
            changes.Add(change);
            events.Publish(new PriceChanged(list.Id, productId, previous, price, version, now));
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<IReadOnlyList<PriceChange>>.Success(changes);
    }

    /// <summary>Preço vigente de um produto na tabela padrão; null = sem preço.</summary>
    public async Task<decimal?> CurrentAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var list = await defaults.DefaultPriceListAsync(cancellationToken);
        return await db.PriceListItems.AsNoTracking().Where(i => i.PriceListId == list.Id && i.ProductId == productId && i.MinQuantity == 1m)
            .Select(i => (decimal?)i.Price).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Tabelas de preço (nesta story, só a "Balcão").</summary>
    public async Task<IReadOnlyList<PriceListView>> ListsAsync(CancellationToken cancellationToken = default)
    {
        await defaults.DefaultPriceListAsync(cancellationToken);
        return await db.PriceLists.AsNoTracking().OrderByDescending(l => l.IsDefault).ThenBy(l => l.Name)
            .Select(l => new PriceListView(l.Id, l.Name, l.Channel, l.StoreId, l.IsDefault, l.Version, db.PriceListItems.Count(i => i.PriceListId == l.Id)))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Histórico do preço do produto (CA 8), do mais novo para o mais antigo.</summary>
    public async Task<Result<IReadOnlyList<PriceChangeView>>> HistoryAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        if (!await db.Products.AnyAsync(p => p.Id == productId, cancellationToken))
        {
            return Error.NotFound("product_not_found", "Produto não encontrado.");
        }

        var changes = await db.PriceChanges.AsNoTracking().Where(c => c.ProductId == productId).OrderByDescending(c => c.ChangedAt).ThenByDescending(c => c.ListVersion)
            .Take(200).ToListAsync(cancellationToken);
        var views = new List<PriceChangeView>();
        foreach (var change in changes)
        {
            views.Add(await ViewAsync(change, cancellationToken));
        }

        return Result<IReadOnlyList<PriceChangeView>>.Success(views);
    }

    private async Task<PriceChangeView> ViewAsync(PriceChange change, CancellationToken ct)
    {
        var listName = await db.PriceLists.AsNoTracking().Where(l => l.Id == change.PriceListId).Select(l => l.Name).FirstOrDefaultAsync(ct) ?? string.Empty;
        var who = change.ChangedBy is { } user ? await users.DisplayNameAsync(user, ct) : null;
        return new PriceChangeView(change.Id, listName, change.PreviousPrice, change.NewPrice, change.ListVersion, change.ChangedAt, who);
    }
}
