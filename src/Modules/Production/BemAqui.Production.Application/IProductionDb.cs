using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BemAqui.Production.Application;

/// <summary>Dados do módulo Production (ADR 0006): os handlers do módulo só enxergam estas tabelas.</summary>
public interface IProductionDb
{
    DbSet<WorkCenter> WorkCenters { get; }

    DbSet<Calendar> Calendars { get; }

    DbSet<CalendarExceptionDay> CalendarExceptions { get; }

    DbSet<ProductionOrder> ProductionOrders { get; }

    DbSet<ProductionOrderStep> ProductionOrderSteps { get; }

    DbSet<ProductionOrderChecklistItem> ProductionOrderChecklist { get; }

    DbSet<ProductionOrderProjectItem> ProductionOrderProjectItems { get; }

    DbSet<InstallationTemplateLine> InstallationTemplate { get; }

    DbSet<LaborEntry> LaborEntries { get; }

    DbSet<StepPause> StepPauses { get; }

    DbSet<LaborCostRecord> LaborCostHistory { get; }

    DbSet<ProductionParameter> ProductionParameters { get; }

    DbSet<NonConformity> NonConformities { get; }

    DbSet<QualityDecision> QualityDecisions { get; }

    DbSet<Shipment> Shipments { get; }

    DbSet<ShipmentLine> ShipmentLines { get; }

    DbSet<ShipmentLineSerial> ShipmentLineSerials { get; }

    /// <summary>Plano (PLN-01a, ADR 0029) só para leitura e sem rastreamento: o único escritor é o <see cref="ScheduleService"/> (<see cref="IProductionPlanDb"/>).</summary>
    IQueryable<ScheduleRun> ScheduleRunsView { get; }

    IQueryable<ScheduleAllocation> ScheduleAllocationsView { get; }

    IQueryable<ScheduleStepForecast> ScheduleStepForecastsView { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;
}
