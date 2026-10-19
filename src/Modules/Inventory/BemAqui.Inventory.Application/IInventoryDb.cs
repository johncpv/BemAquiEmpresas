using BemAqui.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BemAqui.Inventory.Application;

/// <summary>Dados do módulo Inventory (ADR 0006): os handlers do módulo só enxergam estas tabelas.</summary>
public interface IInventoryDb
{
    DbSet<Location> Locations { get; }

    /// <summary>Para trocar o local padrão em dois passos numa transação (o índice único parcial é checado a cada comando).</summary>
    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
