using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Production.Infrastructure;

/// <summary>
/// Apontamento (PRD-03a): um aberto por funcionário (índice único parcial, CA 10 e 30), por etapa e por OS. Custo da PRD-04: snapshot
/// <c>numeric(18,6)</c>, valor <c>numeric(18,2)</c>, versão e as marcas "Sem custo-hora" e "Calculado na implantação".
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
        b.Property(e => e.HourlyCostSnapshot).HasPrecision(18, 6);
        b.Property(e => e.Cost).HasPrecision(18, 2);
        b.HasOne<ProductionOrder>().WithMany().HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ProductionOrderStep>().WithMany().HasForeignKey(e => e.OrderStepId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.EmployeeId).IsUnique().HasDatabaseName("ux_labor_entries_open_employee").HasFilter("ended_at IS NULL");
        b.HasIndex(e => new { e.EmployeeId, e.StartedAt });
        b.HasIndex(e => e.OrderId);
        b.HasIndex(e => e.OrderStepId);
        b.HasIndex(e => e.CostPositionId).HasDatabaseName("ix_labor_entries_cost_missing").HasFilter("cost_missing");
    }
}

/// <summary>Histórico do custo do apontamento (PRD-04 CA 17): uma linha por versão (único por apontamento e versão).</summary>
internal sealed class LaborCostRecordConfiguration : IEntityTypeConfiguration<LaborCostRecord>
{
    public void Configure(EntityTypeBuilder<LaborCostRecord> b)
    {
        b.ToTable("labor_cost_history", ProductionSchema.Name, t => t.HasCheckConstraint("ck_labor_cost_history_version", "cost_version >= 1"));
        b.Property(e => e.Origin).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.HourlyCostSnapshot).HasPrecision(18, 6);
        b.Property(e => e.PreviousAmount).HasPrecision(18, 2);
        b.Property(e => e.Amount).HasPrecision(18, 2);
        b.HasOne<LaborEntry>().WithMany().HasForeignKey(e => e.LaborEntryId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.LaborEntryId, e.CostVersion }).IsUnique();
        b.HasIndex(e => e.OrderId);
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