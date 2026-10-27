using System.Text.Json;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Finance.Infrastructure;

internal static class FinanceSchema
{
    public const string Name = "finance";
}

internal sealed class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> b)
    {
        b.ToTable("bank_accounts", FinanceSchema.Name);
        b.Property(e => e.Name).HasMaxLength(100);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(10);
        b.Property(e => e.BankCode).HasMaxLength(3);
        b.Property(e => e.Agency).HasMaxLength(10);
        b.Property(e => e.Account).HasMaxLength(20);
        b.Property(e => e.OpeningBalance).HasPrecision(18, 2);
        b.Property(e => e.ImportSource).HasMaxLength(100);
        // BNK-01a: banco ligado por API; as contas que já existem entram como None.
        b.Property(e => e.Provider).HasConversion<string>().HasMaxLength(20).HasDefaultValue(BankProvider.None).HasSentinel(BankProvider.None);
        // Uma conta padrão por vez (CA 6); a excluída deixa de ser padrão.
        b.HasIndex(e => e.IsDefault).IsUnique().HasFilter("is_default").HasDatabaseName(BankAccountService.DefaultIndex);
    }
}

/// <summary>
/// Integração bancária por conta (BNK-01a, ADR 0032 §1): uma por conta (<c>bank_account_id</c> único). As colunas <c>*_cipher</c>
/// guardam só o ciphertext do Data Protection.
/// </summary>
internal sealed class BankIntegrationConfiguration : IEntityTypeConfiguration<BankIntegration>
{
    public void Configure(EntityTypeBuilder<BankIntegration> b)
    {
        b.ToTable("bank_integrations", FinanceSchema.Name);
        b.HasIndex(e => e.BankAccountId).IsUnique();
        b.HasOne<BankAccount>().WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        b.Property(e => e.Provider).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Environment).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.CertificateFormat).HasConversion<string>().HasMaxLength(10);
        b.Property(e => e.LastTestOutcome).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.ClientIdHint).HasMaxLength(8);
        b.Property(e => e.CertificateThumbprint).HasMaxLength(64);
        b.Property(e => e.CertificateSubject).HasMaxLength(300);
        b.Property(e => e.AccountNumber).HasMaxLength(20);
        b.Property(e => e.GrantedScopes).HasMaxLength(500);
        b.Property(e => e.LastTestMessage).HasMaxLength(300);
        b.Ignore(e => e.IsActive);
    }
}

internal sealed class PaymentTermConfiguration : IEntityTypeConfiguration<PaymentTerm>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<PaymentTerm> b)
    {
        b.ToTable("payment_terms", FinanceSchema.Name);
        b.Property(e => e.Name).HasMaxLength(100);
        b.Property(e => e.SeedKey).HasMaxLength(50);
        b.Property(e => e.ImportSource).HasMaxLength(100);
        // Parcelas em jsonb: [{ "days": 30, "percent": 33.3333 }, ...] (dias e percentual numeric(7,4)).
        b.Property(e => e.Installments).HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, Json),
            s => JsonSerializer.Deserialize<List<Installment>>(s, Json) ?? new List<Installment>(),
            new ValueComparer<List<Installment>>((x, y) => x!.SequenceEqual(y!), v => v.Aggregate(0, (h, i) => HashCode.Combine(h, i.GetHashCode())), v => v.ToList()));
        b.HasIndex(e => e.SeedKey).IsUnique().HasFilter("seed_key IS NOT NULL").HasDatabaseName("ix_payment_terms_seed_key");
    }
}
