using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>Dados do módulo Production (ADR 0006): os handlers do módulo só enxergam estas tabelas.</summary>
public interface IProductionDb
{
    DbSet<WorkCenter> WorkCenters { get; }

    DbSet<Calendar> Calendars { get; }

    DbSet<CalendarExceptionDay> CalendarExceptions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
