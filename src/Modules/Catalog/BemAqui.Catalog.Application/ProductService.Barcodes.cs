using BemAqui.Catalog.Contracts;
using BemAqui.Catalog.Domain;
using BemAqui.Inventory.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Catalog.Application;

public sealed partial class ProductService
{
    public const string BarcodeIndex = "ix_product_barcodes_gtin";

    public async Task<Result<IReadOnlyList<BarcodeView>>> BarcodesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await db.Products.AnyAsync(p => p.Id == id, cancellationToken))
        {
            return NotFound();
        }

        return Result<IReadOnlyList<BarcodeView>>.Success(await db.ProductBarcodes.AsNoTracking().Where(b => b.ProductId == id).OrderBy(b => b.CreatedAt)
            .Select(b => new BarcodeView(b.Id, b.Gtin, b.PackQuantity, b.CreatedAt)).ToListAsync(cancellationToken));
    }

    /// <summary>
    /// Código de barras novo (CA 5 a 7): dígito verificador conferido (422 "Código de barras inválido. Confira os dígitos."), único no tenant
    /// (409 com o nome do produto que já o usa) e a quantidade da embalagem (1 na unidade). O pai de grade não tem código: vai na variação.
    /// </summary>
    public async Task<Result<BarcodeView>> AddBarcodeAsync(Guid id, BarcodeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        if (product.HasVariants)
        {
            return Invalid("gtin", "O produto tem variações: cada código de barras vai na sua variação.");
        }

        var gtin = GtinRules.Normalize(input.Gtin);
        if (ValidateBarcodes([(gtin, input.PackQuantity ?? 1m)]) is { } invalid)
        {
            return invalid;
        }

        if (await CheckUniqueAsync(null, [gtin], null, cancellationToken) is { } taken)
        {
            return taken;
        }

        var barcode = new ProductBarcode { ProductId = id, Gtin = gtin, PackQuantity = input.PackQuantity ?? 1m, CreatedAt = clock.GetUtcNow() };
        db.ProductBarcodes.Add(barcode);
        events.Publish(new ProductChanged(id));
        if (await SaveUniqueAsync(cancellationToken) is { } conflict)
        {
            db.Entry(barcode).State = EntityState.Detached;
            return conflict;
        }

        return new BarcodeView(barcode.Id, barcode.Gtin, barcode.PackQuantity, barcode.CreatedAt);
    }

    /// <summary>Remove o código (auditado); o GTIN volta a ficar livre.</summary>
    public async Task<Result> RemoveBarcodeAsync(Guid id, Guid barcodeId, CancellationToken cancellationToken = default)
    {
        var barcode = await db.ProductBarcodes.FirstOrDefaultAsync(b => b.Id == barcodeId && b.ProductId == id, cancellationToken);
        if (barcode is null)
        {
            return Error.NotFound("barcode_not_found", "Código de barras não encontrado neste produto.");
        }

        db.ProductBarcodes.Remove(barcode);
        events.Publish(new ProductChanged(id));
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Gera a grade (CA 15): cada combinação dos atributos que ainda não existe vira uma variação, com item próprio (Mercadoria, com a unidade e
    /// o NCM do item do pai), na categoria e com a foto do pai. O preço, quando vem (só com <c>edit_price</c>), vale para todas; sem ele, cada
    /// variação nasce com o preço do pai se quem gera pode alterar preço, e sem preço se não pode.
    /// </summary>
    public async Task<Result<ProductDetail>> CreateVariantsAsync(Guid id, VariantsInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var canPrice = await permissions.HasPermissionAsync(Module, "edit_price", cancellationToken);
        if (input.Price is not null && !canPrice)
        {
            return PriceForbidden();
        }

        if (input.Price is { } given && !ProductRules.IsValidPrice(given))
        {
            return Invalid("price", "Informe um preço maior que zero, com até 2 casas.");
        }

        var parent = await db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (parent is null)
        {
            return NotFound();
        }

        if (parent.VariantOfProductId is not null)
        {
            return Invalid("attributes", "Esta já é uma variação. Gere a grade no produto pai.");
        }

        if (ValidateAttributes(input.Attributes, out var attributes) is { } badAttributes)
        {
            return badAttributes;
        }

        var combos = ProductRules.Combinations(attributes);
        var existing = (await db.Products.AsNoTracking().Where(v => v.VariantOfProductId == id).Select(v => v.VariantAttributes).ToListAsync(cancellationToken))
            .Select(ProductRules.CombinationKey).ToHashSet(StringComparer.Ordinal);
        var missing = combos.Where(c => !existing.Contains(ProductRules.CombinationKey(c))).ToList();
        if (missing.Count == 0)
        {
            return Invalid("attributes", "Todas essas combinações já existem.");
        }

        if (existing.Count + missing.Count > ProductRules.MaxVariantsPerGrade)
        {
            return Invalid("attributes", $"A grade passaria de {ProductRules.MaxVariantsPerGrade} variações. Divida em mais de um produto.");
        }

        var parentItems = await items.FindAsync([parent.ItemId], cancellationToken);
        var parentItem = parentItems.Count == 0 ? null : parentItems[0];
        if (parentItem is null)
        {
            return Invalid("attributes", "O item do estoque do produto pai não foi encontrado.");
        }

        decimal? parentPrice = await prices.CurrentAsync(parent.Id, cancellationToken);
        var price = input.Price ?? (canPrice ? parentPrice : null);
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var created = new List<Guid>();
        foreach (var combo in missing)
        {
            var name = ProductRules.VariantName(parent.SaleName, combo);
            var item = await registration.CreateAsync(new NewItemData(ItemType.Merchandise, null, name, parentItem.UnitId, parentItem.Ncm), cancellationToken);
            if (item.IsFailure)
            {
                return item.Error!;
            }

            var variant = new Product
            {
                ItemId = item.Value, CategoryId = parent.CategoryId, SaleName = name, SellablePos = parent.SellablePos,
                SellableOnline = parent.SellableOnline, VariantOfProductId = parent.Id, VariantAttributes = combo, IsActive = parent.IsActive, CreatedAt = now,
            };
            db.Products.Add(variant);
            created.Add(variant.Id);
            events.Publish(new ProductChanged(variant.Id));
        }

        parent.HasVariants = true;
        events.Publish(new ProductChanged(parent.Id));
        if (await SaveUniqueAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        if (price is { } p)
        {
            var priced = await prices.ApplyAsync(created, p, null, false, cancellationToken);
            if (priced.IsFailure)
            {
                return priced.Error!;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>CA 16: nome, categoria e foto do pai descem para as variações (o nome da variação é o do pai com os atributos).</summary>
    private async Task InheritAsync(Product parent, CancellationToken ct)
    {
        foreach (var variant in await db.Products.Where(v => v.VariantOfProductId == parent.Id).ToListAsync(ct))
        {
            variant.SaleName = ProductRules.VariantName(parent.SaleName, variant.VariantAttributes ?? new Dictionary<string, string>());
            variant.CategoryId = parent.CategoryId;
            events.Publish(new ProductChanged(variant.Id));
        }
    }

    private static Error? ValidateAttributes(IReadOnlyList<VariantAttributeInput>? input, out List<(string Name, IReadOnlyList<string> Values)> attributes)
    {
        attributes = [];
        if (input is null || input.Count is 0 or > ProductRules.MaxAttributes)
        {
            return Invalid("attributes", $"Informe de 1 a {ProductRules.MaxAttributes} atributos (ex.: Cor e Tamanho).");
        }

        foreach (var attribute in input)
        {
            var name = (attribute.Name ?? string.Empty).Trim();
            var values = (attribute.Values ?? []).Select(v => (v ?? string.Empty).Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (name.Length is 0 or > ProductRules.AttributeTextMax || values.Count == 0 || values.Any(v => v.Length > ProductRules.AttributeTextMax))
            {
                return Invalid("attributes", $"Cada atributo tem nome e ao menos um valor, com até {ProductRules.AttributeTextMax} caracteres.");
            }

            if (attributes.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return Invalid("attributes", $"O atributo {name} foi informado duas vezes.");
            }

            attributes.Add((name, values));
        }

        return null;
    }
}
