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

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;
}
