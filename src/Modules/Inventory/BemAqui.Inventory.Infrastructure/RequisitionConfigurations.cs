using BemAqui.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Inventory.Infrastructure;

/// <summary>
/// Requisições de material da OS (EST-03): número único (REQ), situação e tipo como texto; os <c>CHECK</c>s das quantidades entram por
/// SQL na migration <c>InventoryRequisitions</c>. A OS, a etapa e o projeto são de outros módulos: só o id, sem FK (ADR 0006).
/// </summary>
internal sealed class MaterialRequisitionConfiguration : IEntityTypeConfiguration<MaterialRequisition>
{
    public void Configure(EntityTypeBuilder<MaterialRequisition> b)
    {
        b.ToTable("material_requisitions", InventorySchema.Name);
        b.Property(e => e.Number).HasMaxLength(30);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.StepName).HasMaxLength(120);
        b.Property(e => e.DecisionComment).HasMaxLength(500);
        b.Property(e => e.RejectReason).HasMaxLength(200);
        b.Property(e => e.ReversalReason).HasMaxLength(200);
        b.Property(e => e.OverBomValue).HasPrecision(18, 2);
        b.HasIndex(e => e.Number).IsUnique().HasDatabaseName("ux_material_requisitions_number");
        b.HasIndex(e => new { e.ProductionOrderId, e.RequestedAt }).HasDatabaseName("ix_material_requisitions_order_requested_at");
        b.HasIndex(e => new { e.Status, e.RequestedAt }).HasDatabaseName("ix_material_requisitions_status_requested_at");
        b.HasIndex(e => e.ProjectId).HasDatabaseName("ix_material_requisitions_project_id");
    }
}

internal sealed class MaterialRequisitionLineConfiguration : IEntityTypeConfiguration<MaterialRequisitionLine>
{
    public void Configure(EntityTypeBuilder<MaterialRequisitionLine> b)
    {
        b.ToTable("material_requisition_lines", InventorySchema.Name);
        b.Property(e => e.Quantity).HasPrecision(18, 4);
        b.Property(e => e.OverQuantity).HasPrecision(18, 4);
        b.Property(e => e.OverReason).HasMaxLength(200);
        b.HasOne<MaterialRequisition>().WithMany().HasForeignKey(e => e.RequisitionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Item>().WithMany().HasForeignKey(e => e.ItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Location>().WithMany().HasForeignKey(e => e.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Lot>().WithMany().HasForeignKey(e => e.LotId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Reservation>().WithMany().HasForeignKey(e => e.ReservationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MaterialRequisitionLine>().WithMany().HasForeignKey(e => e.ReturnsLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.RequisitionId, e.Sequence }).IsUnique().HasDatabaseName("ux_material_requisition_lines_requisition_sequence");
        b.HasIndex(e => e.ReturnsLineId).HasFilter("returns_line_id IS NOT NULL").HasDatabaseName("ix_material_requisition_lines_returns_line_id");
        b.HasIndex(e => e.MovementId).HasFilter("movement_id IS NOT NULL").HasDatabaseName("ix_material_requisition_lines_movement_id");
        b.HasIndex(e => e.ItemId).HasDatabaseName("ix_material_requisition_lines_item_id");
    }
}
