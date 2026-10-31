using BemAqui.HR.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.HR.Infrastructure;

/// <summary>
/// Marcações de ponto (RH-01a): índice (funcionário, instante); localização em <c>numeric(9,6)</c> (gravada com 4 casas); o aparelho é só o
/// id do Identity, sem chave estrangeira entre módulos.
/// </summary>
internal sealed class TimeEntryConfiguration : IEntityTypeConfiguration<TimeEntry>
{
    public void Configure(EntityTypeBuilder<TimeEntry> b)
    {
        b.ToTable("time_entries", HrSchema.Name);
        b.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Source).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Location).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Latitude).HasPrecision(9, 6);
        b.Property(e => e.Longitude).HasPrecision(9, 6);
        b.HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.EmployeeId, e.At }).HasDatabaseName("ix_time_entries_employee_at");
        b.HasIndex(e => e.Day).HasDatabaseName("ix_time_entries_day");
    }
}

/// <summary>Ciência do aviso da geolocalização (RH-01a CA 10): uma por funcionário e versão do texto.</summary>
internal sealed class TimeClockGeoConsentConfiguration : IEntityTypeConfiguration<TimeClockGeoConsent>
{
    public void Configure(EntityTypeBuilder<TimeClockGeoConsent> b)
    {
        b.ToTable("time_clock_geo_consents", HrSchema.Name);
        b.Property(e => e.TextVersion).HasMaxLength(20);
        b.HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.EmployeeId, e.TextVersion }).IsUnique().HasDatabaseName("ix_time_clock_geo_consents_employee_version");
    }
}

/// <summary>
/// Pedidos de ajuste do ponto (RH-01b): índices (funcionário, dia) e (situação, pedido em); a marcação desconsiderada e a criada ficam só
/// pelo id (sem chave estrangeira, a marcação nunca é apagada); o motivo da lista única em texto.
/// </summary>
internal sealed class TimeAdjustmentConfiguration : IEntityTypeConfiguration<TimeAdjustment>
{
    public void Configure(EntityTypeBuilder<TimeAdjustment> b)
    {
        b.ToTable("time_adjustments", HrSchema.Name);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.EntryType).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.AbsenceReason).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Source).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Justification).HasMaxLength(TimeAdjustmentRules.JustificationMax);
        b.Property(e => e.DecisionComment).HasMaxLength(TimeAdjustmentRules.CommentMax);
        b.HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.EmployeeId, e.Day }).HasDatabaseName("ix_time_adjustments_employee_day");
        b.HasIndex(e => new { e.Status, e.RequestedAt }).HasDatabaseName("ix_time_adjustments_status_requested");
        b.HasIndex(e => e.EntryId).HasDatabaseName("ix_time_adjustments_entry");
    }
}

/// <summary>
/// Espelho do período (RH-01c): único por (funcionário, período); os totais em minutos (<c>integer</c>); o índice por período serve à visão
/// geral, ao fechamento e à trava do período fechado.
/// </summary>
internal sealed class TimesheetConfiguration : IEntityTypeConfiguration<Timesheet>
{
    public void Configure(EntityTypeBuilder<Timesheet> b)
    {
        b.ToTable("timesheets", HrSchema.Name);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.JustifiedByReason).HasMaxLength(500);
        b.Property(e => e.ReopenReason).HasMaxLength(TimesheetRules.ReasonMax);
        b.Property(e => e.EmployeeAckSummary).HasMaxLength(500);
        b.HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.EmployeeId, e.Period }).IsUnique().HasDatabaseName("ix_timesheets_employee_period");
        b.HasIndex(e => new { e.Period, e.Status }).HasDatabaseName("ix_timesheets_period_status");
    }
}

/// <summary>Dia inconsistente revisado (RH-01c CA 11): um por funcionário e dia.</summary>
internal sealed class TimesheetDayReviewConfiguration : IEntityTypeConfiguration<TimesheetDayReview>
{
    public void Configure(EntityTypeBuilder<TimesheetDayReview> b)
    {
        b.ToTable("timesheet_day_reviews", HrSchema.Name);
        b.Property(e => e.Comment).HasMaxLength(TimesheetRules.CommentMax);
        b.HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.EmployeeId, e.Day }).IsUnique().HasDatabaseName("ix_timesheet_day_reviews_employee_day");
    }
}
