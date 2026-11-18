using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Catalog.Contracts;
using BemAqui.Catalog.Domain;
using BemAqui.Inventory.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Catalog.Application;

/// <summary>
/// Produtos do Catálogo (CAT-01): ligados a um item do Estoque (existente ou criado no mesmo formulário, CA 3), um produto por item (CA 4),
/// categoria (a "Geral" por padrão, CA 14), PLU, códigos de barras (CA 5 a 7), grade (CA 15 e 16) e o preço da tabela padrão (CA 8 e 9).
/// Custo e margem só com <c>catalog.view_cost</c>, e ausentes sem ela (CA 10 e 11).
/// </summary>
public sealed partial class ProductService(
    ICatalogDb db, CatalogDefaults defaults, PriceService prices, ProductPhotoService photos, IItems items, IItemRegistration registration, IItemCosts costs, IPermissionChecker permissions,
    IEnumerable<IProductUsageProvider> usages, IEventPublisher events, TimeProvider clock)
{
    public const string Module = "catalog";
    public const string ItemIndex = "ix_products_item_id";
    public const string PluIndex = "ix_products_plu";
    public const string ItemTakenMessage = "Este item já tem um produto no catálogo.";

    public async Task<PagedList<ProductListItem>> ListAsync(ProductListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);
        var list = await defaults.DefaultPriceListAsync(cancellationToken);
        var products = db.Products.AsNoTracking().Where(p => p.VariantOfProductId == null);
        if (!query.IncludeInactive)
        {
            products = products.Where(p => p.IsActive);
        }

        if (query.CategoryId is { } category)
        {
            products = products.Where(p => p.CategoryId == category);
        }

        if (query.WithoutPrice)
        {
            products = products.Where(p => !db.PriceListItems.Any(i => i.PriceListId == list.Id && i.ProductId == p.Id));
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            // Busca por nome, PLU ou código de barras, também nas variações (a lista mostra o pai, CA 15).
            var text = SearchText.Normalize(query.Q);
            var digits = GtinRules.Normalize(query.Q);
            products = products.Where(p => p.SearchText.Contains(text)
                || db.ProductBarcodes.Any(b => b.ProductId == p.Id && b.Gtin == digits)
                || db.Products.Any(v => v.VariantOfProductId == p.Id && (v.SearchText.Contains(text) || db.ProductBarcodes.Any(b => b.ProductId == v.Id && b.Gtin == digits))));
        }

        products = page.ParseSort() switch
        {
            ("name", true) => products.OrderByDescending(p => p.SaleName),
            _ => products.OrderBy(p => p.SaleName),
        };

        var paged = await products.Select(p => new
        {
            p.Id, p.SaleName, p.CategoryId, p.ItemId, p.Plu, p.VariantOfProductId, p.IsActive,
            Category = db.ProductCategories.IgnoreQueryFilters().Where(c => c.Id == p.CategoryId).Select(c => c.Name).FirstOrDefault(),
            FirstGtin = db.ProductBarcodes.Where(b => b.ProductId == p.Id).OrderBy(b => b.CreatedAt).Select(b => b.Gtin).FirstOrDefault(),
            Barcodes = db.ProductBarcodes.Count(b => b.ProductId == p.Id),
            Variants = db.Products.Count(v => v.VariantOfProductId == p.Id),
            Price = db.PriceListItems.Where(i => i.PriceListId == list.Id && i.ProductId == p.Id && i.MinQuantity == 1m).Select(i => (decimal?)i.Price).FirstOrDefault(),
        }).ToPagedListAsync(page, cancellationToken);

        var found = await ItemsAsync(paged.Items.Select(p => p.ItemId), cancellationToken);
        var withCost = await CanSeeCostAsync(cancellationToken);
        var averages = withCost ? await costs.AverageCostsAsync(paged.Items.Select(p => p.ItemId).ToList(), cancellationToken) : null;
        var rows = paged.Items.Select(p =>
        {
            var item = found.GetValueOrDefault(p.ItemId);
            return new ProductListItem(p.Id, p.SaleName, p.CategoryId, p.Category, p.ItemId, item?.Code ?? string.Empty, item?.UnitCode ?? string.Empty, p.Plu, p.FirstGtin,
                p.Barcodes, p.Price, p.VariantOfProductId, p.Variants, p.IsActive, averages is null ? null : Cost(averages.GetValueOrDefault(p.ItemId), p.Price, null));
        }).ToList();
        return new PagedList<ProductListItem>(rows, paged.Page, paged.PageSize, paged.Total);
    }

    public async Task<Result<ProductDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return product is null ? NotFound() : await DetailAsync(product, cancellationToken);
    }

    /// <summary>
    /// CA 3 e 4: item existente (sem produto) ou item novo do tipo Mercadoria, criado na mesma transação com o nome de venda, a unidade e o
    /// NCM digitados uma vez só. Preço só com <c>catalog.edit_price</c> (CA 9).
    /// </summary>
    public async Task<Result<ProductDetail>> CreateAsync(ProductCreateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Price is not null && !await permissions.HasPermissionAsync(Module, "edit_price", cancellationToken))
        {
            return PriceForbidden();
        }

        await defaults.EnsureAsync(cancellationToken);
        var name = (input.SaleName ?? string.Empty).Trim();
        var plu = NormalizePlu(input.Plu);
        var barcodes = (input.Barcodes ?? []).Select(b => (Gtin: GtinRules.Normalize(b.Gtin), Pack: b.PackQuantity ?? 1m)).ToList();
        if ((ValidateFields(name, plu, input.IsWeighable, input.Price) ?? ValidateBarcodes(barcodes)) is { } invalid)
        {
            return invalid;
        }

        if ((input.ItemId is null) == (input.NewItem is null))
        {
            return Invalid("itemId", "Escolha um item do estoque ou crie um novo, um dos dois.");
        }

        var categoryId = input.CategoryId ?? (await defaults.GeneralCategoryAsync(cancellationToken)).Id;
        if (await CheckCategoryAsync(categoryId, null, cancellationToken) is { } badCategory)
        {
            return badCategory;
        }

        if (input.ItemId is { } existing && await CheckItemAsync(existing, cancellationToken) is { } badItem)
        {
            return badItem;
        }

        if (await CheckUniqueAsync(plu, barcodes.Select(b => b.Gtin), null, cancellationToken) is { } taken)
        {
            return taken;
        }

        // Dentro da transação de quem chama (a importação, CAT-02, grava produto, preço e saldo de cada linha juntos), não abre outra.
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        var itemId = input.ItemId;
        if (input.NewItem is { } newItem)
        {
            var created = await registration.CreateAsync(new NewItemData(ItemType.Merchandise, newItem.Code, name, newItem.UnitId, newItem.Ncm), cancellationToken);
            if (created.IsFailure)
            {
                return created.Error!;
            }

            itemId = created.Value;
        }

        var now = clock.GetUtcNow();
        var product = new Product
        {
            ItemId = itemId!.Value, CategoryId = categoryId, SaleName = name, Plu = plu, IsWeighable = input.IsWeighable, SellablePos = input.SellablePos,
            SellableOnline = input.SellableOnline, CreatedAt = now,
        };
        db.Products.Add(product);
        foreach (var (gtin, pack) in barcodes)
        {
            db.ProductBarcodes.Add(new ProductBarcode { ProductId = product.Id, Gtin = gtin, PackQuantity = pack, CreatedAt = now });
        }

        events.Publish(new ProductChanged(product.Id));
        if (await SaveUniqueAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        if (input.Price is { } price)
        {
            var priced = await prices.ApplyAsync([product.Id], price, null, false, cancellationToken);
            if (priced.IsFailure)
            {
                return priced.Error!;
            }
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return await GetAsync(product.Id, cancellationToken);
    }

    /// <summary>
    /// Edição (CA 9 e 16): preço na edição exige <c>catalog.edit_price</c> (sem ela, 403 e nada muda). No pai, nome e categoria descem para as
    /// variações. A variação não troca nome nem categoria: vêm do pai.
    /// </summary>
    public async Task<Result<ProductDetail>> UpdateAsync(Guid id, ProductUpdateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Price is not null && !await permissions.HasPermissionAsync(Module, "edit_price", cancellationToken))
        {
            return PriceForbidden();
        }

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        // Concorrência otimista (ADR 0019): gravar sobre uma versão antiga dá 409 concurrency_conflict.
        db.Entry(product).Property(p => p.Version).OriginalValue = input.Version;
        var name = (input.SaleName ?? string.Empty).Trim();
        var plu = NormalizePlu(input.Plu);
        if (ValidateFields(name, plu, input.IsWeighable, input.Price) is { } invalid)
        {
            return invalid;
        }

        var categoryId = input.CategoryId ?? product.CategoryId;
        if (await CheckCategoryAsync(categoryId, product.CategoryId, cancellationToken) is { } badCategory)
        {
            return badCategory;
        }

        if (await CheckUniqueAsync(plu, [], product.Id, cancellationToken) is { } taken)
        {
            return taken;
        }

        // Dentro da transação de quem chama (a importação, CAT-02, grava produto, preço e saldo de cada linha juntos), não abre outra.
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        if (product.VariantOfProductId is null)
        {
            product.SaleName = name;
            product.CategoryId = categoryId;
            if (product.HasVariants)
            {
                await InheritAsync(product, cancellationToken);
            }
        }

        product.Plu = plu;
        product.IsWeighable = input.IsWeighable;
        product.SellablePos = input.SellablePos;
        product.SellableOnline = input.SellableOnline;
        events.Publish(new ProductChanged(product.Id));
        if (await SaveUniqueAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        if (input.Price is { } price)
        {
            var priced = await prices.ApplyAsync([product.Id], price, null, onlyIfChanged: true, cancellationToken);
            if (priced.IsFailure)
            {
                return priced.Error!;
            }
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Desativar ou reativar; no pai, vale também para as variações.</summary>
    public async Task<Result<ProductDetail>> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        product.IsActive = active;
        events.Publish(new ProductChanged(product.Id));
        foreach (var variant in await db.Products.Where(v => v.VariantOfProductId == id).ToListAsync(cancellationToken))
        {
            variant.IsActive = active;
            events.Publish(new ProductChanged(variant.Id));
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>
    /// Exclusão lógica (regra "produto com venda não é apagado, só desativado"): com uso, 409 <c>product_in_use</c>. Os códigos de barras
    /// saem (o GTIN volta a ficar livre); no pai, as variações saem junto; o item do Estoque continua.
    /// </summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        var family = await db.Products.Where(p => p.Id == id || p.VariantOfProductId == id).ToListAsync(cancellationToken);
        foreach (var member in family)
        {
            foreach (var usage in usages)
            {
                if (await usage.UsageAsync(member.Id, cancellationToken) is { } where)
                {
                    return Error.Conflict("product_in_use", "O produto já foi vendido. Desative-o.") with { Data = new Dictionary<string, object?> { ["usage"] = where } };
                }
            }
        }

        var now = clock.GetUtcNow();
        var ids = family.Select(p => p.Id).ToList();
        db.ProductBarcodes.RemoveRange(await db.ProductBarcodes.Where(b => ids.Contains(b.ProductId)).ToListAsync(cancellationToken));
        foreach (var member in family)
        {
            member.MarkDeleted(now);
            events.Publish(new ProductChanged(member.Id));
        }

        if (product.VariantOfProductId is { } parentId && await db.Products.FirstOrDefaultAsync(p => p.Id == parentId, cancellationToken) is { } parent)
        {
            parent.HasVariants = await db.Products.AnyAsync(v => v.VariantOfProductId == parentId && v.Id != id, cancellationToken);
            events.Publish(new ProductChanged(parent.Id));
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Itens do Estoque ainda sem produto, para o seletor do formulário (CA 3 e 4); Serviço fica fora.</summary>
    public async Task<IReadOnlyList<ItemChoice>> ItemChoicesAsync(string? q, CancellationToken cancellationToken = default)
    {
        var found = await items.SearchAsync(null, q, 40, cancellationToken);
        var ids = found.Select(i => i.Id).ToList();
        var taken = await db.Products.Where(p => ids.Contains(p.ItemId)).Select(p => p.ItemId).ToListAsync(cancellationToken);
        return found.Where(i => i.Type != ItemType.Service && !taken.Contains(i.Id)).Take(20)
            .Select(i => new ItemChoice(i.Id, i.Code, i.Name, i.UnitCode, i.Ncm, i.Type.ToString())).ToList();
    }

    public async Task<IReadOnlyList<UnitChoiceView>> UnitChoicesAsync(CancellationToken cancellationToken = default) =>
        (await registration.UnitsAsync(cancellationToken)).Select(u => new UnitChoiceView(u.Id, u.Code, u.Name)).ToList();

    private ValueTask<bool> CanSeeCostAsync(CancellationToken ct) => permissions.HasPermissionAsync(Module, "view_cost", ct);

    private async Task<Dictionary<Guid, ItemSummary>> ItemsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return list.Count == 0 ? new Dictionary<Guid, ItemSummary>() : (await items.FindAsync(list, ct)).ToDictionary(i => i.Id);
    }

    /// <summary>Painel de custo e margem (CA 11): custo médio do item, margem e markup sobre o preço e o preço sugerido pela categoria.</summary>
    private static ProductCostView Cost(decimal averageCost, decimal? price, ProductCategory? category) =>
        new(averageCost, MarginRules.MarginPercent(price, averageCost), MarginRules.MarkupPercent(price, averageCost),
            category is null ? null : MarginRules.SuggestedPrice(averageCost, category.DefaultMarginPercent, category.DefaultMarkupPercent));

    private async Task<ProductDetail> DetailAsync(Product p, CancellationToken ct)
    {
        var list = await defaults.DefaultPriceListAsync(ct);
        var category = await db.ProductCategories.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(c => c.Id == p.CategoryId, ct);
        var variants = await db.Products.AsNoTracking().Where(v => v.VariantOfProductId == p.Id).OrderBy(v => v.SaleName).ToListAsync(ct);
        var productIds = variants.Select(v => v.Id).Append(p.Id).ToList();
        var priceById = await db.PriceListItems.AsNoTracking().Where(i => i.PriceListId == list.Id && productIds.Contains(i.ProductId) && i.MinQuantity == 1m)
            .ToDictionaryAsync(i => i.ProductId, i => i.Price, ct);
        var barcodes = await db.ProductBarcodes.AsNoTracking().Where(b => productIds.Contains(b.ProductId)).OrderBy(b => b.CreatedAt).ToListAsync(ct);
        var found = await ItemsAsync(variants.Select(v => v.ItemId).Append(p.ItemId), ct);
        var withCost = await CanSeeCostAsync(ct);
        var averages = withCost ? await costs.AverageCostsAsync(found.Keys.ToList(), ct) : null;
        var parent = p.VariantOfProductId is { } parentId ? await db.Products.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == parentId).Select(x => x.SaleName).FirstOrDefaultAsync(ct) : null;
        var item = found.GetValueOrDefault(p.ItemId);
        decimal? price = priceById.TryGetValue(p.Id, out var own) ? own : null;
        return new ProductDetail(
            p.Id, p.SaleName, p.CategoryId, category?.Name, p.ItemId, item?.Code ?? string.Empty, item?.Name ?? string.Empty, item?.UnitId ?? Guid.Empty,
            item?.UnitCode ?? string.Empty, item?.Ncm, p.Plu, p.IsWeighable, p.SellablePos, p.SellableOnline, p.VariantOfProductId, parent, p.VariantAttributes,
            p.HasVariants, p.IsActive, price, list.Id, list.Name, list.Version,
            barcodes.Where(b => b.ProductId == p.Id).Select(b => new BarcodeView(b.Id, b.Gtin, b.PackQuantity, b.CreatedAt)).ToList(),
            variants.Select(v =>
            {
                decimal? variantPrice = priceById.TryGetValue(v.Id, out var vp) ? vp : null;
                return new VariantView(v.Id, v.SaleName, v.VariantAttributes ?? new Dictionary<string, string>(), v.ItemId, found.GetValueOrDefault(v.ItemId)?.Code ?? string.Empty,
                    barcodes.Where(b => b.ProductId == v.Id).Select(b => b.Gtin).FirstOrDefault(), variantPrice, v.IsActive,
                    averages is null ? null : Cost(averages.GetValueOrDefault(v.ItemId), variantPrice, null));
            }).ToList(),
            p.CreatedAt, p.Version, (await photos.PhotosAsync([(p.Id, p.VariantOfProductId)], ct)).TryGetValue(p.Id, out var photo) ? photo : null, averages is null ? null : Cost(averages.GetValueOrDefault(p.ItemId), price, category));
    }

    private static string? NormalizePlu(string? plu) => string.IsNullOrWhiteSpace(plu) ? null : plu.Trim();

    private static Error? ValidateFields(string name, string? plu, bool weighable, decimal? price)
    {
        if (name.Length is 0 or > ProductRules.NameMax)
        {
            return Invalid("saleName", $"Informe o nome de venda, com até {ProductRules.NameMax} caracteres.");
        }

        if (plu is not null && !ProductRules.IsValidPlu(plu))
        {
            return Invalid("plu", $"O PLU tem de 1 a {ProductRules.PluMaxDigits} dígitos.");
        }

        if (weighable && plu is null)
        {
            return Invalid("plu", "Produto vendido por peso precisa do PLU, o código que a balança imprime na etiqueta.");
        }

        if (price is { } p && !ProductRules.IsValidPrice(p))
        {
            return Invalid("price", "Informe um preço maior que zero, com até 2 casas.");
        }

        return null;
    }

    private static Error? ValidateBarcodes(List<(string Gtin, decimal Pack)> barcodes)
    {
        foreach (var (gtin, pack) in barcodes)
        {
            if (!GtinRules.IsValid(gtin))
            {
                return Error.Validation("barcode_invalid", GtinRules.InvalidMessage, new Dictionary<string, string[]> { ["gtin"] = [GtinRules.InvalidMessage] });
            }

            if (!ProductRules.IsValidPackQuantity(pack))
            {
                return Invalid("packQuantity", "A quantidade da embalagem é maior que zero, com até 4 casas.");
            }
        }

        return barcodes.Select(b => b.Gtin).Distinct(StringComparer.Ordinal).Count() == barcodes.Count ? null : Invalid("gtin", "O mesmo código de barras foi informado duas vezes.");
    }

    private async Task<Error?> CheckCategoryAsync(Guid categoryId, Guid? current, CancellationToken ct) =>
        categoryId == current || await db.ProductCategories.AnyAsync(c => c.Id == categoryId && c.IsActive, ct)
            ? null
            : Invalid("categoryId", "Escolha uma categoria ativa da lista.");

    private async Task<Error?> CheckItemAsync(Guid itemId, CancellationToken ct)
    {
        var found = await items.FindAsync([itemId], ct);
        var item = found.Count == 0 ? null : found[0];
        if (item is not { IsActive: true, IsDeleted: false })
        {
            return Invalid("itemId", "Escolha um item ativo do estoque.");
        }

        if (item.Type == ItemType.Service)
        {
            return Invalid("itemId", "Item do tipo Serviço não tem estoque. Escolha uma mercadoria ou um produto.");
        }

        return await db.Products.AnyAsync(p => p.ItemId == itemId, ct) ? Error.Conflict("product_item_taken", ItemTakenMessage) : null;
    }

    /// <summary>PLU e GTIN únicos no tenant (CA 7): o 409 diz qual produto já usa o código.</summary>
    private async Task<Error?> CheckUniqueAsync(string? plu, IEnumerable<string> gtins, Guid? self, CancellationToken ct)
    {
        if (plu is not null && await db.Products.Where(p => p.Plu == plu && p.Id != self).Select(p => p.SaleName).FirstOrDefaultAsync(ct) is { } pluOwner)
        {
            return Error.Conflict("product_plu_taken", $"O PLU {plu} já é do produto {pluOwner}.");
        }

        var list = gtins.ToList();
        if (list.Count == 0)
        {
            return null;
        }

        var owner = await db.ProductBarcodes.Where(b => list.Contains(b.Gtin))
            .Join(db.Products.IgnoreQueryFilters(), b => b.ProductId, p => p.Id, (b, p) => new { b.Gtin, p.SaleName, p.Id })
            .Where(x => x.Id != self).FirstOrDefaultAsync(ct);
        return owner is null ? null : GtinTaken(owner.Gtin, owner.SaleName);
    }

    /// <summary>Gravação simultânea: os índices únicos de item, PLU e GTIN seguram, e a resposta é o mesmo 409 da conferência.</summary>
    private async Task<Error?> SaveUniqueAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, ItemIndex))
        {
            return Error.Conflict("product_item_taken", ItemTakenMessage);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, PluIndex))
        {
            return Error.Conflict("product_plu_taken", "Esse PLU acabou de ser usado em outro produto. Escolha outro.");
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, BarcodeIndex))
        {
            return Error.Conflict("barcode_taken", "Esse código de barras acabou de ser usado em outro produto.");
        }
    }

    private static Error GtinTaken(string gtin, string productName) =>
        Error.Conflict("barcode_taken", $"O código de barras {gtin} já é do produto {productName}.") with { Data = new Dictionary<string, object?> { ["productName"] = productName } };

    private static Error PriceForbidden() =>
        Error.Forbidden(Error.Codes.Forbidden, "Você não tem permissão para alterar preço. Peça a quem tem \"Alterar preço\" no Catálogo.");

    private static Error Invalid(string field, string message) =>
        Error.Validation("validation", message, new Dictionary<string, string[]> { [field] = [message] });

    private static Error NotFound() => Error.NotFound("product_not_found", "Produto não encontrado.");
}
