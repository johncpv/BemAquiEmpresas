using BemAqui.Purchasing.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application;

/// <summary>Implementação de <see cref="IPurchaseRequests"/> (CA 15): linhas não canceladas das OS, com o número e a situação.</summary>
public sealed class PurchaseRequestsContract(IPurchasingDb db) : IPurchaseRequests
{
    public Task<IReadOnlyList<PurchaseRequestLineInfo>> ForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default) =>
        ForOrdersAsync([productionOrderId], cancellationToken);

    public async Task<IReadOnlyList<PurchaseRequestLineInfo>> ForOrdersAsync(IReadOnlyCollection<Guid> productionOrderIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productionOrderIds);
        if (productionOrderIds.Count == 0)
        {
            return [];
        }

        return await (from l in db.PurchaseRequestLines.AsNoTracking()
                      join r in db.PurchaseRequests.AsNoTracking() on l.RequestId equals r.Id
                      where l.ProductionOrderId != null && productionOrderIds.Contains(l.ProductionOrderId.Value) && l.Status != PurchaseRequestStatus.Cancelled
                      orderby r.Number, l.Sequence
                      select new PurchaseRequestLineInfo(r.Id, r.Number, l.Id, l.ItemId, l.Quantity, l.NeededBy, l.Status, l.Origin, l.ProductionOrderId, l.ProjectId))
            .ToListAsync(cancellationToken);
    }
}
