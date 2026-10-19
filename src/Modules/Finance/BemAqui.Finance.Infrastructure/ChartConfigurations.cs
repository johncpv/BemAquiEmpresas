using BemAqui.Finance.Application;
using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Finance.Infrastructure;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> b)
    {
        b.ToTable("chart_of_accounts", FinanceSchema.Name);
        b.Property(e => e.Code).HasMaxLength(10);
        b.Property(e => e.Name).HasMaxLength(100);
        b.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.DreGroup).HasConversion<string>().HasMaxLength(30);
        b.Property(e => e.SeedKey).HasMaxLength(50);
        b.HasOne<Account>().WithMany().HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.Restrict);
        // Código único entre os não excluídos (CA 8), também em gravação simultânea; seed uma vez por tenant, mesmo excluído (CA 2).
        b.HasIndex(e => e.Code).IsUnique().HasFilter("NOT is_deleted").HasDatabaseName(AccountService.CodeIndex);
        b.HasIndex(e => e.SeedKey).IsUnique().HasFilter("seed_key IS NOT NULL").HasDatabaseName("ix_chart_of_accounts_seed_key");
    }
}

internal sealed class CostCenterConfiguration : IEntityTypeConfiguration<CostCenter>
{
    public void Configure(EntityTypeBuilder<CostCenter> b)
    {
        b.ToTable("cost_centers", FinanceSchema.Name);
        b.Property(e => e.Code).HasMaxLength(10);
        b.Property(e => e.Name).HasMaxLength(100);
        b.Property(e => e.SeedKey).HasMaxLength(50);
        b.HasOne<CostCenter>().WithMany().HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.Code).IsUnique().HasFilter("NOT is_deleted").HasDatabaseName(CostCenterService.CodeIndex);
        b.HasIndex(e => e.SeedKey).IsUnique().HasFilter("seed_key IS NOT NULL").HasDatabaseName("ix_cost_centers_seed_key");
    }
}
