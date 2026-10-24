using BemAqui.Purchasing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Purchasing.Infrastructure;

/// <summary>Envios do pedido ao fornecedor (CMP-03b): <c>purchase_order_id</c> com a FK do pedido, endereços até 254 (cópia até 3 × 254).</summary>
internal sealed class PurchaseOrderDispatchConfiguration : IEntityTypeConfiguration<PurchaseOrderDispatch>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderDispatch> b)
    {
        b.ToTable("purchase_order_dispatches", PurchasingSchema.Name);
        b.Property(e => e.OrderId).HasColumnName("purchase_order_id");
        b.Property(e => e.To).HasMaxLength(254);
        b.Property(e => e.Cc).HasMaxLength(800);
        b.HasOne<PurchaseOrder>().WithMany().HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.OrderId).HasDatabaseName("ix_purchase_order_dispatches_purchase_order_id");
        b.HasIndex(e => e.CreatedAt).HasDatabaseName("ix_purchase_order_dispatches_created_at");
    }
}
