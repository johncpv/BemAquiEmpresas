using BemAqui.Assets.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Assets.Infrastructure;

internal static class AssetsSchema
{
    public const string Name = "assets";
}

/// <summary>
/// <c>assets.assets</c> (PAT-01a): etiqueta única sem filtro (nunca reaproveitada, nem a do bem excluído); dinheiro em
/// <c>numeric(18,2)</c>, data de aquisição em <c>date</c>; índices dos filtros da lista (CA 9 e 11).
/// </summary>
internal sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> b)
    {
        b.ToTable("assets", AssetsSchema.Name);
        b.Property(e => e.Tag).HasMaxLength(20);
        b.Property(e => e.Name).HasMaxLength(AssetRules.NameMax);
        b.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Brand).HasMaxLength(AssetRules.TextMax);
        b.Property(e => e.Model).HasMaxLength(AssetRules.TextMax);
        b.Property(e => e.SerialNumber).HasMaxLength(AssetRules.TextMax);
        b.Property(e => e.AcquisitionValue).HasPrecision(18, 2);
        b.Property(e => e.InvoiceRef).HasMaxLength(AssetRules.InvoiceRefMax);
        b.Property(e => e.Notes).HasMaxLength(AssetRules.NotesMax);
        b.HasOne<AssetSite>().WithMany().HasForeignKey(e => e.SiteId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.Tag).IsUnique().HasDatabaseName("ix_assets_tag");
        b.HasIndex(e => e.SerialNumber).HasFilter("serial_number IS NOT NULL AND NOT is_deleted").HasDatabaseName("ix_assets_serial_number");
        b.HasIndex(e => e.WorkCenterId).HasFilter("NOT is_deleted").HasDatabaseName("ix_assets_work_center_id");
        b.HasIndex(e => e.ResponsibleEmployeeId).HasFilter("NOT is_deleted").HasDatabaseName("ix_assets_responsible_employee_id");
        b.HasIndex(e => e.SupplierPartyId).HasFilter("NOT is_deleted").HasDatabaseName("ix_assets_supplier_party_id");
        b.HasIndex(e => new { e.Status, e.Type }).HasFilter("NOT is_deleted").HasDatabaseName("ix_assets_status_type");
    }
}

/// <summary><c>assets.parameters</c> (PAT-01b): chave única, valor em texto (<c>50x25</c>).</summary>
internal sealed class AssetParameterConfiguration : IEntityTypeConfiguration<AssetParameter>
{
    public void Configure(EntityTypeBuilder<AssetParameter> b)
    {
        b.ToTable("parameters", AssetsSchema.Name);
        b.HasKey(e => e.Id).HasName("pk_assets_parameters");
        b.Property(e => e.Key).HasMaxLength(64);
        b.Property(e => e.Value).HasMaxLength(100);
        b.HasIndex(e => e.Key).IsUnique().HasDatabaseName("ix_assets_parameters_key");
    }
}

/// <summary><c>assets.sites</c> (ADR 0038 §2): nome único entre os não excluídos e <c>seed_key</c> única.</summary>
internal sealed class AssetSiteConfiguration : IEntityTypeConfiguration<AssetSite>
{
    public void Configure(EntityTypeBuilder<AssetSite> b)
    {
        b.ToTable("sites", AssetsSchema.Name);
        b.Property(e => e.Name).HasMaxLength(100);
        b.Property(e => e.SeedKey).HasMaxLength(50);
        b.HasIndex(e => e.Name).IsUnique().HasFilter("NOT is_deleted").HasDatabaseName("ix_sites_name");
        b.HasIndex(e => e.SeedKey).IsUnique().HasFilter("seed_key IS NOT NULL").HasDatabaseName("ix_sites_seed_key");
    }
}

/// <summary>
/// <c>assets.asset_movements</c> (PAT-02): movimento imutável do bem, com antes e depois; data de negócio em <c>date</c>, valor da venda em
/// <c>numeric(18,2)</c>; índice pelo bem e data para a aba Movimentações (do mais recente ao mais antigo).
/// </summary>
internal sealed class AssetMovementConfiguration : IEntityTypeConfiguration<AssetMovement>
{
    public void Configure(EntityTypeBuilder<AssetMovement> b)
    {
        b.ToTable("asset_movements", AssetsSchema.Name, t =>
        {
            t.HasCheckConstraint("ck_asset_movements_disposal_value", "disposal_value IS NULL OR disposal_value >= 0");
            t.HasCheckConstraint("ck_asset_movements_kind", "kind IN ('Transfer', 'Disposal', 'Reactivation')");
        });
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.DisposalReason).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Reason).HasMaxLength(AssetMovementRules.ReasonMax);
        b.Property(e => e.DisposalValue).HasPrecision(18, 2);
        b.HasOne<Asset>().WithMany().HasForeignKey(e => e.AssetId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.AssetId, e.MovedOn, e.CreatedAt }).HasDatabaseName("ix_asset_movements_asset_id_moved_on");
        b.HasIndex(e => e.FromEmployeeId).HasFilter("from_employee_id IS NOT NULL").HasDatabaseName("ix_asset_movements_from_employee_id");
        b.HasIndex(e => e.ToEmployeeId).HasFilter("to_employee_id IS NOT NULL").HasDatabaseName("ix_asset_movements_to_employee_id");
        b.HasIndex(e => e.BuyerPartyId).HasFilter("buyer_party_id IS NOT NULL").HasDatabaseName("ix_asset_movements_buyer_party_id");
    }
}
