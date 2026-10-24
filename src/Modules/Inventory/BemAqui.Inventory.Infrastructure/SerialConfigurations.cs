using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Inventory.Infrastructure;

/// <summary>
/// Séries do produto acabado e lotes consumidos (PRD-06a2): série única por item (CA 13); o <c>CHECK</c> da situação e das quantidades
/// entra por SQL na migration <c>InventorySerials</c>. A OS e o projeto são de outros módulos: só o id, sem FK (ADR 0006).
/// </summary>
internal sealed class SerialConfiguration : IEntityTypeConfiguration<Serial>
{
    public const string UniqueIndex = "ux_serials_item_number";

    public void Configure(EntityTypeBuilder<Serial> b)
    {
        b.ToTable("serials", InventorySchema.Name);
        b.Property(e => e.SerialNumber).HasMaxLength(SerialRules.MaxLength);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.HasOne<Item>().WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Location>().WithMany().HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StockMovement>().WithMany().HasForeignKey(e => e.EntryMovementId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.ItemId, e.SerialNumber }).IsUnique().HasDatabaseName(UniqueIndex);
        b.HasIndex(e => e.ProductionOrderId).HasFilter("production_order_id IS NOT NULL").HasDatabaseName("ix_serials_production_order_id");
        b.HasIndex(e => e.ProjectId).HasFilter("project_id IS NOT NULL").HasDatabaseName("ix_serials_project_id");
        b.HasIndex(e => new { e.ItemId, e.Status }).HasDatabaseName("ix_serials_item_status");
        b.HasIndex(e => e.LocationId).HasDatabaseName("ix_serials_location_id");
        b.HasIndex(e => e.EntryMovementId).HasDatabaseName("ix_serials_entry_movement_id");
    }
}

internal sealed class SerialComponentConfiguration : IEntityTypeConfiguration<SerialComponent>
{
    public void Configure(EntityTypeBuilder<SerialComponent> b)
    {
        b.ToTable("serial_components", InventorySchema.Name);
        b.Property(e => e.Quantity).HasPrecision(18, 4);
        b.HasOne<Serial>().WithMany().HasForeignKey(e => e.SerialId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Item>().WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Lot>().WithMany().HasForeignKey(e => e.LotId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.ProductionOrderId, e.ReversedAt }).HasDatabaseName("ix_serial_components_order");
        b.HasIndex(e => e.LotId).HasFilter("lot_id IS NOT NULL").HasDatabaseName("ix_serial_components_lot_id");
        b.HasIndex(e => e.SerialId).HasFilter("serial_id IS NOT NULL").HasDatabaseName("ix_serial_components_serial_id");
        b.HasIndex(e => e.ItemId).HasDatabaseName("ix_serial_components_item_id");
    }
}

internal sealed class SerialSequenceConfiguration : IEntityTypeConfiguration<SerialSequence>
{
    public void Configure(EntityTypeBuilder<SerialSequence> b)
    {
        b.ToTable("serial_sequences", InventorySchema.Name);
        b.HasKey(e => new { e.ItemId, e.Year }).HasName("pk_serial_sequences");
        b.HasOne<Item>().WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
