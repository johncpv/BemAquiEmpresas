using BemAqui.Inventory.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Saldo de um local para o caixa da loja (PDV-05, PP-02): todas as posições do local ou só as dos itens com movimento lançado depois de um
/// instante (o caixa pede o delta com uma folga, então um movimento confirmado tarde ainda desce). Só leitura da posição agregada (ADR 0023 §2).
/// </summary>
public sealed class StockLocationFeed(IInventoryDb db) : IStockLocationFeed
{
    public async Task<IReadOnlyList<StockAvailability>> LocationBalancesAsync(Guid locationId, DateTimeOffset? changedSince, CancellationToken cancellationToken = default)
    {
        var positions = db.StockPositions.AsNoTracking().Where(p => p.LocationId == locationId);
        if (changedSince is { } since)
        {
            var changed = db.StockMovements.AsNoTracking().Where(m => m.LocationId == locationId && m.PostedAt > since).Select(m => m.ItemId);
            positions = positions.Where(p => changed.Contains(p.ItemId));
        }

        return await positions
            .Select(p => new StockAvailability(p.ItemId, p.LocationId, p.QuantityOnHand, p.QuantityReserved, p.QuantityOnHand - p.QuantityReserved))
            .ToListAsync(cancellationToken);
    }
}
