using BemAqui.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Inventory.Infrastructure;

/// <summary>
/// Falta de saldo da venda do caixa (PDV-05): uma por origem, item e local (índice único, a segunda entrega não abre outra); a situação como
/// texto; os movimentos da baixa aplicada e do estorno com FK para o movimento. A origem (a venda) é só o id e o número, sem FK (outro módulo).
/// </summary>
internal sealed class StockShortfallConfiguration : IEntityTypeConfiguration<StockShortfall>
{
    public void Configure(EntityTypeBuilder<StockShortfall> b)
    {
        b.ToTable("stock_shortfalls", InventorySchema.Name, t => t.HasCheckConstraint("ck_stock_shortfalls_quantity", "quantity > 0"));
        b.Property(e => e.Quantity).HasPrecision(18, 4);
        b.Property(e => e.SourceType).HasMaxLength(40);
        b.Property(e => e.SourceNumber).HasMaxLength(40);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.HasOne<Item>().WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Location>().WithMany().HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StockMovement>().WithMany().HasForeignKey(e => e.MovementId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StockMovement>().WithMany().HasForeignKey(e => e.ReversalMovementId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.SourceType, e.SourceId, e.ItemId, e.LocationId }).IsUnique().HasDatabaseName("ux_stock_shortfalls_source_item_location");
        b.HasIndex(e => new { e.LocationId, e.ItemId, e.Status }).HasDatabaseName("ix_stock_shortfalls_location_item_status");
        b.HasIndex(e => e.Status).HasFilter("status = 'Open'").HasDatabaseName("ix_stock_shortfalls_open");
    }
}
