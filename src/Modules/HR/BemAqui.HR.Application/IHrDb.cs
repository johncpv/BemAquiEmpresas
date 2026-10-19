using BemAqui.HR.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>Dados do módulo HR (ADR 0006): os handlers do módulo só enxergam estas tabelas.</summary>
public interface IHrDb
{
    DbSet<JobPosition> JobPositions { get; }

    DbSet<WorkSchedule> WorkSchedules { get; }

    DbSet<LaborUnion> LaborUnions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
