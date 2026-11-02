using BemAqui.HR.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.HR.Infrastructure;

/// <summary>Períodos aquisitivos (FLH-03 CA 1): um por funcionário e ordem desde a admissão; datas de negócio em <c>date</c>.</summary>
internal sealed class VacationPeriodConfiguration : IEntityTypeConfiguration<VacationPeriod>
{
    public void Configure(EntityTypeBuilder<VacationPeriod> b)
    {
        b.ToTable("vacation_periods", HrSchema.Name);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.RestartedReason).HasMaxLength(200);
        b.Ignore(e => e.Balance);
        b.HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.EmployeeId, e.Sequence }).IsUnique().HasDatabaseName("ix_vacation_periods_employee_sequence");
        b.HasIndex(e => new { e.Status, e.ConcessionEndsOn }).HasDatabaseName("ix_vacation_periods_status_concession");
    }
}

/// <summary>Programações de férias (FLH-03 CA 4): por funcionário e período; a folha de férias calculada é uma por programação.</summary>
internal sealed class VacationScheduleConfiguration : IEntityTypeConfiguration<VacationSchedule>
{
    public void Configure(EntityTypeBuilder<VacationSchedule> b)
    {
        b.ToTable("vacation_schedules", HrSchema.Name);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.CalculatedSplit).HasColumnType("jsonb");
        b.Property(e => e.CancelReason).HasMaxLength(200);
        b.Ignore(e => e.EndsOn);
        b.HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<VacationPeriod>().WithMany().HasForeignKey(e => e.PeriodId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PayrollRun>().WithMany().HasForeignKey(e => e.PayrollRunId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(e => new { e.EmployeeId, e.StartsOn }).HasDatabaseName("ix_vacation_schedules_employee_start");
        b.HasIndex(e => e.PeriodId).HasDatabaseName("ix_vacation_schedules_period");
        b.HasIndex(e => e.PayrollRunId).IsUnique().HasFilter("payroll_run_id IS NOT NULL").HasDatabaseName("ix_vacation_schedules_run");
    }
}
