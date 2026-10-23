using BemAqui.Engineering.Contracts;
using BemAqui.Engineering.Domain;
using BemAqui.Inventory.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Engineering.Application;

/// <summary>Implementação de <see cref="IBoms"/> (ENG-02 CA 18): a revisão liberada vigente, lida só das tabelas do Engineering.</summary>
public sealed class BomsContract(IEngineeringDb db) : IBoms
{
    public async Task<ReleasedBomInfo?> ReleasedForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default)
    {
        var bom = await db.Boms.AsNoTracking().FirstOrDefaultAsync(b => b.ProductionOrderId == productionOrderId && b.Status == BomStatus.Released, cancellationToken);
        if (bom is null)
        {
            return null;
        }

        var lines = await db.BomLines.AsNoTracking().Where(l => l.BomId == bom.Id).OrderBy(l => l.Sequence)
            .Select(l => new BomRequirement(l.Id, l.ItemId, l.RequiredQuantity)).ToListAsync(cancellationToken);
        return new ReleasedBomInfo(bom.Id, bom.ProductionOrderId, bom.Revision, bom.ReleasedAt!.Value, lines);
    }

    public Task<bool> IsReleasedAsync(Guid productionOrderId, CancellationToken cancellationToken = default) =>
        db.Boms.AnyAsync(b => b.ProductionOrderId == productionOrderId && b.Status == BomStatus.Released, cancellationToken);
}

/// <summary>Item usado em BOM (CAD-02a CA 14 e 16): em uso, o item só inativa. Movimento e saldo são do Estoque (EST-01a).</summary>
public sealed class BomItemUsage(IEngineeringDb db) : IItemUsageProvider
{
    public Task<bool> HasMovementAsync(Guid itemId, CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<bool> HasBalanceAsync(Guid itemId, CancellationToken cancellationToken = default) => Task.FromResult(false);

    public async Task<string?> UsageAsync(Guid itemId, CancellationToken cancellationToken = default) =>
        await db.BomLines.AnyAsync(l => l.ItemId == itemId, cancellationToken) ? "BOMs de OS" : null;
}
