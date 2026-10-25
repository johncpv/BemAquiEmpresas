using BemAqui.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Inventory.Infrastructure;

/// <summary>
/// Inventário (EST-05): número único (INV), escopo em jsonb, situação e escolha dos não contados como texto, e um só
/// <c>Counting</c> por local (índice único parcial, CA 2). O ajuste gerado é só o id e o número, sem FK (o ajuste já aponta para cá).
/// </summary>
internal sealed class InventoryCountConfiguration : IEntityTypeConfiguration<InventoryCount>
{
    public void Configure(EntityTypeBuilder<InventoryCount> b)
    {
        b.ToTable("inventory_counts", InventorySchema.Name);
        b.Property(e => e.Number).HasMaxLength(30);
        b.Property(e => e.Scope).HasColumnType("jsonb");
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.UncountedPolicy).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.AdjustmentNumber).HasMaxLength(30);
        b.Property(e => e.CancelReason).HasMaxLength(InventoryCountRules.MaxReason);
        b.HasOne<Location>().WithMany().HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.Number).IsUnique().HasDatabaseName("ux_inventory_counts_number");
        b.HasIndex(e => e.LocationId).IsUnique().HasFilter("status = 'Counting'").HasDatabaseName("ux_inventory_counts_location_counting");
        b.HasIndex(e => new { e.Status, e.SnapshotAt }).HasDatabaseName("ix_inventory_counts_status_snapshot_at");
        b.HasIndex(e => e.AdjustmentId).HasFilter("adjustment_id IS NOT NULL").HasDatabaseName("ix_inventory_counts_adjustment_id");
    }
}

internal sealed class InventoryCountLineConfiguration : IEntityTypeConfiguration<InventoryCountLine>
{
    public void Configure(EntityTypeBuilder<InventoryCountLine> b)
    {
        b.ToTable("inventory_count_lines", InventorySchema.Name);
        b.Property(e => e.SystemQty).HasPrecision(18, 4);
        b.Property(e => e.CountedQty).HasPrecision(18, 4);
        b.Property(e => e.EnteredQuantity).HasPrecision(18, 4);
        b.Property(e => e.SystemQtyAtCount).HasPrecision(18, 4);
        b.Property(e => e.MovementDelta).HasPrecision(18, 4);
        b.Property(e => e.Difference).HasPrecision(18, 4);
        b.Property(e => e.UnitCost).HasPrecision(18, 6);
        b.HasOne<InventoryCount>().WithMany().HasForeignKey(e => e.CountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Item>().WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Lot>().WithMany().HasForeignKey(e => e.LotId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InventoryCountLine>().WithMany().HasForeignKey(e => e.RecountOfLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.CountId, e.Sequence }).IsUnique().HasDatabaseName("ux_inventory_count_lines_count_sequence");
        // Item e lote uma vez entre as linhas vigentes (a recontagem substitui a anterior), também para o item sem lote.
        b.HasIndex(e => new { e.CountId, e.ItemId, e.LotId }).IsUnique().AreNullsDistinct(false).HasFilter("superseded = false")
            .HasDatabaseName("ux_inventory_count_lines_count_item_lot");
        b.HasIndex(e => e.ItemId).HasDatabaseName("ix_inventory_count_lines_item_id");
        b.HasIndex(e => e.RecountOfLineId).HasFilter("recount_of_line_id IS NOT NULL").HasDatabaseName("ix_inventory_count_lines_recount_of_line_id");
    }
}
