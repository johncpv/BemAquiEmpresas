using BemAqui.Pos.Application;
using BemAqui.Pos.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Pos.Infrastructure;

/// <summary><c>pos.stores</c> (PDV-01 CA 2): código único; local de estoque e tabela de preço são só ids (sem FK para outro módulo).</summary>
internal sealed class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> b)
    {
        b.ToTable("stores", PosSchema.Name);
        b.Property(e => e.Code).HasMaxLength(6);
        b.Property(e => e.Name).HasMaxLength(Store.MaxNameLength);
        b.Property(e => e.SeedKey).HasMaxLength(40);
        b.HasIndex(e => e.Code).IsUnique().HasDatabaseName(StoreService.StoreCodeIndex);
        b.HasIndex(e => e.SeedKey).IsUnique().HasFilter("seed_key IS NOT NULL");
    }
}

/// <summary>
/// <c>pos.registers</c> (PDV-01 CA 3 e 6): número único na loja, conta Caixa única entre os caixas e um aparelho ativo por caixa (índice único
/// parcial); <c>device_epoch</c> sobe a cada troca de aparelho.
/// </summary>
internal sealed class RegisterConfiguration : IEntityTypeConfiguration<Register>
{
    public void Configure(EntityTypeBuilder<Register> b)
    {
        b.ToTable("registers", PosSchema.Name, t =>
        {
            t.HasCheckConstraint("ck_registers_number", "number BETWEEN 1 AND 99");
            t.HasCheckConstraint("ck_registers_epoch", "device_epoch >= 0");
        });
        b.Property(e => e.UiMode).HasConversion<string>().HasMaxLength(16);
        b.HasOne<Store>().WithMany().HasForeignKey(e => e.StoreId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.StoreId, e.Number }).IsUnique().HasDatabaseName(StoreService.RegisterNumberIndex);
        b.HasIndex(e => e.CashAccountId).IsUnique().HasDatabaseName(StoreService.RegisterCashAccountIndex);
        b.HasIndex(e => e.ActiveDeviceId).IsUnique().HasFilter("active_device_id IS NOT NULL");
    }
}
