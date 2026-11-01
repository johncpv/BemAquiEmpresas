using BemAqui.HR.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.HR.Infrastructure;

/// <summary>Conferência paralela (FLH-06): uma por competência e tipo da folha; as folhas comparadas por chave estrangeira.</summary>
internal sealed class PayrollParallelRunConfiguration : IEntityTypeConfiguration<PayrollParallelRun>
{
    public void Configure(EntityTypeBuilder<PayrollParallelRun> b)
    {
        b.ToTable("parallel_runs", HrSchema.Name);
        b.Property(e => e.RunType).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.PayrollType).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.EnteredNet).HasPrecision(18, 2);
        b.Property(e => e.CalculatedNet).HasPrecision(18, 2);
        b.Property(e => e.AccountantName).HasMaxLength(PayrollParallelRules.AccountantNameMax);
        b.Property(e => e.AccountantCrc).HasMaxLength(PayrollParallelRules.CrcMax);
        b.HasOne<PayrollRun>().WithMany().HasForeignKey(e => e.EnteredRunId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PayrollRun>().WithMany().HasForeignKey(e => e.CalculatedRunId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.Period, e.PayrollType }).IsUnique().HasDatabaseName("ix_parallel_runs_period_type");
    }
}

/// <summary>Linhas da comparação: dinheiro numeric(18,2), o percentual com 4 casas, o caso de correção em jsonb.</summary>
internal sealed class PayrollParallelDifferenceConfiguration : IEntityTypeConfiguration<PayrollParallelDifference>
{
    public void Configure(EntityTypeBuilder<PayrollParallelDifference> b)
    {
        b.ToTable("parallel_differences", HrSchema.Name);
        b.Property(e => e.PayrollItemCode).HasMaxLength(PayrollRules.CodeMaxLength);
        b.Property(e => e.ItemName).HasMaxLength(PayrollRules.NameMaxLength);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.EnteredAmount).HasPrecision(18, 2);
        b.Property(e => e.CalculatedAmount).HasPrecision(18, 2);
        b.Property(e => e.Difference).HasPrecision(18, 2);
        b.Property(e => e.DifferencePct).HasPrecision(18, 4);
        b.Property(e => e.Classification).HasConversion<string>().HasMaxLength(30);
        b.Property(e => e.Justification).HasMaxLength(PayrollParallelRules.JustificationMax);
        b.Property(e => e.CorrectionCase).HasColumnType("jsonb");
        b.HasOne<PayrollParallelRun>().WithMany().HasForeignKey(e => e.ParallelRunId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.ParallelRunId, e.EmployeeId, e.PayrollItemCode }).IsUnique().HasDatabaseName("ix_parallel_differences_row");
        b.HasIndex(e => e.EmployeeId).HasDatabaseName("ix_parallel_differences_employee");
    }
}

/// <summary>De-para de rubricas: um código da contadora aponta para um código do sistema.</summary>
internal sealed class PayrollItemMappingConfiguration : IEntityTypeConfiguration<PayrollItemMapping>
{
    public void Configure(EntityTypeBuilder<PayrollItemMapping> b)
    {
        b.ToTable("payroll_item_mappings", HrSchema.Name);
        b.Property(e => e.EnteredCode).HasMaxLength(PayrollRules.CodeMaxLength);
        b.Property(e => e.CalculatedCode).HasMaxLength(PayrollRules.CodeMaxLength);
        b.HasIndex(e => e.EnteredCode).IsUnique().HasDatabaseName("ix_payroll_item_mappings_entered");
    }
}

/// <summary>Virada e volta da folha: só cresce; as competências aceitas em uuid[].</summary>
internal sealed class PayrollModeChangeConfiguration : IEntityTypeConfiguration<PayrollModeChange>
{
    public void Configure(EntityTypeBuilder<PayrollModeChange> b)
    {
        b.ToTable("payroll_mode_changes", HrSchema.Name);
        b.Property(e => e.RunType).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Mode).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Reason).HasMaxLength(PayrollParallelRules.ReasonMax);
        b.Property(e => e.AccountantName).HasMaxLength(PayrollParallelRules.AccountantNameMax);
        b.Property(e => e.AccountantCrc).HasMaxLength(PayrollParallelRules.CrcMax);
        b.HasIndex(e => new { e.RunType, e.ChangedAt }).HasDatabaseName("ix_payroll_mode_changes_type");
    }
}
