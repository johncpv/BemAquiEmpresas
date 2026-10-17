using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.BuildingBlocks.Security;

/// <summary>
/// Key ring do Data Protection no banco de catálogo, schema <c>security</c> (ADR 0007).
/// Cada chave é gravada já cifrada pelo certificado de proteção: o banco sozinho não decifra nada.
/// </summary>
public sealed class DataProtectionKeysDbContext(DbContextOptions<DataProtectionKeysDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public const string Schema = "security";

    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<DataProtectionKey>().ToTable("data_protection_keys");
    }
}
