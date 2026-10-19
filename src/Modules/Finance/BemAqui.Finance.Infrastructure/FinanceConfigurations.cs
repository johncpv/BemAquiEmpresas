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
        // Uma conta padrão por vez (CA 6); a excluída deixa de ser padrão.
        b.HasIndex(e => e.IsDefault).IsUnique().HasFilter("is_default").HasDatabaseName(BankAccountService.DefaultIndex);
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
