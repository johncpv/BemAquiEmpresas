using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Production.Infrastructure;

/// <summary>
/// Não conformidade (PRD-05): número único, uma por etapa de qualidade e tentativa (a reprovação concorrente não duplica, CA 14),
/// textos com os limites do CA 6 e 9, custo informativo <c>numeric(18,2)</c> e as listas por OS, responsável e situação.
/// </summary>
internal sealed class NonConformityConfiguration : IEntityTypeConfiguration<NonConformity>
{
    public void Configure(EntityTypeBuilder<NonConformity> b)
    {
        b.ToTable("non_conformities", ProductionSchema.Name, t => t.HasCheckConstraint("ck_non_conformities_attempt", "attempt >= 1"));
        b.Property(e => e.Number).HasMaxLength(20);
        b.Property(e => e.Description).HasMaxLength(1000);
        b.Property(e => e.Cause).HasMaxLength(500);
        b.Property(e => e.Action).HasMaxLength(500);
        b.Property(e => e.Result).HasMaxLength(500);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Cost).HasPrecision(18, 2);
        b.HasOne<ProductionOrder>().WithMany().HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProductionOrderStep>().WithMany().HasForeignKey(e => e.OrderStepId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProductionOrderStep>().WithMany().HasForeignKey(e => e.ReturnToStepId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.Number).IsUnique();
        b.HasIndex(e => new { e.OrderStepId, e.Attempt }).IsUnique();
        b.HasIndex(e => e.OrderId);
        b.HasIndex(e => new { e.ResponsibleUserId, e.Status });
        b.HasIndex(e => e.Status);
    }
}

/// <summary>Decisão de qualidade por tentativa (PRD-05 CA 7): uma por etapa de qualidade e tentativa.</summary>
internal sealed class QualityDecisionConfiguration : IEntityTypeConfiguration<QualityDecision>
{
    public void Configure(EntityTypeBuilder<QualityDecision> b)
    {
        b.ToTable("quality_decisions", ProductionSchema.Name);
        b.Property(e => e.Result).HasConversion<string>().HasMaxLength(16);
        b.HasOne<ProductionOrder>().WithMany().HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProductionOrderStep>().WithMany().HasForeignKey(e => e.StepId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<NonConformity>().WithMany().HasForeignKey(e => e.NonConformityId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.StepId, e.Attempt }).IsUnique();
        b.HasIndex(e => e.OrderId);
        b.HasIndex(e => e.NonConformityId);
    }
}