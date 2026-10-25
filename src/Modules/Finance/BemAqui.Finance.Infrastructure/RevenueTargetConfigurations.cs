using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Finance.Infrastructure;

/// <summary>Metas de faturamento (DSH-01a CA 9, ADR 0028 §1): uma por ano e mês, <c>numeric(18,2)</c> e ≥ 0.</summary>
internal sealed class RevenueTargetConfiguration : IEntityTypeConfiguration<RevenueTarget>
{
    public void Configure(EntityTypeBuilder<RevenueTarget> b)
    {
        b.ToTable("revenue_targets", FinanceSchema.Name, t =>
        {
            t.HasCheckConstraint("ck_revenue_targets_month", "month BETWEEN 1 AND 12");
            t.HasCheckConstraint("ck_revenue_targets_amount", "amount >= 0");
        });
        b.Property(e => e.Amount).HasPrecision(18, 2);
        b.HasIndex(e => new { e.Year, e.Month }).IsUnique();
    }
}
