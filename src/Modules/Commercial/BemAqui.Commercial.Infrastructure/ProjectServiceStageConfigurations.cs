using BemAqui.Commercial.Application;
using BemAqui.Commercial.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Commercial.Infrastructure;

/// <summary>
/// Etapas do projeto de serviço (EXE-03a). A letra e a sequência são únicas no projeto por constraints DEFERRABLE criadas na migration (reordenar
/// ou trocar duas letras num salvar só não falha no meio); o serviço também valida.
/// </summary>
internal sealed class ProjectServiceStageConfiguration : IEntityTypeConfiguration<ProjectServiceStage>
{
    public void Configure(EntityTypeBuilder<ProjectServiceStage> b)
    {
        b.ToTable("project_stages", CommercialSchema.Name);
        b.Property(e => e.Code).HasMaxLength(1).IsFixedLength();
        b.Property(e => e.Name).HasMaxLength(StageTemplateRules.MaxStepName);
        b.Property(e => e.Role).HasConversion<string>().HasMaxLength(12);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.PlannedHours).HasPrecision(18, 4);
        b.Property(e => e.CompletionVersion).HasDefaultValue(0);
        b.HasOne<Project>().WithMany().HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.ProjectId);
        b.HasIndex(e => e.ResponsibleUserId);
        b.Ignore(e => e.IsStarted);
    }
}

internal sealed class ProjectServiceStageCheckConfiguration : IEntityTypeConfiguration<ProjectServiceStageCheck>
{
    public void Configure(EntityTypeBuilder<ProjectServiceStageCheck> b)
    {
        b.ToTable("project_stage_checklist", CommercialSchema.Name);
        b.Property(e => e.Text).HasMaxLength(StageTemplateRules.MaxChecklistText);
        b.Property(e => e.Result).HasConversion<string>().HasMaxLength(16);
        b.HasOne<ProjectServiceStage>().WithMany().HasForeignKey(e => e.StageId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.StageId, e.Sequence });
    }
}
