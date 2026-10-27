using BemAqui.HR.Application;
using BemAqui.HR.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.HR.Infrastructure;

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> b)
    {
        b.ToTable("employees", HrSchema.Name);
        b.Property(e => e.Name).HasMaxLength(150);
        b.Property(e => e.Registration).HasMaxLength(20);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Cpf).HasMaxLength(11);
        b.Property(e => e.BaseSalary).HasPrecision(18, 2);
        b.Property(e => e.Rg).HasMaxLength(20);
        b.Property(e => e.Pis).HasMaxLength(11);
        b.Property(e => e.CtpsNumber).HasMaxLength(20);
        b.Property(e => e.CtpsSeries).HasMaxLength(10);
        b.Property(e => e.VoterId).HasMaxLength(20);
        b.Property(e => e.ZipCode).HasMaxLength(8);
        b.Property(e => e.Street).HasMaxLength(150);
        b.Property(e => e.Number).HasMaxLength(20);
        b.Property(e => e.Complement).HasMaxLength(100);
        b.Property(e => e.District).HasMaxLength(100);
        b.Property(e => e.City).HasMaxLength(100);
        b.Property(e => e.State).HasMaxLength(2);
        b.Property(e => e.Phone).HasMaxLength(20);
        b.Property(e => e.Email).HasMaxLength(150);
        b.Property(e => e.PreviousEmail).HasMaxLength(150);
        // Conta para pagamento, adicionais e documentos extras (CAD-04c2).
        b.Property(e => e.BankCode).HasMaxLength(3);
        b.Property(e => e.BankAgency).HasMaxLength(10);
        b.Property(e => e.BankAccount).HasMaxLength(20);
        b.Property(e => e.BankAccountType).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.BankHolderName).HasMaxLength(150);
        b.Property(e => e.BankHolderCpf).HasMaxLength(11);
        b.Property(e => e.PixKeyType).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.PixKey).HasMaxLength(77);
        b.Property(e => e.BankMismatchReason).HasMaxLength(200);
        b.Property(e => e.BankReviewNote).HasMaxLength(200);
        b.Property(e => e.CnhNumber).HasMaxLength(11);
        b.Property(e => e.CnhCategory).HasMaxLength(5);
        b.Property(e => e.MilitaryCertificate).HasMaxLength(20);
        b.HasOne<JobPosition>().WithMany().HasForeignKey(e => e.PositionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WorkSchedule>().WithMany().HasForeignKey(e => e.WorkScheduleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LaborUnion>().WithMany().HasForeignKey(e => e.UnionId).OnDelete(DeleteBehavior.Restrict);
        // CPF e matrícula únicos entre os não excluídos (CA 4 e 7).
        b.HasIndex(e => e.Cpf).IsUnique().HasFilter("NOT is_deleted").HasDatabaseName(EmployeeService.CpfIndex);
        b.HasIndex(e => e.Registration).IsUnique().HasFilter("NOT is_deleted AND registration IS NOT NULL").HasDatabaseName(EmployeeService.RegistrationIndex);
    }
}

/// <summary>Dependentes (CAD-04c2): CPF único entre os não excluídos do mesmo funcionário (CA 2).</summary>
internal sealed class EmployeeDependentConfiguration : IEntityTypeConfiguration<EmployeeDependent>
{
    public void Configure(EntityTypeBuilder<EmployeeDependent> b)
    {
        b.ToTable("employee_dependents", HrSchema.Name);
        b.Property(e => e.Name).HasMaxLength(150);
        b.Property(e => e.Cpf).HasMaxLength(11);
        b.Property(e => e.Kinship).HasConversion<string>().HasMaxLength(20);
        b.HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.EmployeeId).HasDatabaseName("ix_employee_dependents_employee");
        b.HasIndex(e => new { e.EmployeeId, e.Cpf }).IsUnique().HasFilter("NOT is_deleted AND cpf IS NOT NULL").HasDatabaseName(EmployeeComplementService.DependentCpfIndex);
    }
}

internal sealed class EmployeeHistoryConfiguration : IEntityTypeConfiguration<EmployeeHistoryEntry>
{
    public void Configure(EntityTypeBuilder<EmployeeHistoryEntry> b)
    {
        b.ToTable("employee_history", HrSchema.Name);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.OldValue).HasMaxLength(200);
        b.Property(e => e.NewValue).HasMaxLength(200);
        b.Property(e => e.Reason).HasMaxLength(200);
        // Afastamento (CAD-04c1): tipo e fim previsto só no Leave; marcas de publicação dos eventos (CA 7).
        b.Property(e => e.LeaveType).HasConversion<string>().HasMaxLength(20);
        // Rastro das mudanças do afastamento (CAD-04c1b): o LeaveChange aponta para o Leave; o cancelado fica com a marca.
        b.Property(e => e.PreviousLeaveType).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.LeaveChange).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(e => e.LeaveId).HasFilter("leave_id IS NOT NULL").HasDatabaseName("ix_employee_history_leave_changes");
        b.HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.EmployeeId, e.EffectiveDate }).HasDatabaseName("ix_employee_history_employee");
        // Job diário (CA 7): só os afastamentos com evento pendente.
        b.HasIndex(e => e.EffectiveDate).HasFilter("kind = 'Leave' AND (start_published_at IS NULL OR end_published_at IS NULL)").HasDatabaseName("ix_employee_history_leave_pending");
    }
}
