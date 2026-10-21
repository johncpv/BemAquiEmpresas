using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Finance.Infrastructure;

internal sealed class PaymentOrderConfiguration : IEntityTypeConfiguration<PaymentOrder>
{
    public void Configure(EntityTypeBuilder<PaymentOrder> b)
    {
        b.ToTable("payment_orders", FinanceSchema.Name);
        b.Property(e => e.Number).HasMaxLength(20);
        b.HasIndex(e => e.Number).IsUnique();
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Total).HasPrecision(18, 2);
        b.Property(e => e.CancelReason).HasMaxLength(200);
        b.Property(e => e.ReversalReason).HasMaxLength(200);
        b.HasIndex(e => new { e.Status, e.SubmittedAt });
    }
}

internal sealed class PaymentOrderItemConfiguration : IEntityTypeConfiguration<PaymentOrderItem>
{
    public void Configure(EntityTypeBuilder<PaymentOrderItem> b)
    {
        b.ToTable("payment_order_items", FinanceSchema.Name);
        b.Property(e => e.Amount).HasPrecision(18, 2);
        b.Property(e => e.ExpectedCharges).HasPrecision(18, 2);
        b.Property(e => e.ApprovedTotal).HasPrecision(18, 2);
        b.Property(e => e.PayeeSnapshot).HasColumnType("jsonb");
        b.HasIndex(e => e.PaymentOrderId);
        b.HasIndex(e => e.PayableId);
    }
}

internal sealed class PaymentOrderApprovalConfiguration : IEntityTypeConfiguration<PaymentOrderApproval>
{
    public void Configure(EntityTypeBuilder<PaymentOrderApproval> b)
    {
        b.ToTable("payment_order_approvals", FinanceSchema.Name);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Decision).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Comment).HasMaxLength(500);
        b.HasIndex(e => e.PaymentOrderId);
    }
}

internal sealed class PayableSettlementConfiguration : IEntityTypeConfiguration<PayableSettlement>
{
    public void Configure(EntityTypeBuilder<PayableSettlement> b)
    {
        b.ToTable("payable_settlements", FinanceSchema.Name);
        foreach (var money in new[] { nameof(PayableSettlement.Principal), nameof(PayableSettlement.Interest), nameof(PayableSettlement.Fine), nameof(PayableSettlement.Discount), nameof(PayableSettlement.Total) })
        {
            b.Property(money).HasPrecision(18, 2);
        }

        b.Property(e => e.BackdateReason).HasMaxLength(200);
        b.Property(e => e.ReversalReason).HasMaxLength(200);
        b.HasIndex(e => e.PayableId);
        b.HasIndex(e => e.PaymentOrderId);
    }
}

internal sealed class ApprovalRuleConfiguration : IEntityTypeConfiguration<ApprovalRule>
{
    public void Configure(EntityTypeBuilder<ApprovalRule> b)
    {
        b.ToTable("approval_rules", FinanceSchema.Name);
        b.Property(e => e.DocumentType).HasMaxLength(20);
        b.Property(e => e.MinAmount).HasPrecision(18, 2);
        b.Property(e => e.MaxAmount).HasPrecision(18, 2);
        b.Property(e => e.SeedKey).HasMaxLength(40);
        b.HasIndex(e => e.SeedKey).IsUnique().HasFilter("seed_key IS NOT NULL");
        b.HasIndex(e => new { e.DocumentType, e.Level });
    }
}

internal sealed class PaymentOrderLevelConfiguration : IEntityTypeConfiguration<PaymentOrderLevel>
{
    public void Configure(EntityTypeBuilder<PaymentOrderLevel> b)
    {
        b.ToTable("payment_order_levels", FinanceSchema.Name);
        b.Property(e => e.ReferenceAmount).HasPrecision(18, 2);
        b.Property(e => e.SplitWarning).HasMaxLength(200);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(12);
        b.HasOne<PaymentOrder>().WithMany().HasForeignKey(e => e.PaymentOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.PaymentOrderId, e.Level }).IsUnique();
    }
}
