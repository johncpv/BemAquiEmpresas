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
        b.ToTable("stores", PosSchema.Name, t =>
        {
            t.HasCheckConstraint("ck_stores_offline_window", "offline_window_hours BETWEEN 1 AND 168");
            t.HasCheckConstraint("ck_stores_parameters", "cash_break_tolerance >= 0 AND exchange_days >= 0 AND cash_drop_alert_amount >= 0");
        });
        // OFF-01 (PP-10 e PP-22): parâmetros da loja com os padrões do PO; as lojas que já existiam recebem os padrões na migration.
        b.Property(e => e.OfflineWindowHours).HasDefaultValue(Store.DefaultOfflineWindowHours);
        b.Property(e => e.CashBreakTolerance).HasPrecision(18, 2).HasDefaultValue(5.00m);
        b.Property(e => e.ExchangeDays).HasDefaultValue(30);
        b.Property(e => e.CashDropAlertAmount).HasPrecision(18, 2).HasDefaultValue(1000.00m);
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

/// <summary>
/// <c>pos.catalog_items</c> (OFF-01): chave (loja, coleção, entidade); o delta lê pelo índice (loja, <c>change_seq</c>). <c>change_seq</c> vem da
/// sequência <c>pos.catalog_change_seq</c>; o <c>payload</c> é jsonb. Sem FK para a loja de propósito: é modelo de leitura, refeito pela carga.
/// </summary>
internal sealed class CatalogItemConfiguration : IEntityTypeConfiguration<CatalogItem>
{
    public void Configure(EntityTypeBuilder<CatalogItem> b)
    {
        b.ToTable("catalog_items", PosSchema.Name);
        b.HasKey(e => new { e.StoreId, e.Kind, e.EntityId });
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Payload).HasColumnType("jsonb");
        b.Property(e => e.ChangeSeq).HasDefaultValueSql("nextval('pos.catalog_change_seq')");
        b.HasIndex(e => new { e.StoreId, e.ChangeSeq }).HasDatabaseName("ix_catalog_items_store_change_seq");
    }
}

/// <summary><c>pos.catalog_store_builds</c> (OFF-01): a carga inicial de cada loja e a tabela de preço para a qual foi feita.</summary>
internal sealed class CatalogStoreBuildConfiguration : IEntityTypeConfiguration<CatalogStoreBuild>
{
    public void Configure(EntityTypeBuilder<CatalogStoreBuild> b)
    {
        b.ToTable("catalog_store_builds", PosSchema.Name);
        b.HasKey(e => e.StoreId);
    }
}
