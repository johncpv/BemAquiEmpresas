using BemAqui.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Inventory.Infrastructure;

/// <summary>
/// Motor de estoque no banco (ADR 0023 §2 e §4). Os <c>CHECK</c>s da posição e do lote e o trigger de imutabilidade dos movimentos
/// entram por SQL na migration <c>InventoryStockEngine</c>.
/// </summary>
internal sealed class StockPositionConfiguration : IEntityTypeConfiguration<StockPosition>
{
    public const string UniqueIndex = "ux_stock_positions_item_location";

    public void Configure(EntityTypeBuilder<StockPosition> b)
    {
        b.ToTable("stock_positions", InventorySchema.Name);
        b.Property(e => e.QuantityOnHand).HasPrecision(18, 4);
        b.Property(e => e.QuantityReserved).HasPrecision(18, 4);
        b.Ignore(e => e.Available);
        b.HasOne<Item>().WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Location>().WithMany().HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.ItemId, e.LocationId }).IsUnique().HasDatabaseName(UniqueIndex);
        b.HasIndex(e => e.LocationId).HasDatabaseName("ix_stock_positions_location_id");
    }
}

internal sealed class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
{
    public void Configure(EntityTypeBuilder<StockBalance> b)
    {
        b.ToTable("stock_balances", InventorySchema.Name);
        b.Property(e => e.QuantityOnHand).HasPrecision(18, 4);
        b.HasOne<Item>().WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Location>().WithMany().HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Lot>().WithMany().HasForeignKey(e => e.LotId).OnDelete(DeleteBehavior.Restrict);
        // Uma linha por item, local e lote, também para o item sem lote (lot_id nulo): NULLS NOT DISTINCT (CA 15).
        b.HasIndex(e => new { e.ItemId, e.LocationId, e.LotId }).IsUnique().AreNullsDistinct(false).HasDatabaseName("ux_stock_balances_item_location_lot");
        b.HasIndex(e => e.LotId).HasDatabaseName("ix_stock_balances_lot_id");
    }
}

internal sealed class LotConfiguration : IEntityTypeConfiguration<Lot>
{
    public void Configure(EntityTypeBuilder<Lot> b)
    {
        b.ToTable("lots", InventorySchema.Name);
        b.Property(e => e.LotNumber).HasMaxLength(40);
        b.Property(e => e.CorrectionReason).HasMaxLength(500);
        b.HasOne<Item>().WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.ItemId, e.LotNumber }).IsUnique().HasDatabaseName("ux_lots_item_number");
        // EST-04 CA 10: busca exata ou por prefixo pelo número, em todos os itens (text_pattern_ops atende o LIKE 'x%' parametrizado).
        b.HasIndex(e => e.LotNumber).HasOperators("text_pattern_ops").HasDatabaseName("ix_lots_lot_number");
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public const string IdempotencyIndex = "ux_stock_movements_source_leg_type";

    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.ToTable("stock_movements", InventorySchema.Name);
        b.Property(e => e.Number).HasMaxLength(30);
        b.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Quantity).HasPrecision(18, 4);
        b.Property(e => e.UnitCost).HasPrecision(18, 6);
        b.Property(e => e.TotalCost).HasPrecision(18, 2);
        b.Property(e => e.BalanceAfter).HasPrecision(18, 4);
        b.Property(e => e.AverageCostAfter).HasPrecision(18, 6);
        // Ordem de lançamento do kardex: sequência do banco (identity), nunca a data de negócio (ADR 0023 §4).
        b.Property(e => e.PostedSeq).UseIdentityAlwaysColumn();
        b.Property(e => e.SourceType).HasMaxLength(30);
        b.Property(e => e.SourceNumber).HasMaxLength(30);
        b.Property(e => e.Reason).HasMaxLength(200);
        b.Property(e => e.Origin).HasMaxLength(20);
        b.Ignore(e => e.IsManual);
        b.HasOne<Item>().WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Location>().WithMany().HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Lot>().WithMany().HasForeignKey(e => e.LotId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StockMovement>().WithMany().HasForeignKey(e => e.ReversesMovementId).OnDelete(DeleteBehavior.Restrict);
        // A mesma perna do mesmo documento nunca lança duas vezes (CA 25); o estorno é único por movimento (CA 20).
        b.HasIndex(e => new { e.SourceType, e.SourceId, e.Leg, e.Type }).IsUnique().HasFilter("reverses_movement_id IS NULL").HasDatabaseName(IdempotencyIndex);
        b.HasIndex(e => e.ReversesMovementId).IsUnique().HasFilter("reverses_movement_id IS NOT NULL").HasDatabaseName("ux_stock_movements_reverses");
        b.HasIndex(e => new { e.ItemId, e.PostedSeq }).HasDatabaseName("ix_stock_movements_item_seq");
        b.HasIndex(e => e.PostedSeq).HasDatabaseName("ix_stock_movements_posted_seq");
        b.HasIndex(e => new { e.LocationId, e.ItemId }).HasDatabaseName("ix_stock_movements_location_item");
        b.HasIndex(e => e.ProjectId).HasFilter("project_id IS NOT NULL").HasDatabaseName("ix_stock_movements_project_id");
        b.HasIndex(e => e.ProductionOrderId).HasFilter("production_order_id IS NOT NULL").HasDatabaseName("ix_stock_movements_production_order_id");
        b.HasIndex(e => e.Number).HasDatabaseName("ix_stock_movements_number");
        b.HasIndex(e => new { e.Origin, e.PostedAt }).HasDatabaseName("ix_stock_movements_origin_posted_at");
    }
}

internal sealed class ManualPostingConfiguration : IEntityTypeConfiguration<ManualPosting>
{
    public void Configure(EntityTypeBuilder<ManualPosting> b)
    {
        b.ToTable("manual_postings", InventorySchema.Name);
        b.Property(e => e.Number).HasMaxLength(30);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.ApprovalReason).HasMaxLength(200);
        b.Property(e => e.EstimatedValue).HasPrecision(18, 2);
        b.Property(e => e.Reason).HasMaxLength(200);
        b.Property(e => e.DecisionComment).HasMaxLength(500);
        b.HasIndex(e => e.Number).IsUnique().HasDatabaseName("ux_manual_postings_number");
        b.HasIndex(e => new { e.Status, e.RequestedAt }).HasDatabaseName("ix_manual_postings_status_requested_at");
    }
}

internal sealed class ManualPostingLineConfiguration : IEntityTypeConfiguration<ManualPostingLine>
{
    public void Configure(EntityTypeBuilder<ManualPostingLine> b)
    {
        b.ToTable("manual_posting_lines", InventorySchema.Name);
        b.Property(e => e.LotNumber).HasMaxLength(40);
        b.Property(e => e.Quantity).HasPrecision(18, 4);
        b.Property(e => e.EnteredQuantity).HasPrecision(18, 4);
        b.Property(e => e.UnitCost).HasPrecision(18, 6);
        b.Property(e => e.ReversalReason).HasMaxLength(200);
        b.HasOne<ManualPosting>().WithMany().HasForeignKey(e => e.PostingId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Item>().WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Location>().WithMany().HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Location>().WithMany().HasForeignKey(e => e.ToLocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Lot>().WithMany().HasForeignKey(e => e.LotId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.PostingId, e.Sequence }).IsUnique().HasDatabaseName("ux_manual_posting_lines_posting_sequence");
        b.HasIndex(e => e.MovementId).HasFilter("movement_id IS NOT NULL").HasDatabaseName("ix_manual_posting_lines_movement_id");
        b.HasIndex(e => e.InMovementId).HasFilter("in_movement_id IS NOT NULL").HasDatabaseName("ix_manual_posting_lines_in_movement_id");
    }
}

internal sealed class InventoryParametersConfiguration : IEntityTypeConfiguration<InventoryParameters>
{
    public void Configure(EntityTypeBuilder<InventoryParameters> b)
    {
        b.ToTable("parameters", InventorySchema.Name);
        b.HasKey(e => e.Id).HasName("pk_inventory_parameters");
        b.Property(e => e.ManualApprovalAmount).HasPrecision(18, 2);
        b.Property(e => e.ManualCostDeviationPct).HasPrecision(5, 2);
    }
}
