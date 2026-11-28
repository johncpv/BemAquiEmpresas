using BemAqui.Sales.Application;
using BemAqui.Sales.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Sales.Infrastructure;

/// <summary>
/// <c>sales.orders</c> (VND-02a): número único; <c>(origin, source_app_id, external_id)</c> único com nulos iguais (CA 2); dinheiro <c>numeric(18,2)</c>;
/// a reserva e o título são de outros módulos (só o id, ADR 0006).
/// </summary>
internal sealed class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> b)
    {
        b.ToTable("orders", SalesSchema.Name, t => t.HasCheckConstraint("ck_orders_totals", "subtotal >= 0 AND discount_total >= 0 AND order_discount >= 0 AND total >= 0"));
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Number).HasMaxLength(40);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Origin).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.PaymentKind).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.SourceAppName).HasMaxLength(80);
        b.Property(e => e.ExternalId).HasMaxLength(80);
        b.Property(e => e.ExternalHash).HasMaxLength(64);
        b.Property(e => e.Note).HasMaxLength(500);
        b.Property(e => e.CancelReason).HasMaxLength(200);
        foreach (var money in new[] { nameof(SalesOrder.Subtotal), nameof(SalesOrder.DiscountTotal), nameof(SalesOrder.OrderDiscount), nameof(SalesOrder.Total) })
        {
            b.Property(money).HasPrecision(18, 2);
        }

        b.Ignore(e => e.FromApp);
        b.HasIndex(e => e.Number).IsUnique().HasDatabaseName(SalesOrderService.NumberIndex);
        b.HasIndex(e => new { e.Origin, e.SourceAppId, e.ExternalId }).IsUnique().AreNullsDistinct(false).HasFilter("external_id IS NOT NULL")
            .HasDatabaseName(SalesOrderService.ExternalIndex);
        b.HasIndex(e => new { e.Status, e.UpdatedAt }).HasDatabaseName("ix_orders_status_updated");
        b.HasIndex(e => e.HoldUntil).HasFilter("hold_until IS NOT NULL").HasDatabaseName("ix_orders_hold_until");
        b.HasIndex(e => e.ReceivableId).HasFilter("receivable_id IS NOT NULL");
        b.HasIndex(e => new { e.CustomerId, e.SourceAppId });
        b.HasIndex(e => e.SourceConnectionId).HasFilter("source_connection_id IS NOT NULL");
    }
}

internal sealed class SalesOrderLineConfiguration : IEntityTypeConfiguration<SalesOrderLine>
{
    public void Configure(EntityTypeBuilder<SalesOrderLine> b)
    {
        b.ToTable("order_lines", SalesSchema.Name, t => t.HasCheckConstraint("ck_order_lines_quantity", "quantity > 0 AND discount >= 0 AND unit_price >= 0"));
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Description).HasMaxLength(200);
        b.Property(e => e.UnitCode).HasMaxLength(10);
        b.Property(e => e.Quantity).HasPrecision(18, 4);
        b.Property(e => e.UnitPrice).HasPrecision(18, 2);
        b.Property(e => e.Discount).HasPrecision(18, 2);
        b.Property(e => e.Total).HasPrecision(18, 2);
        b.Ignore(e => e.Gross);
        b.HasOne<SalesOrder>().WithMany().HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.OrderId, e.LineNumber });
        b.HasIndex(e => e.ProductId);
    }
}

internal sealed class SalesOrderTransitionConfiguration : IEntityTypeConfiguration<SalesOrderTransition>
{
    public void Configure(EntityTypeBuilder<SalesOrderTransition> b)
    {
        b.ToTable("order_transitions", SalesSchema.Name);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.FromStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.ToStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Origin).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Action).HasMaxLength(30);
        b.Property(e => e.ActorLabel).HasMaxLength(120);
        b.Property(e => e.Reason).HasMaxLength(200);
        b.HasOne<SalesOrder>().WithMany().HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.OrderId, e.At });
    }
}

internal sealed class SalesOrderParametersConfiguration : IEntityTypeConfiguration<SalesOrderParameters>
{
    public void Configure(EntityTypeBuilder<SalesOrderParameters> b)
    {
        b.ToTable("order_parameters", SalesSchema.Name);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.DiscountLimitPct).HasPrecision(5, 2);
        b.Property(e => e.AppDiscountLimitPct).HasPrecision(5, 2);
        b.Property(e => e.AppMaxLineQty).HasPrecision(18, 4);
        b.Property(e => e.AppMaxOrderQty).HasPrecision(18, 4);
    }
}
