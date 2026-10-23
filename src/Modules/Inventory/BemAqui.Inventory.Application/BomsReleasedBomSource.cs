using BemAqui.Engineering.Contracts;
using BemAqui.Production.Contracts;

namespace BemAqui.Inventory.Application;

/// <summary>
/// A fonte da reconciliação pela ENG-02 (ADR 0023 §6): a revisão liberada vigente do <see cref="IBoms.ReleasedForOrderAsync"/>, já na
/// unidade de estoque; para a recomposição (D3), as OS em aberto do <see cref="IProductionOrders.OpenAsync"/> que têm BOM liberada.
/// </summary>
public sealed class BomsReleasedBomSource(IBoms boms, IProductionOrders orders) : IReleasedBomSource
{
    public async Task<ReleasedBom?> ReleasedForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default) =>
        await boms.ReleasedForOrderAsync(productionOrderId, cancellationToken) is { } bom
            ? new ReleasedBom(bom.BomId, bom.Revision, bom.ReleasedAt, bom.Lines.Select(l => new ReleasedBomLine(l.BomLineId, l.ItemId, l.RequiredQty)).ToList())
            : null;

    public async Task<IReadOnlyList<Guid>> OpenOrdersWithReleasedBomAsync(CancellationToken cancellationToken = default)
    {
        var found = new List<Guid>();
        foreach (var order in await orders.OpenAsync(cancellationToken))
        {
            if (await boms.IsReleasedAsync(order.Id, cancellationToken))
            {
                found.Add(order.Id);
            }
        }

        return found;
    }
}
