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
