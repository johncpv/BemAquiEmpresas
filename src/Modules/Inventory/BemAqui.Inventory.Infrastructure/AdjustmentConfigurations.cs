using BemAqui.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Inventory.Infrastructure;

/// <summary>
/// Ajuste de estoque (EST-01b): número único (AJ), modo, origem, motivo e situação como texto; os <c>CHECK</c>s entram por SQL na
/// migration <c>InventoryStockAdjustments</c>. O inventário de origem (EST-05) é só o id e o número, sem FK.
/// </summary>
internal sealed class StockAdjustmentConfiguration : IEntityTypeConfiguration<StockAdjustment>
{
    public void Configure(EntityTypeBuilder<StockAdjustment> b)
    {
        b.ToTable("stock_adjustments", InventorySchema.Name);
        b.Property(e => e.Number).HasMaxLength(30);
        b.Property(e => e.Mode).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Origin).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.OriginNumber).HasMaxLength(30);
        b.Property(e => e.Reason).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.ReasonDetail).HasMaxLength(StockAdjustmentRules.MaxReasonDetail);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.DecisionComment).HasMaxLength(StockAdjustmentRules.MaxComment);
        b.Property(e => e.CancelReason).HasMaxLength(StockAdjustmentRules.MaxReasonDetail);
        b.Property(e => e.ReversalReason).HasMaxLength(StockAdjustmentRules.MaxReasonDetail);
        b.Property(e => e.SurplusValue).HasPrecision(18, 2);
        b.Property(e => e.ShortageValue).HasPrecision(18, 2);
        b.HasOne<Location>().WithMany().HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.Number).IsUnique().HasDatabaseName("ux_stock_adjustments_number");
        b.HasIndex(e => new { e.Status, e.RequestedAt }).HasDatabaseName("ix_stock_adjustments_status_requested_at");
        b.HasIndex(e => new { e.LocationId, e.Status }).HasDatabaseName("ix_stock_adjustments_location_status");
        b.HasIndex(e => e.OriginId).HasFilter("origin_id IS NOT NULL").HasDatabaseName("ix_stock_adjustments_origin_id");
    }
}

internal sealed class StockAdjustmentLineConfiguration : IEntityTypeConfiguration<StockAdjustmentLine>
{
    public void Configure(EntityTypeBuilder<StockAdjustmentLine> b)
    {
        b.ToTable("stock_adjustment_lines", InventorySchema.Name);
        b.Property(e => e.SystemQtyAtRequest).HasPrecision(18, 4);
        b.Property(e => e.CountedQty).HasPrecision(18, 4);
        b.Property(e => e.DeltaQty).HasPrecision(18, 4);
        b.Property(e => e.UnitCostEstimate).HasPrecision(18, 6);
        b.Property(e => e.EnteredQuantity).HasPrecision(18, 4);
        b.Property(e => e.SystemQtyAtApproval).HasPrecision(18, 4);
        b.Property(e => e.AppliedDeltaQty).HasPrecision(18, 4);
        b.Property(e => e.UnitCost).HasPrecision(18, 6);
        b.HasOne<StockAdjustment>().WithMany().HasForeignKey(e => e.AdjustmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Item>().WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Lot>().WithMany().HasForeignKey(e => e.LotId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StockMovement>().WithMany().HasForeignKey(e => e.MovementId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.AdjustmentId, e.Sequence }).IsUnique().HasDatabaseName("ux_stock_adjustment_lines_adjustment_sequence");
        // Item e lote uma vez por ajuste, também para o item sem lote (lot_id nulo).
        b.HasIndex(e => new { e.AdjustmentId, e.ItemId, e.LotId }).IsUnique().AreNullsDistinct(false).HasDatabaseName("ux_stock_adjustment_lines_adjustment_item_lot");
        b.HasIndex(e => e.ItemId).HasDatabaseName("ix_stock_adjustment_lines_item_id");
    }
}
