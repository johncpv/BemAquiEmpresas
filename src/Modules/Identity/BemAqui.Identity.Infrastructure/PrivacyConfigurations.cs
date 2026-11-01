using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Identity.Infrastructure;

/// <summary>Pedido do titular (<c>settings.privacy_requests</c>, LGPD-01a): número único, enums em texto e o hash do link de download.</summary>
internal sealed class PrivacyRequestConfiguration : IEntityTypeConfiguration<PrivacyRequest>
{
    public void Configure(EntityTypeBuilder<PrivacyRequest> b)
    {
        b.ToTable("privacy_requests", "settings");
        b.Property(e => e.Number).HasMaxLength(30);
        b.HasIndex(e => e.Number).IsUnique();
        b.Property(e => e.SubjectType).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.SubjectName).HasMaxLength(150);
        b.Property(e => e.SubjectContact).HasMaxLength(200);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(32);
        b.Property(e => e.Channel).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Notes).HasMaxLength(PrivacyRequest.NotesMaxLength);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.IdentityMethod).HasConversion<string>().HasMaxLength(32);
        b.Property(e => e.IdentityNotes).HasMaxLength(PrivacyRequest.NotesMaxLength);
        b.Property(e => e.Response).HasMaxLength(PrivacyRequest.NotesMaxLength);
        b.Property(e => e.RefusalReason).HasMaxLength(PrivacyRequest.NotesMaxLength);
        b.Property(e => e.CorrectionLink).HasMaxLength(200);
        b.Property(e => e.ExportStatus).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.ExportError).HasMaxLength(200);
        b.Property(e => e.DownloadTokenHash).HasMaxLength(64);
        b.HasIndex(e => e.DownloadTokenHash).IsUnique().HasFilter("download_token_hash IS NOT NULL");
        b.HasIndex(e => new { e.Status, e.DueOn });
        b.Ignore(e => e.IsIdentityVerified);
        // LGPD-01b: as etapas e o resumo da anonimização, sem dado pessoal.
        b.Property(e => e.AnonymizationStatus).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.AnonymizationSteps).HasColumnType("jsonb");
        b.Property(e => e.AnonymizationSummary).HasColumnType("jsonb");
    }
}

/// <summary>Tabela de retenção (<c>settings.retention_rules</c>, LGPD-01b CA 1): uma linha por tipo de titular e grupo de campos.</summary>
internal sealed class RetentionRuleConfiguration : IEntityTypeConfiguration<RetentionRule>
{
    public void Configure(EntityTypeBuilder<RetentionRule> b)
    {
        b.ToTable("retention_rules", "settings");
        b.Property(e => e.SubjectType).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.FieldGroup).HasMaxLength(32);
        b.Property(e => e.Rule).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.BaseDate).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.SeedKey).HasMaxLength(64);
        b.HasIndex(e => e.SeedKey).IsUnique();
        b.HasIndex(e => new { e.SubjectType, e.FieldGroup }).IsUnique();
    }
}
