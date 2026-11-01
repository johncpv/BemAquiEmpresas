using BemAqui.HR.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace BemAqui.HR.Application;

/// <summary>Dados do módulo HR (ADR 0006): os handlers do módulo só enxergam estas tabelas.</summary>
public interface IHrDb
{
    DbSet<JobPosition> JobPositions { get; }

    DbSet<WorkSchedule> WorkSchedules { get; }

    DbSet<LaborUnion> LaborUnions { get; }

    DbSet<Employee> Employees { get; }

    DbSet<EmployeeHistoryEntry> EmployeeHistory { get; }

    DbSet<EmployeeDependent> EmployeeDependents { get; }

    // Folha lançada (RH-02a).
    DbSet<PayrollItem> PayrollItems { get; }

    DbSet<PayrollRun> PayrollRuns { get; }

    DbSet<Payslip> Payslips { get; }

    DbSet<PayslipLine> PayslipLines { get; }

    DbSet<PayrollRunCharge> PayrollRunCharges { get; }

    DbSet<HrParameter> HrParameters { get; }

    // Fechamento e folha prevista (RH-02c).
    DbSet<PayrollForecast> PayrollForecasts { get; }

    // Ponto (RH-01a): marcações e a ciência da geolocalização.
    DbSet<TimeEntry> TimeEntries { get; }

    DbSet<TimeClockGeoConsent> TimeClockGeoConsents { get; }

    // Ajustes do ponto (RH-01b).
    DbSet<TimeAdjustment> TimeAdjustments { get; }

    // Espelho, fechamento e dias revisados (RH-01c).
    DbSet<Timesheet> Timesheets { get; }

    DbSet<TimesheetDayReview> TimesheetDayReviews { get; }

    // Tabelas legais da folha (FLH-01) e os datasets da contadora.
    DbSet<PayrollTaxTable> PayrollTaxTables { get; }

    DbSet<PayrollTaxTableDataset> PayrollTaxTableDatasets { get; }

    // Histórico do cálculo da folha (FLH-02a CA 19).
    DbSet<PayrollCalculationLog> PayrollCalculationLogs { get; }

    /// <summary>Transação do fechamento: a folha, os títulos do Finance (mesmo contexto) e a numeração gravam juntos ou nada grava.</summary>
    Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade Database { get; }

    /// <summary>Para desistir do que ficou pendente depois de um conflito de versão (RH-02b: PDF gerado ao mesmo tempo).</summary>
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;
}
