using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application;

/// <summary>
/// <see cref="IPurchaseOrders"/> (CMP-03a CA 24) e a porta do job do mínimo da CMP-01 (<see cref="IOpenPurchaseOrderQuantities"/>, destino
/// "Estoque geral"). As linhas guardam a quantidade já na unidade de estoque (convertida pelo <c>IUnitConversion</c> na gravação).
/// </summary>
public sealed class PurchaseOrdersContract(IPurchasingDb db) : IPurchaseOrders, IOpenPurchaseOrderQuantities
{
    public async Task<PurchaseOrderInfo?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.PurchaseOrders.AsNoTracking().Where(o => o.Id == id)
            .Select(o => new PurchaseOrderInfo(o.Id, o.Number, o.Status, o.PartyId, o.ProjectId, o.Total, o.ExpectedDate)).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PurchaseOrderForRequestLine>> ForRequestLinesAsync(IReadOnlyCollection<Guid> requestLineIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestLineIds);
        if (requestLineIds.Count == 0)
        {
            return [];
        }

        // Pelos itens das linhas de SC (consulta simples) e o vínculo no array da linha do pedido conferido no processo.
        var items = await db.PurchaseRequestLines.AsNoTracking().Where(l => requestLineIds.Contains(l.Id)).Select(l => l.ItemId).Distinct().ToListAsync(cancellationToken);
        var lines = await (from l in db.PurchaseOrderLines.AsNoTracking()
                           join o in db.PurchaseOrders.AsNoTracking() on l.OrderId equals o.Id
                           where items.Contains(l.ItemId) && o.Status != PurchaseOrderStatus.Cancelled
                           select new { l.RequestLineIds, o.Id, o.Number, o.Status, o.ExpectedDate }).ToListAsync(cancellationToken);
        var wanted = requestLineIds.ToHashSet();
        return lines.SelectMany(l => l.RequestLineIds.Where(wanted.Contains).Select(r => new PurchaseOrderForRequestLine(r, l.Id, l.Number, l.Status, l.ExpectedDate)))
            .Distinct().OrderBy(p => p.ExpectedDate).ThenBy(p => p.Number, StringComparer.Ordinal).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> OpenQuantityByItemAsync(
        IReadOnlyCollection<Guid> itemIds, PurchaseOrderDestination destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        ArgumentNullException.ThrowIfNull(destination);
        if (itemIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var open = PurchaseOrderRules.OpenForReceipt.ToList();
        var orders = db.PurchaseOrders.AsNoTracking().Where(o => open.Contains(o.Status));
        if (!destination.AllDestinations)
        {
            orders = destination.ProjectId is { } project ? orders.Where(o => o.ProjectId == project) : orders.Where(o => o.ProjectId == null);
        }

        var rows = await (from l in db.PurchaseOrderLines.AsNoTracking()
                          join o in orders on l.OrderId equals o.Id
                          where itemIds.Contains(l.ItemId)
                          group l by l.ItemId into g
                          select new { ItemId = g.Key, Open = g.Sum(l => l.Quantity - l.QuantityReceived) }).ToListAsync(cancellationToken);
        return rows.Where(r => r.Open > 0m).ToDictionary(r => r.ItemId, r => r.Open);
    }

    public async Task<IReadOnlyList<PurchaseCommitment>> CommittedByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var open = PurchaseOrderRules.OpenForReceipt.ToList();
        var rows = await (from l in db.PurchaseOrderLines.AsNoTracking()
                          join o in db.PurchaseOrders.AsNoTracking() on l.OrderId equals o.Id
                          where o.ProjectId == projectId && open.Contains(o.Status)
                          select new { l.CostType, l.Quantity, l.QuantityReceived, l.UnitPrice }).ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.CostType)
            .Select(g => new PurchaseCommitment(g.Key, g.Sum(r => PurchaseOrderRules.LineTotal(Math.Max(0m, r.Quantity - r.QuantityReceived), r.UnitPrice))))
            .Where(c => c.Amount > 0m).ToList();
    }

    public async Task<LastPurchaseInfo?> LastPurchaseAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var counted = PurchaseOrderRules.CountsForReference.Where(s => s != PurchaseOrderStatus.PendingApproval).ToList();
        return await (from l in db.PurchaseOrderLines.AsNoTracking()
                      join o in db.PurchaseOrders.AsNoTracking() on l.OrderId equals o.Id
                      where l.ItemId == itemId && counted.Contains(o.Status) && o.ApprovedAt != null
                      orderby o.ApprovedAt descending, o.Number descending
                      select new LastPurchaseInfo(l.ItemId, o.Id, o.Number, o.PartyId, l.UnitPrice, o.ApprovedAt!.Value)).FirstOrDefaultAsync(cancellationToken);
    }

    public Task<IReadOnlyDictionary<Guid, decimal>> GeneralStockOpenQuantityAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default) =>
        OpenQuantityByItemAsync(itemIds, PurchaseOrderDestination.GeneralStock, cancellationToken);
}
