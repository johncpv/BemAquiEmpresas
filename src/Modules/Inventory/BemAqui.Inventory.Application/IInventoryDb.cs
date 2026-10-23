using BemAqui.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BemAqui.Inventory.Application;

/// <summary>Dados do módulo Inventory (ADR 0006): os handlers do módulo só enxergam estas tabelas.</summary>
public interface IInventoryDb
{
    DbSet<Location> Locations { get; }

    DbSet<MeasureUnit> Units { get; }

    DbSet<ItemCategory> ItemCategories { get; }

    DbSet<WarrantyDefault> WarrantyDefaults { get; }

    DbSet<Item> Items { get; }

    DbSet<ItemUnitConversion> ItemUnitConversions { get; }

    DbSet<ItemSupplierCode> ItemSupplierCodes { get; }

    /// <summary>Posição (item, local), lote e movimentos (EST-01a): só o <see cref="StockLedger"/> escreve nas três.</summary>
    DbSet<StockPosition> StockPositions { get; }

    DbSet<StockBalance> StockBalances { get; }

    DbSet<StockMovement> StockMovements { get; }

    DbSet<Lot> Lots { get; }

    DbSet<ManualPosting> ManualPostings { get; }

    DbSet<ManualPostingLine> ManualPostingLines { get; }

    DbSet<InventoryParameters> InventoryParameters { get; }

    /// <summary>Reservas e o histórico delas (EST-02): só o <see cref="ReservationAllocator"/> escreve.</summary>
    DbSet<Reservation> Reservations { get; }

    DbSet<ReservationChange> ReservationChanges { get; }

    /// <summary>Para trocar o local padrão em dois passos numa transação (o índice único parcial é checado a cada comando).</summary>
    DatabaseFacade Database { get; }

    /// <summary>Para adicionar a entidade nova só depois das validações (a importação usa um contexto para a carga toda).</summary>
    Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
