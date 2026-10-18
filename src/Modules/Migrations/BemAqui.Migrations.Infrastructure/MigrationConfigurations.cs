using BemAqui.Migrations.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Migrations.Infrastructure;

internal static class MigrationSchema
{
    public const string Name = "migration";
}

internal sealed class ImportRunConfiguration : IEntityTypeConfiguration<ImportRun>
{
    public void Configure(EntityTypeBuilder<ImportRun> b)
    {
        b.ToTable("import_runs", MigrationSchema.Name);
        b.Property(e => e.Mode).HasConversion<string>().HasMaxLength(10);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(e => e.Entities).HasMaxLength(500);
        b.Property(e => e.Operator).HasMaxLength(254);
        b.Property(e => e.Summary).HasColumnType("jsonb");
        b.Property(e => e.Failure).HasMaxLength(2000);
        b.HasIndex(e => e.StartedAt);
    }
}

internal sealed class ImportItemConfiguration : IEntityTypeConfiguration<ImportItem>
{
    public void Configure(EntityTypeBuilder<ImportItem> b)
    {
        b.ToTable("import_items", MigrationSchema.Name);
        b.Property(e => e.Entity).HasMaxLength(50);
        b.Property(e => e.SourceTable).HasMaxLength(64);
        b.Property(e => e.SourceId).HasMaxLength(100);
        b.Property(e => e.Payload).HasColumnType("jsonb");
        b.Property(e => e.Hash).HasMaxLength(64);
        b.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(10);
        b.Property(e => e.TargetType).HasMaxLength(50);
        b.Property(e => e.Message).HasMaxLength(1000);
        b.Property(e => e.Warning).HasMaxLength(1000);
        b.Property(e => e.Differences).HasColumnType("jsonb");
        b.HasOne<ImportRun>().WithMany().HasForeignKey(e => e.RunId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(e => new { e.RunId, e.Outcome });
    }
}

internal sealed class ExternalRefConfiguration : IEntityTypeConfiguration<ExternalRef>
{
    public void Configure(EntityTypeBuilder<ExternalRef> b)
    {
        b.ToTable("external_refs", MigrationSchema.Name);
        b.Property(e => e.Source).HasMaxLength(20);
        b.Property(e => e.SourceTable).HasMaxLength(64);
        b.Property(e => e.SourceId).HasMaxLength(100);
        b.Property(e => e.TargetType).HasMaxLength(50);
        b.Property(e => e.SourceHash).HasMaxLength(64);
        // Uma chave por linha de origem (CA 10): é o que impede a duplicação na reexecução.
        b.HasIndex(e => new { e.Source, e.SourceTable, e.SourceId }).IsUnique().HasDatabaseName("ix_external_refs_source");
        b.HasIndex(e => new { e.TargetType, e.TargetId }).HasDatabaseName("ix_external_refs_target");
    }
}
