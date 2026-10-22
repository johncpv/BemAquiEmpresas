using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Production.Infrastructure;

/// <summary>
/// Apontamento (PRD-03a): um aberto por funcionário (índice único parcial, CA 10 e 30), por etapa e por OS. Sem colunas de custo
/// (PRD-04).
/// </summary>
internal sealed class LaborEntryConfiguration : IEntityTypeConfiguration<LaborEntry>
{
    public void Configure(EntityTypeBuilder<LaborEntry> b)
    {
        b.ToTable("labor_entries", ProductionSchema.Name, t =>
        {
            t.HasCheckConstraint("ck_labor_entries_interval", "ended_at IS NULL OR ended_at >= started_at");
            t.HasCheckConstraint("ck_labor_entries_paused", "paused_minutes >= 0");
        });
        b.Property(e => e.CloseKind).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.CorrectionReason).HasMaxLength(200);
        b.Property(e => e.Notes).HasMaxLength(500);
        b.HasOne<ProductionOrder>().WithMany().HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProductionOrderStep>().WithMany().HasForeignKey(e => e.OrderStepId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.EmployeeId).IsUnique().HasDatabaseName("ux_labor_entries_open_employee").HasFilter("ended_at IS NULL");
        b.HasIndex(e => new { e.EmployeeId, e.StartedAt });
        b.HasIndex(e => e.OrderId);
        b.HasIndex(e => e.OrderStepId);
    }
}

internal sealed class StepPauseConfiguration : IEntityTypeConfiguration<StepPause>
{
    public void Configure(EntityTypeBuilder<StepPause> b)
    {
        b.ToTable("step_pauses", ProductionSchema.Name, t => t.HasCheckConstraint("ck_step_pauses_interval", "ended_at IS NULL OR ended_at >= started_at"));
        b.Property(e => e.Reason).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Notes).HasMaxLength(120);
        b.HasOne<LaborEntry>().WithMany().HasForeignKey(e => e.LaborEntryId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.LaborEntryId).IsUnique().HasDatabaseName("ux_step_pauses_open_entry").HasFilter("ended_at IS NULL");
        b.HasIndex(e => e.OrderId);
    }
}