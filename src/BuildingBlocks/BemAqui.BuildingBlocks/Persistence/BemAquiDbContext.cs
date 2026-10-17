using System.Linq.Expressions;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.BuildingBlocks.Persistence;

/// <summary>
/// Base do DbContext de tenant: tabelas compartilhadas (<c>shared</c>, <c>audit</c>) e filtro global de
/// exclusão lógica. O <c>TenantDbContext</c> do Host deriva daqui e aplica as configurações dos módulos (ADR 0006).
/// </summary>
public abstract class BemAquiDbContext(DbContextOptions options) : DbContext(options)
{
    public const string SharedSchema = "shared";
    public const string AuditSchema = "audit";

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<AuditEntry>(b =>
        {
            b.ToTable("audit_log", AuditSchema);
            b.HasKey(e => e.Id);
            b.Property(e => e.Entity).HasMaxLength(128);
            b.Property(e => e.EntityId).HasMaxLength(64);
            b.Property(e => e.Action).HasMaxLength(16);
            b.Property(e => e.Changes).HasColumnType("jsonb");
            b.Property(e => e.Ip).HasMaxLength(64);
            b.HasIndex(e => new { e.Entity, e.EntityId, e.At });
        });

        modelBuilder.Entity<NumberSequence>(b =>
        {
            b.ToTable("number_sequences", SharedSchema);
            b.HasKey(e => e.Id);
            b.Property(e => e.DocType).HasMaxLength(16);
            b.HasIndex(e => new { e.DocType, e.Year }).IsUnique();
        });

        ConfigureModules(modelBuilder);
        ApplySoftDeleteFilter(modelBuilder);
    }

    /// <summary>Configurações dos módulos (assemblies <c>*.Infrastructure</c>).</summary>
    protected virtual void ConfigureModules(ModelBuilder modelBuilder)
    {
    }

    private static void ApplySoftDeleteFilter(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(t => typeof(ISoftDeletable).IsAssignableFrom(t.ClrType)))
        {
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var filter = Expression.Lambda(Expression.Not(Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted))), parameter);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }
}
