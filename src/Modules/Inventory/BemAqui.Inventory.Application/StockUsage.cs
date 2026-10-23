using BemAqui.Inventory.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Uso de item e local pelo estoque (CAD-02a CA 14 a 17, CAD-03 CA 5 e 6): com movimento, o item não troca de unidade nem de lote
/// e o local não é excluído; com saldo, nenhum dos dois é inativado.
/// </summary>
public sealed class StockUsage(IInventoryDb db) : IItemUsageProvider, ILocationUsageProvider
{
    public Task<bool> HasMovementAsync(Guid itemId, CancellationToken cancellationToken = default) =>
        db.StockMovements.AnyAsync(m => m.ItemId == itemId, cancellationToken);

    public Task<bool> HasBalanceAsync(Guid itemId, CancellationToken cancellationToken = default) =>
        db.StockPositions.AnyAsync(p => p.ItemId == itemId && p.QuantityOnHand != 0, cancellationToken);

    async Task<string?> IItemUsageProvider.UsageAsync(Guid itemId, CancellationToken cancellationToken) =>
        await HasMovementAsync(itemId, cancellationToken) ? "movimentos de estoque" : null;

    async Task<bool> ILocationUsageProvider.HasBalanceAsync(Guid locationId, CancellationToken cancellationToken) =>
        await db.StockPositions.AnyAsync(p => p.LocationId == locationId && p.QuantityOnHand != 0, cancellationToken);

    async Task<string?> ILocationUsageProvider.UsageAsync(Guid locationId, CancellationToken cancellationToken) =>
        await db.StockMovements.AnyAsync(m => m.LocationId == locationId, cancellationToken) ? "movimentos de estoque" : null;
}
