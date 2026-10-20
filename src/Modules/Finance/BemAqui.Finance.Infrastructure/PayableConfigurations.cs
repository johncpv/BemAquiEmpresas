using BemAqui.Finance.Application;
using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Finance.Infrastructure;

internal sealed class PayableConfiguration : IEntityTypeConfiguration<Payable>
{
    public void Configure(EntityTypeBuilder<Payable> b)
    {
        b.ToTable("payables", FinanceSchema.Name);
        b.Property(e => e.Number).HasMaxLength(20);
        b.Property(e => e.Description).HasMaxLength(200);
        foreach (var money in new[] { nameof(Payable.Amount), nameof(Payable.RetentionIss), nameof(Payable.RetentionInss), nameof(Payable.RetentionIr), nameof(Payable.RetentionPcc), nameof(Payable.PaidAmount) })
        {
            b.Property(money).HasPrecision(18, 2);
        }

        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Origin).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.CostType).HasConversion<string>().HasMaxLength(16);
        b.HasIndex(e => e.ProjectId);
        b.Property(e => e.PaymentMethod).HasConversion<string>().HasMaxLength(10);
        b.Property(e => e.PaymentDetails).HasColumnType("jsonb");
        b.Property(e => e.CancelReason).HasMaxLength(200);
        b.Property(e => e.OpeningReference).HasMaxLength(40);
        b.Ignore(e => e.Retentions);
        b.Ignore(e => e.NetAmount);
        b.Ignore(e => e.Balance);
        b.Ignore(e => e.InPaymentOrder);
        b.Ignore(e => e.IsOpen);
        b.HasIndex(e => e.Number).IsUnique().HasDatabaseName(PayableService.NumberIndex);
        b.HasIndex(e => new { e.Status, e.DueDate }).HasFilter("status NOT IN ('Paid','Cancelled')").HasDatabaseName("ix_payables_open_due");
        b.HasIndex(e => e.PartyId);
        b.HasIndex(e => e.InstallmentGroupId);
        // Um título por origem (CA 14) e um por competência da recorrente (CA 17), mesmo em paralelo.
        b.HasIndex(e => new { e.Origin, e.OriginId }).IsUnique().HasFilter("origin_id IS NOT NULL").HasDatabaseName(PayableService.OriginIndex);
        b.HasIndex(e => new { e.RecurringExpenseId, e.Competence }).IsUnique().HasFilter("recurring_expense_id IS NOT NULL").HasDatabaseName(PayableService.CompetenceIndex);
    }
}

internal sealed class RecurringExpenseConfiguration : IEntityTypeConfiguration<RecurringExpense>
{
    public void Configure(EntityTypeBuilder<RecurringExpense> b)
    {
        b.ToTable("recurring_expenses", FinanceSchema.Name);
        b.Property(e => e.Description).HasMaxLength(200);
        b.Property(e => e.Amount).HasPrecision(18, 2);
        b.Property(e => e.PaymentMethod).HasConversion<string>().HasMaxLength(10);
        b.Property(e => e.PaymentDetails).HasColumnType("jsonb");
    }
}
