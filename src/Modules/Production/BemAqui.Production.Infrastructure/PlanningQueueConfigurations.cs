using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Production.Infrastructure;

/// <summary>
/// Fila de prioridade (PLN-02, ADR 0029 §3): um projeto por linha (<c>project_id</c> único, só id, sem FK: ADR 0006), posição
/// <c>CHECK &gt; 0</c> (contígua pelo código; sem índice único na posição, que a renumeração em lote violaria no meio).
/// </summary>
internal sealed class PlanningQueueEntryConfiguration : IEntityTypeConfiguration<PlanningQueueEntry>
{
    public void Configure(EntityTypeBuilder<PlanningQueueEntry> b)
    {
        b.ToTable("planning_queue", ProductionSchema.Name, t => t.HasCheckConstraint("ck_planning_queue_position", "position > 0"));
        b.HasIndex(e => e.ProjectId).IsUnique();
        b.HasIndex(e => e.Position);
    }
}

/// <summary>Versão da fila (<c>xmin</c>, IConcurrencyVersioned), quem e quando reordenou.</summary>
internal sealed class PlanningQueueStateConfiguration : IEntityTypeConfiguration<PlanningQueueState>
{
    public void Configure(EntityTypeBuilder<PlanningQueueState> b) => b.ToTable("planning_queue_state", ProductionSchema.Name);
}

/// <summary>Histórico da fila (CA 8): tipo em texto, motivo até 200, posições em <c>jsonb</c> (só exibidas), índice pela data.</summary>
internal sealed class PlanningQueueChangeConfiguration : IEntityTypeConfiguration<PlanningQueueChange>
{
    public void Configure(EntityTypeBuilder<PlanningQueueChange> b)
    {
        b.ToTable("planning_queue_changes", ProductionSchema.Name);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Reason).HasMaxLength(200);
        b.Property(e => e.Moves).HasColumnType("jsonb");
        b.HasIndex(e => e.At);
    }
}
