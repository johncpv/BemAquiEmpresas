using BemAqui.HR.Application;
using BemAqui.HR.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.HR.Infrastructure;

/// <summary>Rubricas (RH-02a CA 1): código único entre as não excluídas; seed_key única (o seed não recria a excluída).</summary>
internal sealed class PayrollItemConfiguration : IEntityTypeConfiguration<PayrollItem>
{
    public void Configure(EntityTypeBuilder<PayrollItem> b)
    {
        b.ToTable("payroll_items", HrSchema.Name);
        b.Property(e => e.Code).HasMaxLength(PayrollRules.CodeMaxLength);
        b.Property(e => e.Name).HasMaxLength(PayrollRules.NameMaxLength);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.SeedKey).HasMaxLength(50);
        b.HasIndex(e => e.Code).IsUnique().HasFilter("NOT is_deleted").HasDatabaseName(PayrollItemService.CodeIndex);
        b.HasIndex(e => e.SeedKey).IsUnique().HasFilter("seed_key IS NOT NULL").HasDatabaseName("ix_payroll_items_seed_key");
    }
}

/// <summary>Folha (CA 4): uma por tipo e competência.</summary>
internal sealed class PayrollRunConfiguration : IEntityTypeConfiguration<PayrollRun>
{
    public void Configure(EntityTypeBuilder<PayrollRun> b)
    {
        b.ToTable("payroll_runs", HrSchema.Name);
        b.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Mode).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(e => new { e.Period, e.Type }).IsUnique().HasDatabaseName(PayrollService.RunIndex);
        b.Property(e => e.ClosedNet).HasPrecision(18, 2);
        b.Property(e => e.ClosedCharges).HasPrecision(18, 2);
        b.Property(e => e.ReopenReason).HasMaxLength(200);
    }
}

/// <summary>Folha prevista (RH-02c CA 14): uma linha por mês, tipo e funcionário ou guia; cancelada, nunca apagada.</summary>
internal sealed class PayrollForecastConfiguration : IEntityTypeConfiguration<PayrollForecast>
{
    public void Configure(EntityTypeBuilder<PayrollForecast> b)
    {
        b.ToTable("payroll_forecasts", HrSchema.Name);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.ChargeKind).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Amount).HasPrecision(18, 2);
        b.Property(e => e.CancelReason).HasMaxLength(200);
        b.Ignore(e => e.Key);
        b.HasIndex(e => new { e.Period, e.Kind }).HasFilter("cancelled_at IS NULL");
    }
}

/// <summary>Holerite (CA 5 e 6): um por funcionário na folha; dinheiro numeric(18,2).</summary>
internal sealed class PayslipConfiguration : IEntityTypeConfiguration<Payslip>
{
    public void Configure(EntityTypeBuilder<Payslip> b)
    {
        b.ToTable("payslips", HrSchema.Name);
        b.Property(e => e.Gross).HasPrecision(18, 2);
        b.Property(e => e.Deductions).HasPrecision(18, 2);
        b.Property(e => e.Net).HasPrecision(18, 2);
        b.Property(e => e.InssBase).HasPrecision(18, 2);
        b.Property(e => e.FgtsBase).HasPrecision(18, 2);
        b.Property(e => e.IrrfBase).HasPrecision(18, 2);
        b.Property(e => e.FgtsAmount).HasPrecision(18, 2);
        b.HasOne<PayrollRun>().WithMany().HasForeignKey(e => e.PayrollRunId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.PayrollRunId, e.EmployeeId }).IsUnique().HasDatabaseName(PayrollService.PayslipIndex);
        b.HasIndex(e => e.EmployeeId).HasDatabaseName("ix_payslips_employee");
    }
}

internal sealed class PayslipLineConfiguration : IEntityTypeConfiguration<PayslipLine>
{
    public void Configure(EntityTypeBuilder<PayslipLine> b)
    {
        b.ToTable("payslip_lines", HrSchema.Name);
        b.Property(e => e.Code).HasMaxLength(PayrollRules.CodeMaxLength);
        b.Property(e => e.Description).HasMaxLength(PayrollRules.NameMaxLength);
        b.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Reference).HasMaxLength(PayrollRules.ReferenceMaxLength);
        b.Property(e => e.Amount).HasPrecision(18, 2);
        b.HasOne<Payslip>().WithMany().HasForeignKey(e => e.PayslipId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PayrollItem>().WithMany().HasForeignKey(e => e.PayrollItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.PayslipId).HasDatabaseName("ix_payslip_lines_payslip");
        b.HasIndex(e => e.PayrollItemId).HasDatabaseName("ix_payslip_lines_item");
    }
}

/// <summary>Guias da folha (CA 7): o órgão é o id do terceiro (do <c>IParties</c>, sem chave estrangeira entre módulos).</summary>
internal sealed class PayrollRunChargeConfiguration : IEntityTypeConfiguration<PayrollRunCharge>
{
    public void Configure(EntityTypeBuilder<PayrollRunCharge> b)
    {
        b.ToTable("payroll_run_charges", HrSchema.Name);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Description).HasMaxLength(PayrollRules.ChargeDescriptionMaxLength);
        b.Property(e => e.Amount).HasPrecision(18, 2);
        b.HasOne<PayrollRun>().WithMany().HasForeignKey(e => e.PayrollRunId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.PayrollRunId).HasDatabaseName("ix_payroll_run_charges_run");
    }
}

/// <summary>Parâmetros do RH (CA 3): uma linha por chave; nomes próprios de PK e índice, porque outros módulos têm a tabela parameters.</summary>
internal sealed class HrParameterConfiguration : IEntityTypeConfiguration<HrParameter>
{
    public void Configure(EntityTypeBuilder<HrParameter> b)
    {
        b.ToTable("parameters", HrSchema.Name);
        b.HasKey(e => e.Id).HasName("pk_hr_parameters");
        b.Property(e => e.Key).HasMaxLength(80);
        b.Property(e => e.Value).HasPrecision(18, 4);
        b.Property(e => e.Text).HasMaxLength(200);
        b.HasIndex(e => e.Key).IsUnique().HasDatabaseName("ix_hr_parameters_key");
    }
}
