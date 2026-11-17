using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Finance.Infrastructure;

/// <summary>Forma de pagamento (PAG-01): conta de destino com FK no próprio schema; a adquirente é só o id do terceiro (ADR 0006).</summary>
internal sealed class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> b)
    {
        b.ToTable("payment_methods", FinanceSchema.Name);
        b.Property(e => e.Name).HasMaxLength(100);
        b.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.NfePaymentCode).HasMaxLength(2);
        b.Property(e => e.SeedKey).HasMaxLength(50);
        b.HasOne<BankAccount>().WithMany().HasForeignKey(e => e.DestinationAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.AcquirerPartyId);
        b.HasIndex(e => e.SeedKey).IsUnique().HasFilter("seed_key IS NOT NULL").HasDatabaseName("ix_payment_methods_seed_key");
    }
}

/// <summary>Taxa da adquirente por vigência (PAG-01 CA 2 e 3): taxa <c>numeric(9,4)</c>; um início de vigência por forma, adquirente, bandeira e parcelas.</summary>
internal sealed class AcquirerFeeConfiguration : IEntityTypeConfiguration<AcquirerFee>
{
    public void Configure(EntityTypeBuilder<AcquirerFee> b)
    {
        b.ToTable("acquirer_fees", FinanceSchema.Name);
        b.Property(e => e.Brand).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.FeePercent).HasPrecision(9, 4);
        b.HasOne<PaymentMethod>().WithMany().HasForeignKey(e => e.PaymentMethodId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.PaymentMethodId, e.AcquirerPartyId, e.Brand, e.Installments, e.ValidFrom }).IsUnique();
    }
}
