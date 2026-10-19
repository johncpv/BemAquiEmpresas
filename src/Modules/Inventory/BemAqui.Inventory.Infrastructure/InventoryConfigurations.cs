using BemAqui.Inventory.Application;
using BemAqui.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Inventory.Infrastructure;

internal static class InventorySchema
{
    public const string Name = "inventory";
}

internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> b)
    {
        b.ToTable("locations", InventorySchema.Name);
        b.Property(e => e.Code).HasMaxLength(20);
        b.Property(e => e.Name).HasMaxLength(100);
        b.Property(e => e.SeedKey).HasMaxLength(50);
        b.Property(e => e.ImportSource).HasMaxLength(100);
        // Código único entre os não excluídos (CA 7); um só padrão (CA 4); seed uma vez por tenant, mesmo excluído (CA 3).
        b.HasIndex(e => e.Code).IsUnique().HasFilter("NOT is_deleted").HasDatabaseName(LocationService.CodeIndex);
        b.HasIndex(e => e.IsDefault).IsUnique().HasFilter("is_default").HasDatabaseName(LocationService.DefaultIndex);
        b.HasIndex(e => e.SeedKey).IsUnique().HasFilter("seed_key IS NOT NULL").HasDatabaseName("ix_locations_seed_key");
    }
}
