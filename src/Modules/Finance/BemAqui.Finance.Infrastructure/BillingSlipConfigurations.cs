using BemAqui.Finance.Application;
using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Finance.Infrastructure;

/// <summary>
/// Boletos (<c>finance.billing_slips</c>, BNK-01b): valores em <c>numeric(18,2)</c>, percentuais com 2 casas, o retorno do banco em JSONB
/// (sem pagador), índice único parcial de boleto ativo por título (CA 22) e "seu número" único por conta (CA 6).
/// </summary>
internal sealed class BillingSlipConfiguration : IEntityTypeConfiguration<BillingSlip>
{
    public void Configure(EntityTypeBuilder<BillingSlip> b)
    {
        b.ToTable("billing_slips", FinanceSchema.Name);
        b.Property(e => e.Provider).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(24);
        b.Property(e => e.CancelReason).HasConversion<string>().HasMaxLength(24);
        b.Property(e => e.OurReference).HasMaxLength(BillingReference.MaxLength);
        b.Property(e => e.OurNumber).HasMaxLength(40);
        b.Property(e => e.ProviderRequestId).HasMaxLength(64);
        b.Property(e => e.Barcode).HasMaxLength(60);
        b.Property(e => e.DigitableLine).HasMaxLength(60);
        b.Property(e => e.PixCopyPaste).HasMaxLength(500);
        b.Property(e => e.QrCode).HasMaxLength(500);
        b.Property(e => e.Amount).HasPrecision(18, 2);
        b.Property(e => e.DiscountAmount).HasPrecision(18, 2);
        b.Property(e => e.PaidAmount).HasPrecision(18, 2);
        b.Property(e => e.FinePct).HasPrecision(5, 2);
        b.Property(e => e.InterestMonthPct).HasPrecision(5, 2);
        b.Property(e => e.Message).HasMaxLength(250);
        b.Property(e => e.FailureReason).HasMaxLength(300);
        b.Property(e => e.FailureAction).HasMaxLength(24);
        b.Property(e => e.CancelFailureReason).HasMaxLength(300);
        b.Property(e => e.ProviderPayload).HasColumnType("jsonb");
        b.Property(e => e.ScheduledEmailTo).HasMaxLength(254);
        b.Property(e => e.ScheduledEmailCc).HasMaxLength(800);
        b.Property(e => e.ReviewReason).HasMaxLength(300);
        b.Property(e => e.ReviewDecision).HasMaxLength(16);
        b.Ignore(e => e.IsActive);
        b.Ignore(e => e.HoldsReceivable);
        b.Ignore(e => e.CancelPending);
        b.Ignore(e => e.MayExistAtBank);
        b.HasOne<Receivable>().WithMany().HasForeignKey(e => e.ReceivableId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BankAccount>().WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.ReceivableId).IsUnique().HasFilter("status IN ('Requested','Issued')").HasDatabaseName(BillingSlipService.ActiveIndex);
        b.HasIndex(e => new { e.BankAccountId, e.OurReference }).IsUnique().HasDatabaseName(BillingSlipService.ReferenceIndex);
        b.HasIndex(e => e.ClientRequestId).IsUnique().HasFilter("client_request_id IS NOT NULL").HasDatabaseName(BillingSlipService.RequestIndex);
        b.HasIndex(e => e.Status);
    }
}

/// <summary>Sequência do "seu número" por conta (<c>finance.billing_reference_sequences</c>), fora da auditoria.</summary>
internal sealed class BillingReferenceSequenceConfiguration : IEntityTypeConfiguration<BillingReferenceSequence>
{
    public void Configure(EntityTypeBuilder<BillingReferenceSequence> b)
    {
        b.ToTable("billing_reference_sequences", FinanceSchema.Name);
        b.HasKey(e => e.BankAccountId);
        b.Property(e => e.BankAccountId).ValueGeneratedNever();
        b.HasOne<BankAccount>().WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
