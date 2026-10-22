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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;
}
