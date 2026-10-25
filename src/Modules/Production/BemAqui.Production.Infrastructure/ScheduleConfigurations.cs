using BemAqui.Production.Application;
using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Production.Infrastructure;

/// <summary>
/// Execuções do plano (PLN-01a, ADR 0029): gatilho e situação em texto, no máximo uma marca "pendente" (índice único parcial) e a busca
/// da última execução pela data de fim. Fora da auditoria.
/// </summary>
internal sealed class ScheduleRunConfiguration : IEntityTypeConfiguration<ScheduleRun>
{
    public void Configure(EntityTypeBuilder<ScheduleRun> b)
    {
        b.ToTable("schedule_runs", ProductionSchema.Name);
        b.Property(e => e.Trigger).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Error).HasMaxLength(2000);
        b.HasIndex(e => e.Status).IsUnique().HasDatabaseName(ScheduleService.PendingIndex).HasFilter("status = 'Pending'");
        b.HasIndex(e => e.FinishedAt);
    }
}

/// <summary>
/// Alocações da última execução bem-sucedida: horas <c>numeric(18,4)</c> por etapa, centro e dia; sem FK para as OS (o plano é
/// substituído inteiro a cada execução e nunca bloqueia o documento). Índices para a ocupação por centro e dia (PLN-04) e por etapa.
/// </summary>
internal sealed class ScheduleAllocationConfiguration : IEntityTypeConfiguration<ScheduleAllocation>
{
    public void Configure(EntityTypeBuilder<ScheduleAllocation> b)
    {
        b.ToTable("schedule_allocations", ProductionSchema.Name, t => t.HasCheckConstraint("ck_schedule_allocations_hours", "hours > 0"));
        b.Property(e => e.Hours).HasPrecision(18, 4);
        b.HasIndex(e => new { e.Date, e.WorkCenterId });
        b.HasIndex(e => e.StepId);
        b.HasIndex(e => e.ProductionOrderId);
    }
}

/// <summary>Previsão por etapa (<c>step_id</c> é a chave: uma linha por etapa), horas restantes <c>numeric(18,4)</c> e o motivo (até 200).</summary>
internal sealed class ScheduleStepForecastConfiguration : IEntityTypeConfiguration<ScheduleStepForecast>
{
    public void Configure(EntityTypeBuilder<ScheduleStepForecast> b)
    {
        b.ToTable("schedule_step_forecasts", ProductionSchema.Name, t => t.HasCheckConstraint("ck_schedule_step_forecasts_dates", "planned_end IS NULL OR planned_end >= planned_start"));
        b.HasKey(e => e.StepId);
        b.Property(e => e.StepId).ValueGeneratedNever();
        b.Property(e => e.Hours).HasPrecision(18, 4);
        b.Property(e => e.NoForecastReason).HasMaxLength(200);
        b.HasIndex(e => e.ProductionOrderId);
    }
}
