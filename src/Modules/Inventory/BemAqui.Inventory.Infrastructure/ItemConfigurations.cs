using BemAqui.Inventory.Application;
using BemAqui.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Inventory.Infrastructure;

internal sealed class MeasureUnitConfiguration : IEntityTypeConfiguration<MeasureUnit>
{
    public void Configure(EntityTypeBuilder<MeasureUnit> b)
    {
        b.ToTable("units", InventorySchema.Name);
        b.Property(e => e.Code).HasMaxLength(6);
        b.Property(e => e.Name).HasMaxLength(60);
        b.Property(e => e.SeedKey).HasMaxLength(50);
        b.HasIndex(e => e.Code).IsUnique().HasFilter("NOT is_deleted").HasDatabaseName(UnitService.CodeIndex);
        b.HasIndex(e => e.SeedKey).IsUnique().HasFilter("seed_key IS NOT NULL").HasDatabaseName("ix_units_seed_key");
    }
}

internal sealed class ItemCategoryConfiguration : IEntityTypeConfiguration<ItemCategory>
{
    public void Configure(EntityTypeBuilder<ItemCategory> b)
    {
        b.ToTable("item_categories", InventorySchema.Name);
        b.Property(e => e.Name).HasMaxLength(80);
        b.HasOne<ItemCategory>().WithMany().HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class WarrantyDefaultConfiguration : IEntityTypeConfiguration<WarrantyDefault>
{
    public void Configure(EntityTypeBuilder<WarrantyDefault> b)
    {
        b.ToTable("warranty_defaults", InventorySchema.Name);
        b.Property(e => e.ItemType).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(e => e.ItemType).IsUnique().HasDatabaseName("ix_warranty_defaults_item_type");
    }
}

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> b)
    {
        b.ToTable("items", InventorySchema.Name);
        b.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Code).HasMaxLength(30);
        b.Property(e => e.Name).HasMaxLength(150);
        b.Property(e => e.SearchText).HasMaxLength(200);
        b.Property(e => e.Ncm).HasMaxLength(8);
        b.Property(e => e.Cest).HasMaxLength(7);
        b.Property(e => e.MinStock).HasPrecision(18, 4);
        b.Property(e => e.AverageCost).HasPrecision(18, 6);
        b.Property(e => e.ImportSource).HasMaxLength(100);
        b.HasOne<MeasureUnit>().WithMany().HasForeignKey(e => e.UnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ItemCategory>().WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Restrict);
        // Código único entre os não excluídos (CA 15), também em gravação simultânea; busca por parte do código ou do nome (CA 19).
        b.HasIndex(e => e.Code).IsUnique().HasFilter("NOT is_deleted").HasDatabaseName(ItemService.CodeIndex);
        b.HasIndex(e => e.SearchText).HasDatabaseName("ix_items_search_text_trgm").HasMethod("gin").HasOperators("gin_trgm_ops");
        b.HasIndex(e => new { e.Type, e.IsActive }).HasDatabaseName("ix_items_type_active");
    }
}

internal sealed class ItemUnitConversionConfiguration : IEntityTypeConfiguration<ItemUnitConversion>
{
    public void Configure(EntityTypeBuilder<ItemUnitConversion> b)
    {
        b.ToTable("item_unit_conversions", InventorySchema.Name);
        b.Property(e => e.Factor).HasPrecision(18, 6);
        b.HasOne<Item>().WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MeasureUnit>().WithMany().HasForeignKey(e => e.FromUnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MeasureUnit>().WithMany().HasForeignKey(e => e.ToUnitId).OnDelete(DeleteBehavior.Restrict);
        // O par na mesma ordem é único no item; a ordem inversa é conferida no serviço (CA 2).
        b.HasIndex(e => new { e.ItemId, e.FromUnitId, e.ToUnitId }).IsUnique().HasFilter("NOT is_deleted").HasDatabaseName(ItemConversionService.PairIndex);
    }
}

internal sealed class ItemSupplierCodeConfiguration : IEntityTypeConfiguration<ItemSupplierCode>
{
    public void Configure(EntityTypeBuilder<ItemSupplierCode> b)
    {
        b.ToTable("item_supplier_codes", InventorySchema.Name);
        b.Property(e => e.SupplierCode).HasMaxLength(60);
        b.Property(e => e.SupplierUnit).HasMaxLength(10);
        b.Property(e => e.ConversionFactor).HasPrecision(18, 6);
        b.HasOne<Item>().WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Restrict);
        // Um código por fornecedor entre os não excluídos (CA 10); sem FK para parties (outro módulo, ADR 0006).
        b.HasIndex(e => new { e.PartyId, e.SupplierCode }).IsUnique().HasFilter("NOT is_deleted").HasDatabaseName(ItemSupplierCodeService.CodeIndex);
        b.HasIndex(e => e.ItemId).HasDatabaseName("ix_item_supplier_codes_item_id");
    }
}
