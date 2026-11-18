using BemAqui.Catalog.Domain;
using BemAqui.Inventory.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Catalog.Application;

public sealed partial class ProductImportService
{
    private const int InChunk = 1000;

    private sealed record ProductInfo(Guid Id, Guid ItemId, string SaleName, Guid CategoryId, bool HasVariants, Guid? VariantOfProductId);

    private sealed record CategoryInfo(Guid Id, string Name, bool IsActive);

    /// <summary>O que o banco diz sobre um bloco de linhas: uma leitura por tipo, não por linha.</summary>
    private sealed class Lookups
    {
        public Dictionary<string, ItemSummary> ItemsByCode { get; } = new(StringComparer.Ordinal);

        public Dictionary<Guid, ItemSummary> ItemsById { get; } = [];

        public Dictionary<Guid, ProductInfo> ProductsByItem { get; } = [];

        public Dictionary<Guid, ProductInfo> ProductsById { get; } = [];

        public Dictionary<string, Guid> GtinOwners { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, UnitChoice> Units { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, CategoryInfo> Categories { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>Decisão sobre uma linha: cria ou atualiza, o item e o produto achados, a unidade do produto novo, ou o erro em português.</summary>
    private sealed record Plan(ProductImportAction Action, string? Error, ItemSummary? Item, ProductInfo? Product, Guid? UnitId);

    private static string CategoryKey(string text) => SearchText.Normalize(text);

    private static string UnitKey(string text) => text.Trim().ToUpperInvariant();

    private async Task<Lookups> LookupsAsync(IReadOnlyList<IReadOnlyList<string?>> rows, CancellationToken ct)
    {
        var found = new Lookups();
        var codes = rows.Select(c => ProductImportRules.Cell(c, ProductImportColumns.Code)).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        foreach (var chunk in codes.Chunk(InChunk))
        {
            foreach (var item in await items.FindByCodesAsync(chunk, ct))
            {
                found.ItemsByCode[item.Code] = item;
                found.ItemsById[item.Id] = item;
            }
        }

        var gtins = rows.Select(ProductImportRules.Gtin).OfType<string>().Where(GtinRules.IsValid).Distinct(StringComparer.Ordinal).ToList();
        foreach (var chunk in gtins.Chunk(InChunk))
        {
            foreach (var owner in await db.ProductBarcodes.AsNoTracking().Where(b => chunk.Contains(b.Gtin)).Select(b => new { b.Gtin, b.ProductId }).ToListAsync(ct))
            {
                found.GtinOwners[owner.Gtin] = owner.ProductId;
            }
        }

        var itemIds = found.ItemsById.Keys.ToList();
        var productIds = found.GtinOwners.Values.Distinct().ToList();
        foreach (var chunk in itemIds.Chunk(InChunk).Select(c => (Items: c, Products: Array.Empty<Guid>())).Concat(productIds.Chunk(InChunk).Select(c => (Items: Array.Empty<Guid>(), Products: c))))
        {
            var (byItem, byId) = chunk;
            var list = await db.Products.AsNoTracking().Where(p => byItem.Contains(p.ItemId) || byId.Contains(p.Id))
                .Select(p => new ProductInfo(p.Id, p.ItemId, p.SaleName, p.CategoryId, p.HasVariants, p.VariantOfProductId)).ToListAsync(ct);
            foreach (var product in list)
            {
                found.ProductsByItem[product.ItemId] = product;
                found.ProductsById[product.Id] = product;
            }
        }

        // Itens dos produtos achados pelo código de barras (lote e série barram o saldo inicial).
        var missing = found.ProductsById.Values.Select(p => p.ItemId).Where(id => !found.ItemsById.ContainsKey(id)).Distinct().ToList();
        foreach (var chunk in missing.Chunk(InChunk))
        {
            foreach (var item in await items.FindAsync(chunk, ct))
            {
                found.ItemsById[item.Id] = item;
            }
        }

        foreach (var unit in await registration.UnitsAsync(ct))
        {
            found.Units.TryAdd(UnitKey(unit.Code), unit);
            found.Units.TryAdd(UnitKey(unit.Name), unit);
        }

        foreach (var category in await db.ProductCategories.AsNoTracking().Select(c => new CategoryInfo(c.Id, c.Name, c.IsActive)).ToListAsync(ct))
        {
            found.Categories.TryAdd(CategoryKey(category.Name), category);
        }

        return found;
    }

    /// <summary>
    /// A chave (CA 4): o código interno acha o item e, por ele, o produto; sem código, o código de barras acha o produto. Código de item que existe
    /// sem produto cria o produto ligado a ele. As conferências que dependem do banco: código de barras de outro produto, item Serviço ou inativo,
    /// produto com grade, unidade, categoria desativada e item com lote ou série quando há saldo inicial.
    /// </summary>
    private static Plan Resolve(int line, IReadOnlyList<string?> cells, Lookups found, bool pricesIgnored)
    {
        var code = ProductImportRules.Cell(cells, ProductImportColumns.Code);
        var gtin = ProductImportRules.Gtin(cells);
        var item = code is not null ? found.ItemsByCode.GetValueOrDefault(code) : null;
        ProductInfo? product = null;
        if (item is not null)
        {
            product = found.ProductsByItem.GetValueOrDefault(item.Id);
        }
        else if (code is null && gtin is not null && found.GtinOwners.TryGetValue(gtin, out var ownerId))
        {
            product = found.ProductsById.GetValueOrDefault(ownerId);
        }

        Plan Fail(string message) => new(ProductImportAction.Create, message, item, product, null);

        if (ProductImportRules.Validate(line, cells, product is null && item is null, pricesIgnored) is { } invalid)
        {
            return Fail(invalid);
        }

        if (gtin is not null && found.GtinOwners.TryGetValue(gtin, out var owner) && owner != product?.Id)
        {
            var ownerName = found.ProductsById.GetValueOrDefault(owner)?.SaleName ?? "outro produto";
            return Fail($"Linha {line}: o código de barras {gtin} já é do produto {ownerName}.");
        }

        if (item is not null && product is null)
        {
            if (item.Type == ItemType.Service)
            {
                return Fail($"Linha {line}: o item {item.Code} é um serviço e não vira produto.");
            }

            if (!item.IsActive)
            {
                return Fail($"Linha {line}: o item {item.Code} está inativo no Estoque. Reative-o ou use outro código.");
            }
        }

        if (product is { HasVariants: true })
        {
            return Fail($"Linha {line}: o produto {product.SaleName} tem variações: altere-o pela tela do produto.");
        }

        Guid? unitId = null;
        if (product is null && item is null)
        {
            var unitText = ProductImportRules.Cell(cells, ProductImportColumns.Unit) ?? "UN";
            if (!found.Units.TryGetValue(UnitKey(unitText), out var unit))
            {
                var known = string.Join(", ", found.Units.Values.Select(u => u.Code).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(8));
                return Fail($"Linha {line}: unidade \"{unitText}\" não existe. Use uma das cadastradas: {known}.");
            }

            unitId = unit.Id;
        }

        if (ProductImportRules.Cell(cells, ProductImportColumns.Category) is { } categoryText && found.Categories.TryGetValue(CategoryKey(categoryText), out var category) && !category.IsActive)
        {
            return Fail($"Linha {line}: a categoria {category.Name} está desativada. Reative-a ou use outra.");
        }

        if (ProductImportRules.Stock(cells) is not null)
        {
            var stockItem = item ?? (product is null ? null : found.ItemsById.GetValueOrDefault(product.ItemId));
            if (stockItem is { TracksLot: true } or { TracksSerial: true })
            {
                return Fail($"Linha {line}: o item {stockItem.Code} controla lote ou série: lance o saldo pela tela do Estoque.");
            }
        }

        return new Plan(product is null ? ProductImportAction.Create : ProductImportAction.Update, null, item, product, unitId);
    }

    /// <summary>As categorias da planilha que ainda não existem nascem antes das linhas (padrão propositivo: nada digitado duas vezes).</summary>
    private async Task<Dictionary<string, Guid>> EnsureCategoriesAsync(List<ProductImportRow> rows, CancellationToken ct)
    {
        var wanted = rows.Select(r => ProductImportRules.Cell(r.Cells, ProductImportColumns.Category)).OfType<string>()
            .GroupBy(CategoryKey, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var existing = (await db.ProductCategories.AsNoTracking().Select(c => new CategoryInfo(c.Id, c.Name, c.IsActive)).ToListAsync(ct))
            .GroupBy(c => CategoryKey(c.Name), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var (key, name) in wanted)
        {
            if (existing.TryGetValue(key, out var category))
            {
                if (category.IsActive)
                {
                    ids[key] = category.Id;
                }

                continue;
            }

            var created = await categories.CreateAsync(new ProductCategoryInput(name, null, 0, null, null), ct);
            if (created.IsSuccess)
            {
                ids[key] = created.Value.Id;
            }

            db.ChangeTracker.Clear();
        }

        return ids;
    }

    /// <summary>
    /// Uma linha numa transação: cria (item novo ou existente, código de barras) ou atualiza (nome e categoria, código de barras novo) pelos
    /// serviços do Catálogo; o preço só com a permissão de quem confirmou (CA 5); o saldo inicial pelo motor do Estoque, com o produto como
    /// origem: a segunda vez devolve o movimento já lançado e não lança de novo (CA 6). Devolve o motivo quando a linha não entra.
    /// </summary>
    private async Task<string?> ApplyRowAsync(ProductImportRun run, ProductImportRow row, Lookups found, Dictionary<string, Guid> categoryIds, Counters counters, CancellationToken ct)
    {
        var plan = Resolve(row.Line, row.Cells, found, !run.ApplyPrices);
        if (plan.Error is not null)
        {
            return plan.Error;
        }

        var cells = row.Cells;
        var name = ProductImportRules.Cell(cells, ProductImportColumns.Name);
        var gtin = ProductImportRules.Gtin(cells);
        Guid? categoryId = ProductImportRules.Cell(cells, ProductImportColumns.Category) is { } categoryText && categoryIds.TryGetValue(CategoryKey(categoryText), out var cid) ? cid : null;
        string Failed(Error error) => $"Linha {row.Line}: {error.Message}";

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            Guid productId;
            Guid itemId;
            if (plan.Product is null)
            {
                var input = new ProductCreateInput(
                    name ?? plan.Item!.Name, categoryId, plan.Item?.Id,
                    plan.Item is null ? new NewItemInput(ProductImportRules.Cell(cells, ProductImportColumns.Code), plan.UnitId!.Value, ProductImportRules.Digits(ProductImportRules.Cell(cells, ProductImportColumns.Ncm))) : null,
                    null, false, true, false, null, gtin is null ? [] : [new BarcodeInput(gtin, null)]);
                var created = await products.CreateAsync(input, ct);
                if (created.IsFailure)
                {
                    return Failed(created.Error!);
                }

                productId = created.Value.Id;
                itemId = created.Value.ItemId;
            }
            else
            {
                var current = plan.Product;
                productId = current.Id;
                itemId = current.ItemId;
                var rename = name is not null && name != current.SaleName;
                var recategorize = categoryId is { } c && c != current.CategoryId;
                if (current.VariantOfProductId is null && (rename || recategorize))
                {
                    var entity = await db.Products.AsNoTracking().FirstAsync(p => p.Id == current.Id, ct);
                    var updated = await products.UpdateAsync(current.Id, new ProductUpdateInput(
                        name ?? entity.SaleName, categoryId ?? entity.CategoryId, entity.Plu, entity.IsWeighable, entity.SellablePos, entity.SellableOnline, null, entity.Version), ct);
                    if (updated.IsFailure)
                    {
                        return Failed(updated.Error!);
                    }
                }

                if (gtin is not null && !found.GtinOwners.ContainsKey(gtin))
                {
                    var added = await products.AddBarcodeAsync(current.Id, new BarcodeInput(gtin, null), ct);
                    if (added.IsFailure)
                    {
                        return Failed(added.Error!);
                    }
                }
            }

            if (run.ApplyPrices && ProductImportRules.Price(cells) is { } price)
            {
                var priced = await prices.ApplyAsync([productId], price, null, onlyIfChanged: true, ct);
                if (priced.IsFailure)
                {
                    return Failed(priced.Error!);
                }
            }

            var replayed = (bool?)null;
            if (run.LaunchStock && run.StockLocationId is { } locationId && ProductImportRules.Stock(cells) is { } quantity)
            {
                var posted = await stock.PostAsync(new StockPosting(
                    StockMovementType.OpeningBalance, itemId, locationId, null, quantity, ProductImportRules.Cost(cells), StockSources.CatalogImport, productId,
                    Reason: "Saldo inicial da importação de produtos"), ct);
                if (posted.IsFailure)
                {
                    return Failed(posted.Error!);
                }

                replayed = posted.Value.Replayed;
            }

            await transaction.CommitAsync(ct);
            if (plan.Product is null)
            {
                counters.Created++;
            }
            else
            {
                counters.Updated++;
            }

            if (replayed is { } again)
            {
                if (again)
                {
                    counters.StockAlreadyPosted++;
                }
                else
                {
                    counters.StockPosted++;
                }
            }

            // Os códigos desta linha já valem para as próximas do bloco.
            if (gtin is not null)
            {
                found.GtinOwners[gtin] = productId;
            }

            return null;
        }
        catch (DbUpdateException)
        {
            return $"Linha {row.Line}: não foi possível gravar a linha. Confira os dados e importe de novo.";
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private async Task<ProductImportView> ViewAsync(ProductImportRun run, List<ProductImportRow> rows, List<ProductImportError> errors, CancellationToken ct)
    {
        var canLaunch = await permissions.HasPermissionAsync("inventory", "adjust", ct);
        IReadOnlyList<ImportLocationChoice> choices = [];
        Guid? suggested = null;
        if (run.Status == ProductImportStatus.Previewed && run.StockRows > 0 && canLaunch)
        {
            var active = await locations.ActiveAsync(ct);
            choices = active.Select(l => new ImportLocationChoice(l.Id, l.Name)).ToList();
            suggested = (active.FirstOrDefault(l => CategoryKey(l.Name) == CategoryKey(StoreLocationName)) ?? active.FirstOrDefault(l => l.IsDefault) ?? (active.Count > 0 ? active[0] : null))?.Id;
        }

        string? locationName = null;
        if (run.StockLocationId is { } locationId)
        {
            var found = await locations.FindAsync([locationId], ct);
            locationName = found.Count > 0 ? found[0].Name : null;
        }

        IReadOnlyList<string> newCategories = [];
        if (run.Status == ProductImportStatus.Previewed)
        {
            var existing = (await db.ProductCategories.AsNoTracking().Select(c => c.Name).ToListAsync(ct)).Select(CategoryKey).ToHashSet(StringComparer.Ordinal);
            newCategories = rows.Select(r => ProductImportRules.Cell(r.Cells, ProductImportColumns.Category)).OfType<string>()
                .GroupBy(CategoryKey, StringComparer.Ordinal).Where(g => !existing.Contains(g.Key)).Select(g => g.First()).Order(StringComparer.CurrentCulture).ToList();
        }

        return new ProductImportView(
            run.Id, run.FileName, run.Status, run.TotalRows, run.ToCreate, run.ToUpdate, errors.Count,
            errors.OrderBy(e => e.Line).Take(ErrorsShown).Select(e => e.Message).ToList(), run.PricesIgnored, run.StockRows, canLaunch, choices, suggested, newCategories,
            run.InBackground, run.Processed, rows.Count, run.Created, run.Updated, run.ErrorRows + run.Failed, run.StockPosted, run.StockAlreadyPosted, run.LaunchStock,
            locationName, run.CreatedAt, run.CompletedAt, run.FailureMessage);
    }
}
