using BemAqui.Migrations.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BemAqui.Migrations.Application;

/// <summary>Dados do módulo Migration (ADR 0006 e 0017): só a staging e as chaves externas do schema <c>migration</c>.</summary>
public interface IMigrationDb
{
    DbSet<ImportRun> ImportRuns { get; }

    DbSet<ImportItem> ImportItems { get; }

    DbSet<ExternalRef> ExternalRefs { get; }

    /// <summary>Transação por entidade, savepoint por linha e o advisory lock da execução.</summary>
    DatabaseFacade Database { get; }

    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
