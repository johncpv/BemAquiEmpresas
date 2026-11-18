using System.Text.Json;
using BemAqui.Catalog.Application;
using BemAqui.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Catalog.Infrastructure;

internal static class CatalogSchema
{
    /// <summary>Schema do Catálogo de produtos (o nome <c>catalog</c> é do banco da plataforma).</summary>
    public const string Name = "catalog_products";
}

/// <summary><c>catalog_products.categories</c> (CA 14): nome único por nível entre as não excluídas; <c>seed_key</c> única (a "Geral").</summary>
internal sealed class ProductCategoryConfiguration : IEntityTypeConfiguration<ProductCategory>
{
    public void Configure(EntityTypeBuilder<ProductCategory> b)
    {
        b.ToTable("categories", CatalogSchema.Name);
        b.Property(e => e.Name).HasMaxLength(CategoryService.NameMax);
        b.Property(e => e.DefaultMarginPercent).HasPrecision(9, 4);
        b.Property(e => e.DefaultMarkupPercent).HasPrecision(9, 4);
        b.Property(e => e.SeedKey).HasMaxLength(50);
        b.HasOne<ProductCategory>().WithMany().HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.SeedKey).IsUnique().HasFilter("seed_key IS NOT NULL").HasDatabaseName("ix_categories_seed_key");
        b.HasIndex(e => e.ParentId).HasDatabaseName("ix_categories_parent_id");
    }
}

/// <summary>
/// <c>catalog_products.products</c>: um produto por item entre os não excluídos (CA 4), PLU único entre os não excluídos, busca por trecho
/// do nome com pg_trgm (CA 12 e 13), atributos da variação em jsonb e concorrência otimista pelo <c>xmin</c>. O item é só o id (ADR 0006).
/// </summary>
internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("products", CatalogSchema.Name);
        b.Property(e => e.SaleName).HasMaxLength(ProductRules.NameMax);
        b.Property(e => e.SearchText).HasMaxLength(200);
        b.Property(e => e.Plu).HasMaxLength(ProductRules.PluMaxDigits);
        b.Property(e => e.VariantAttributes).HasColumnType("jsonb").HasConversion(
            v => v == null ? null : JsonSerializer.Serialize(v, Json),
            s => s == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(s, Json),
            new ValueComparer<Dictionary<string, string>?>(
                (x, y) => x == null ? y == null : y != null && x.Count == y.Count && x.All(kv => y.ContainsKey(kv.Key) && y[kv.Key] == kv.Value),
                v => v == null ? 0 : v.Aggregate(0, (h, kv) => HashCode.Combine(h, kv.Key.GetHashCode(StringComparison.Ordinal), kv.Value.GetHashCode(StringComparison.Ordinal))),
                v => v == null ? null : new Dictionary<string, string>(v, StringComparer.Ordinal)));
        b.HasOne<ProductCategory>().WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Product>().WithMany().HasForeignKey(e => e.VariantOfProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.ItemId).IsUnique().HasFilter("NOT is_deleted").HasDatabaseName(ProductService.ItemIndex);
        b.HasIndex(e => e.Plu).IsUnique().HasFilter("plu IS NOT NULL AND NOT is_deleted").HasDatabaseName(ProductService.PluIndex);
        b.HasIndex(e => e.SearchText).HasDatabaseName("ix_products_search_text_trgm").HasMethod("gin").HasOperators("gin_trgm_ops");
        b.HasIndex(e => e.VariantOfProductId).HasFilter("variant_of_product_id IS NOT NULL").HasDatabaseName("ix_products_variant_of_product_id");
        b.HasIndex(e => e.CategoryId).HasDatabaseName("ix_products_category_id");
    }
}

/// <summary><c>catalog_products.product_barcodes</c> (CA 5 a 7): GTIN único no tenant; quantidade da embalagem em <c>numeric(18,4)</c>.</summary>
internal sealed class ProductBarcodeConfiguration : IEntityTypeConfiguration<ProductBarcode>
{
    public void Configure(EntityTypeBuilder<ProductBarcode> b)
    {
        b.ToTable("product_barcodes", CatalogSchema.Name);
        b.Property(e => e.Gtin).HasMaxLength(14);
        b.Property(e => e.PackQuantity).HasPrecision(18, 4);
        b.HasOne<Product>().WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.Gtin).IsUnique().HasDatabaseName(ProductService.BarcodeIndex);
        b.HasIndex(e => e.ProductId).HasDatabaseName("ix_product_barcodes_product_id");
    }
}

/// <summary><c>catalog_products.price_lists</c> (CA 8): versão incrementada a cada mudança de preço; <c>seed_key</c> única (a "Balcão").</summary>
internal sealed class PriceListConfiguration : IEntityTypeConfiguration<PriceList>
{
    public void Configure(EntityTypeBuilder<PriceList> b)
    {
        b.ToTable("price_lists", CatalogSchema.Name);
        b.Property(e => e.Name).HasMaxLength(80);
        b.Property(e => e.Channel).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.SeedKey).HasMaxLength(50);
        b.HasIndex(e => e.SeedKey).IsUnique().HasFilter("seed_key IS NOT NULL").HasDatabaseName("ix_price_lists_seed_key");
    }
}

/// <summary><c>catalog_products.price_list_items</c> (CA 8): preço <c>numeric(18,2)</c>; um por tabela, produto e faixa de quantidade.</summary>
internal sealed class PriceListItemConfiguration : IEntityTypeConfiguration<PriceListItem>
{
    public void Configure(EntityTypeBuilder<PriceListItem> b)
    {
        b.ToTable("price_list_items", CatalogSchema.Name);
        b.Property(e => e.Price).HasPrecision(18, 2);
        b.Property(e => e.MinQuantity).HasPrecision(18, 4);
        b.HasOne<PriceList>().WithMany().HasForeignKey(e => e.PriceListId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Product>().WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.PriceListId, e.ProductId, e.MinQuantity }).IsUnique().HasDatabaseName("ix_price_list_items_list_product_quantity");
        b.HasIndex(e => e.ProductId).HasDatabaseName("ix_price_list_items_product_id");
    }
}

/// <summary><c>catalog_products.price_changes</c> (CA 8): histórico só de inclusão, pelo produto e pela data.</summary>
internal sealed class PriceChangeConfiguration : IEntityTypeConfiguration<PriceChange>
{
    public void Configure(EntityTypeBuilder<PriceChange> b)
    {
        b.ToTable("price_changes", CatalogSchema.Name);
        b.Property(e => e.PreviousPrice).HasPrecision(18, 2);
        b.Property(e => e.NewPrice).HasPrecision(18, 2);
        b.HasOne<PriceList>().WithMany().HasForeignKey(e => e.PriceListId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Product>().WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.ProductId, e.ChangedAt }).HasDatabaseName("ix_price_changes_product_changed_at");
    }
}

/// <summary>
/// <c>catalog_products.import_runs</c> (CAT-02): resumo, progresso e resultado da importação por planilha; as linhas válidas e as com erro em
/// jsonb (para confirmar sem reenviar o arquivo e para baixar as linhas com erro). A troca de situação é atômica (UPDATE condicional).
/// </summary>
internal sealed class ProductImportRunConfiguration : IEntityTypeConfiguration<ProductImportRun>
{
    public void Configure(EntityTypeBuilder<ProductImportRun> b)
    {
        b.ToTable("import_runs", CatalogSchema.Name);
        // O Migrations também tem uma import_runs (schema migration): nomes próprios para a chave e o índice, sem renomear a dele.
        b.HasKey(e => e.Id).HasName("pk_product_import_runs");
        b.Property(e => e.FileName).HasMaxLength(255);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.RowsJson).HasColumnName("rows").HasColumnType("jsonb");
        b.Property(e => e.ErrorsJson).HasColumnName("errors").HasColumnType("jsonb");
        b.Property(e => e.FailureMessage).HasMaxLength(500);
        b.HasIndex(e => e.CreatedAt).HasDatabaseName("ix_product_import_runs_created_at");
    }
}
