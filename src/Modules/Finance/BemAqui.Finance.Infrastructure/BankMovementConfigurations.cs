using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Finance.Infrastructure;

internal sealed class BankTransferConfiguration : IEntityTypeConfiguration<BankTransfer>
{
    public void Configure(EntityTypeBuilder<BankTransfer> b)
    {
        b.ToTable("bank_transfers", FinanceSchema.Name);
        b.Property(e => e.Amount).HasPrecision(18, 2);
        b.Property(e => e.Description).HasMaxLength(200);
        b.Property(e => e.BackdateReason).HasMaxLength(200);
        b.Property(e => e.ReversalReason).HasMaxLength(200);
        b.HasIndex(e => e.FromAccountId);
        b.HasIndex(e => e.ToAccountId);
    }
}

internal sealed class BankEntryConfiguration : IEntityTypeConfiguration<BankEntry>
{
    public void Configure(EntityTypeBuilder<BankEntry> b)
    {
        b.ToTable("bank_entries", FinanceSchema.Name);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Amount).HasPrecision(18, 2);
        b.Property(e => e.Reason).HasMaxLength(200);
        b.Property(e => e.BackdateReason).HasMaxLength(200);
        b.Property(e => e.ReversalReason).HasMaxLength(200);
        b.HasIndex(e => e.BankAccountId);
    }
}
