using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BemAqui.Production.Application;

/// <summary>
/// Escrita das tabelas do plano (PLN-01a CA 11, ADR 0029 §2): <c>schedule_runs</c>, <c>schedule_allocations</c> e
/// <c>schedule_step_forecasts</c>. Só o <see cref="ScheduleService"/> depende desta interface (teste de arquitetura); quem lê usa as vistas
/// sem rastreamento do <see cref="IProductionDb"/>.
/// </summary>
public interface IProductionPlanDb
{
    DbSet<ScheduleRun> ScheduleRuns { get; }

    DbSet<ScheduleAllocation> ScheduleAllocations { get; }

    DbSet<ScheduleStepForecast> ScheduleStepForecasts { get; }

    /// <summary>Última previsão publicada de cada projeto (PLN-01b CA 7).</summary>
    DbSet<ScheduleProjectForecast> ScheduleProjectForecasts { get; }

    DatabaseFacade Database { get; }

    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
