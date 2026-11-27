using BemAqui.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Inventory.Infrastructure;

/// <summary>
/// Reservas (EST-02, ADR 0023 §2 e §6): uma linha por OS, item e local; os <c>CHECK</c>s das quantidades entram por SQL na migration
/// <c>InventoryReservations</c>. A OS e o projeto são de outros módulos: só o id, sem FK (ADR 0006).
/// </summary>
internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public const string UniqueIndex = "ux_reservations_order_item_location";
    public const string SourceIndex = "ux_reservations_source_item_location";
    public const string SourceCheck = "ck_reservations_single_origin";

    public void Configure(EntityTypeBuilder<Reservation> b)
    {
        // VND-02a CA 11: exatamente uma origem por linha, a OS ou o documento (tipo e id).
        b.ToTable("reservations", InventorySchema.Name, t => t.HasCheckConstraint(SourceCheck,
            "(production_order_id IS NOT NULL AND source_type IS NULL AND source_id IS NULL) OR (production_order_id IS NULL AND source_type IS NOT NULL AND source_id IS NOT NULL)"));
        // As reservas de documento (pedido de venda) ficam fora de todas as consultas da OS (EST-02/03/05): só o contrato ISalesReservations as lê.
        b.HasQueryFilter(e => e.ProductionOrderId != null);
        b.Property(e => e.SourceType).HasMaxLength(40);
        b.Property(e => e.RequestedQuantity).HasPrecision(18, 4);
        b.Property(e => e.Quantity).HasPrecision(18, 4);
        b.Property(e => e.QuantityIssued).HasPrecision(18, 4);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Origin).HasMaxLength(120);
        b.Ignore(e => e.Shortage);
        b.HasOne<Item>().WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Location>().WithMany().HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.Restrict);
        // Parcial desde a VND-02a (mesmas colunas: nada muda para a OS); com NULL, o índice antigo só protegia por acaso do NULLS DISTINCT.
        b.HasIndex(e => new { e.ProductionOrderId, e.ItemId, e.LocationId }).IsUnique().HasFilter("production_order_id IS NOT NULL").HasDatabaseName(UniqueIndex);
        b.HasIndex(e => new { e.SourceType, e.SourceId, e.ItemId, e.LocationId }).IsUnique().HasFilter("source_type IS NOT NULL").HasDatabaseName(SourceIndex);
        b.HasIndex(e => e.ExpiresAt).HasFilter("expires_at IS NOT NULL AND status = 'Active'").HasDatabaseName("ix_reservations_expires_at");
        b.HasIndex(e => new { e.ItemId, e.Status }).HasDatabaseName("ix_reservations_item_status");
        b.HasIndex(e => e.LocationId).HasDatabaseName("ix_reservations_location_id");
    }
}

internal sealed class ReservationChangeConfiguration : IEntityTypeConfiguration<ReservationChange>
{
    public void Configure(EntityTypeBuilder<ReservationChange> b)
    {
        b.ToTable("reservation_changes", InventorySchema.Name);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.QuantityDelta).HasPrecision(18, 4);
        b.Property(e => e.QuantityAfter).HasPrecision(18, 4);
        b.Property(e => e.RequestedAfter).HasPrecision(18, 4);
        b.Property(e => e.ShortageAfter).HasPrecision(18, 4);
        b.Property(e => e.Origin).HasMaxLength(120);
        b.HasOne<Reservation>().WithMany().HasForeignKey(e => e.ReservationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.ProductionOrderId, e.At }).HasDatabaseName("ix_reservation_changes_order_at");
        b.HasIndex(e => new { e.ItemId, e.At }).HasDatabaseName("ix_reservation_changes_item_at");
        b.HasIndex(e => e.ReservationId).HasDatabaseName("ix_reservation_changes_reservation_id");
    }
}
