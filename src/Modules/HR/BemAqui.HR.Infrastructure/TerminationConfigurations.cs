using BemAqui.HR.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.HR.Infrastructure;

/// <summary>
/// Rescisões (FLH-05, <c>hr.terminations</c>): uma não cancelada por funcionário; a folha de rescisão calculada é uma por rescisão; o saldo do
/// FGTS em numeric(18,2); datas de negócio em <c>date</c>.
/// </summary>
internal sealed class EmployeeTerminationConfiguration : IEntityTypeConfiguration<EmployeeTermination>
{
    public void Configure(EntityTypeBuilder<EmployeeTermination> b)
    {
        b.ToTable("terminations", HrSchema.Name);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(30);
        b.Property(e => e.NoticeKind).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.FgtsBalance).HasPrecision(18, 2);
        b.Property(e => e.CancelReason).HasMaxLength(200);
        b.HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PayrollRun>().WithMany().HasForeignKey(e => e.PayrollRunId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(e => e.EmployeeId).IsUnique().HasFilter("status <> 'Cancelled'").HasDatabaseName("ix_terminations_employee_active");
        b.HasIndex(e => e.PayrollRunId).IsUnique().HasFilter("payroll_run_id IS NOT NULL").HasDatabaseName("ix_terminations_run");
        b.HasIndex(e => new { e.Status, e.TerminationDate }).HasDatabaseName("ix_terminations_status_date");
    }
}
