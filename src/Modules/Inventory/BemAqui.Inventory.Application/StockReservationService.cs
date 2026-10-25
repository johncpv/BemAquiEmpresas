using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Commercial.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.Production.Contracts;
using BemAqui.Purchasing.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Reservas e faltas (EST-02, ADR 0023 §6). <see cref="ReconcileOrderAsync"/> é o único caminho dos eventos e da recomposição: lê a
/// fonte (a BOM liberada vigente pelo <see cref="IReleasedBomSource"/> e a situação da OS pelo <see cref="IProductionOrders"/>),
/// calcula o alvo de cada item (0 com a OS cancelada, concluída ou inexistente) e leva as reservas até ele, nunca pelo delta do evento:
/// mensagens duplicadas, fora de ordem ou de revisão antiga terminam no mesmo estado (CA 1 e 2). Uma trava por OS
/// (<c>pg_advisory_xact_lock</c>) põe em fila duas reconciliações da mesma OS; depois vêm as travas dos itens em ordem crescente
/// de <c>item_id</c>, as mesmas do motor (ADR 0023 §3).
/// </summary>
public sealed class StockReservationService(
    IInventoryDb db, ReservationAllocator allocator, IReleasedBomSource boms, IProductionOrders orders, IProjects projects, TenantContextHolder tenant,
    IPurchaseRequests purchaseRequests)
    : IStockReservations
{
    public const string Module = "inventory";

    /// <summary>
    /// Reconcilia as reservas da OS a partir da fonte (CA 1 a 9, 12), na transação do chamador (a do consumidor da inbox ou a da
    /// recomposição). Com o módulo <c>inventory</c> não liberado no tenant, não faz nada (a recomposição cobre depois).
    /// </summary>
    public async Task ReconcileOrderAsync(Guid productionOrderId, string origin, bool isBackfill = false, CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        if (tenant.Resolved?.IsReleased(Module) != true)
        {
            return;
        }

        await LockOrderAsync(productionOrderId, cancellationToken);
        var status = await orders.StatusAsync(productionOrderId, cancellationToken);
        var closed = status is null or ProductionOrderState.Cancelled or ProductionOrderState.Completed;
        var bom = closed ? null : await boms.ReleasedForOrderAsync(productionOrderId, cancellationToken);
        var lines = bom?.Lines ?? [];
        var stockItems = await StockItemsAsync(lines.Select(l => l.ItemId).Distinct().ToList(), cancellationToken);
        var targets = ReservationPlanner.Targets(lines.Where(l => stockItems.Contains(l.ItemId)).Select(l => (l.ItemId, l.RequiredQty)), closed);
        var existing = await db.Reservations.AsNoTracking().Where(r => r.ProductionOrderId == productionOrderId).Select(r => new { r.ItemId, r.ProjectId }).ToListAsync(cancellationToken);
        if (targets.Count == 0 && existing.Count == 0)
        {
            return;
        }

        var projectId = existing.Select(r => r.ProjectId).FirstOrDefault(p => p != Guid.Empty);
        if (projectId == Guid.Empty)
        {
            projectId = (await orders.FindAsync(productionOrderId, cancellationToken))?.ProjectId ?? Guid.Empty;
        }

        var order = new ReservationOrder(productionOrderId, projectId);
        var context = new ReservationContext(origin, isBackfill, closed);
        foreach (var itemId in targets.Keys.Union(existing.Select(r => r.ItemId)).Distinct().Order())
        {
            await allocator.LockItemAsync(itemId, cancellationToken);
            var line = bom is null ? null : new ReservationLineSource(lines.FirstOrDefault(l => l.ItemId == itemId)?.BomLineId, bom.Revision, bom.ReleasedAt);
            await allocator.ReconcileAsync(order, itemId, targets.GetValueOrDefault(itemId), line, context, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        await LockOrderAsync(productionOrderId, cancellationToken);
        var rows = await db.Reservations.AsNoTracking().Where(r => r.ProductionOrderId == productionOrderId && r.Status == ReservationState.Active)
            .Select(r => new { r.ItemId, r.ProjectId }).ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return;
        }

        var order = new ReservationOrder(productionOrderId, rows[0].ProjectId);
        foreach (var itemId in rows.Select(r => r.ItemId).Distinct().Order())
        {
            await allocator.LockItemAsync(itemId, cancellationToken);
            await allocator.ReconcileAsync(order, itemId, 0m, null, new ReservationContext(ReservationOrigins.Completion, OrderClosed: true), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StockReservationInfo>> ForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default)
    {
        var rows = await db.Reservations.AsNoTracking().Where(r => r.ProductionOrderId == productionOrderId).OrderBy(r => r.ItemId).ThenBy(r => r.LocationId).ToListAsync(cancellationToken);
        return rows.Select(Info).ToList();
    }

    public async Task<IReadOnlyList<Guid>> OrdersCompletedByEntriesAsync(IReadOnlyCollection<string> movementNumbers, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(movementNumbers);
        if (movementNumbers.Count == 0)
        {
            return [];
        }

        var origins = movementNumbers.Distinct().Select(ReservationOrigins.Entry).ToList();
        return await db.ReservationChanges.AsNoTracking().Where(c => c.Kind == ReservationChangeKind.Completed && c.QuantityDelta > 0 && origins.Contains(c.Origin))
            .Select(c => c.ProductionOrderId).Distinct().ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StockShortageInfo>> OpenShortagesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await ShortRowsAsync(cancellationToken);
        return rows.GroupBy(r => (r.ProductionOrderId, r.ItemId))
            .Select(g => new StockShortageInfo(g.Key.ProductionOrderId, g.First().ProjectId, g.Key.ItemId, g.Select(r => r.BomLineId).FirstOrDefault(id => id is not null), g.Sum(r => r.Shortage), g.Min(r => r.BomReleasedAt)))
            .OrderBy(s => s.BomReleasedAt is null ? 1 : 0).ThenBy(s => s.BomReleasedAt).ThenBy(s => s.ProductionOrderId).ThenBy(s => s.ItemId).ToList();
    }

    /// <summary>Aba Material (CA 14): 404 se a OS não existe ou não é de fabricação.</summary>
    public async Task<Result<OrderMaterialView>> MaterialAsync(Guid productionOrderId, CancellationToken cancellationToken = default)
    {
        var status = await orders.StatusAsync(productionOrderId, cancellationToken);
        if (status is null)
        {
            return OrderNotFound();
        }

        var closed = status is ProductionOrderState.Cancelled or ProductionOrderState.Completed;
        var bom = await boms.ReleasedForOrderAsync(productionOrderId, cancellationToken);
        var rows = await db.Reservations.AsNoTracking().Where(r => r.ProductionOrderId == productionOrderId).ToListAsync(cancellationToken);
        var bomItems = bom?.Lines.Select(l => l.ItemId).ToList() ?? [];
        var itemIds = rows.Select(r => r.ItemId).Union(bomItems).Distinct().ToList();
        var items = await ItemNamesAsync(itemIds, cancellationToken);
        var locations = await LocationNamesAsync(rows.Select(r => r.LocationId).Distinct().ToList(), cancellationToken);
        var stockItems = await StockItemsAsync(bomItems.Distinct().ToList(), cancellationToken);
        var pendingItems = closed || bom is null ? [] : bom.Lines.Where(l => stockItems.Contains(l.ItemId)).Select(l => l.ItemId).Distinct().ToList();
        var requestLinks = (await purchaseRequests.ForOrderAsync(productionOrderId, cancellationToken)).GroupBy(r => r.ItemId)
            .ToDictionary(g => g.Key, g => PurchaseRequestLink(g.Select(r => r.RequestId).Distinct().ToList(), g.Key));
        var reconciling = pendingItems.Count > 0
            && (pendingItems.Any(i => rows.All(r => r.ItemId != i)) || rows.Any(r => r.Status == ReservationState.Active && r.BomRevision < bom!.Revision));
        var lines = itemIds
            .Select(itemId =>
            {
                var mine = rows.Where(r => r.ItemId == itemId).ToList();
                var required = mine.Count > 0 && !reconciling ? mine.Sum(r => r.RequestedQuantity) : bom?.Lines.Where(l => l.ItemId == itemId).Sum(l => l.RequiredQty) ?? 0m;
                var issued = mine.Sum(r => r.QuantityIssued);
                var name = items.GetValueOrDefault(itemId);
                var bomRequired = closed ? 0m : bom?.Lines.Where(l => l.ItemId == itemId).Sum(l => l.RequiredQty) ?? 0m;
                return new OrderMaterialLine(
                    itemId, name?.Code ?? string.Empty, name?.Name ?? "Item removido", name?.Unit ?? string.Empty,
                    bom?.Lines.FirstOrDefault(l => l.ItemId == itemId)?.BomLineId ?? mine.Select(r => r.BomLineId).FirstOrDefault(id => id is not null),
                    required, mine.Sum(r => r.Quantity), issued, mine.Sum(r => r.Shortage), Math.Max(0m, issued - bomRequired), mine.Any(r => r.IsBackfill),
                    mine.OrderBy(r => locations.GetValueOrDefault(r.LocationId)?.Code, StringComparer.Ordinal).Select(r =>
                    {
                        var location = locations.GetValueOrDefault(r.LocationId);
                        return new OrderMaterialLocation(r.Id, r.LocationId, location?.Code ?? string.Empty, location?.Name ?? "Local removido", r.RequestedQuantity, r.Quantity, r.QuantityIssued, r.Shortage, r.Status);
                    }).ToList(),
                    requestLinks.GetValueOrDefault(itemId));
            })
            .Where(l => l.Required > 0 || l.Reserved > 0 || l.Issued > 0 || bomItems.Contains(l.ItemId))
            .OrderBy(l => l.ItemCode, StringComparer.Ordinal).ToList();
        return new OrderMaterialView(
            productionOrderId, bom is not null, bom?.Revision, bom?.ReleasedAt, reconciling, closed, lines.Sum(l => l.Shortage), lines.Sum(l => l.OverIssued), lines);
    }

    /// <summary>Reserva pela OS (CA 17, IDOR): reserva de outra OS responde como inexistente.</summary>
    public async Task<Result<ReservationDetail>> ReservationAsync(Guid productionOrderId, Guid reservationId, CancellationToken cancellationToken = default)
    {
        var row = await db.Reservations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reservationId && r.ProductionOrderId == productionOrderId, cancellationToken);
        if (row is null)
        {
            return Error.NotFound("reservation_not_found", "Reserva não encontrada nesta OS.");
        }

        var item = (await ItemNamesAsync([row.ItemId], cancellationToken)).GetValueOrDefault(row.ItemId);
        var location = (await LocationNamesAsync([row.LocationId], cancellationToken)).GetValueOrDefault(row.LocationId);
        return new ReservationDetail(
            row.Id, row.ProductionOrderId, row.ItemId, item?.Code ?? string.Empty, item?.Name ?? "Item removido", item?.Unit ?? string.Empty, row.LocationId, location?.Code ?? string.Empty,
            location?.Name ?? "Local removido", row.RequestedQuantity, row.Quantity, row.QuantityIssued, row.Shortage, row.Status, row.BomRevision, row.Origin, row.IsBackfill, row.UpdatedAt);
    }

    /// <summary>Histórico da reserva da OS (CA 18), do mais recente para o mais antigo.</summary>
    public async Task<Result<PagedList<ReservationChangeRow>>> OrderHistoryAsync(Guid productionOrderId, PageRequest page, CancellationToken cancellationToken = default)
    {
        if (await orders.StatusAsync(productionOrderId, cancellationToken) is null)
        {
            return OrderNotFound();
        }

        return await HistoryAsync(db.ReservationChanges.AsNoTracking().Where(c => c.ProductionOrderId == productionOrderId), page, cancellationToken);
    }

    /// <summary>Histórico das reservas do item (CA 18), de todas as OS.</summary>
    public async Task<Result<PagedList<ReservationChangeRow>>> ItemHistoryAsync(Guid itemId, PageRequest page, CancellationToken cancellationToken = default)
    {
        if (!await db.Items.AnyAsync(i => i.Id == itemId, cancellationToken))
        {
            return Error.NotFound("item_not_found", "Item não encontrado.");
        }

        return await HistoryAsync(db.ReservationChanges.AsNoTracking().Where(c => c.ItemId == itemId), page, cancellationToken);
    }

    /// <summary>
    /// Lista Faltas (CA 15): por item, a falta somada das OS abertas, com as OS, os projetos e a data prometida mais próxima; filtros por
    /// texto (código ou nome do item), projeto e item. Ordem: a data prometida mais próxima primeiro (sem data por último), depois o
    /// código do item.
    /// </summary>
    public async Task<PagedList<ShortageRow>> ShortagesAsync(ShortageListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);
        var rows = await ShortRowsAsync(cancellationToken);
        if (query.ProjectId is { } project)
        {
            rows = rows.Where(r => r.ProjectId == project).ToList();
        }

        if (query.ItemId is { } onlyItem)
        {
            rows = rows.Where(r => r.ItemId == onlyItem).ToList();
        }

        var itemIds = rows.Select(r => r.ItemId).Distinct().ToList();
        var items = await ItemNamesAsync(itemIds, cancellationToken);
        if (!string.IsNullOrWhiteSpace(query.Term))
        {
            var term = SearchText.Normalize(query.Term);
            itemIds = itemIds.Where(i => items.GetValueOrDefault(i) is { } n && n.Search.Contains(term, StringComparison.Ordinal)).ToList();
        }

        var orderIds = rows.Where(r => itemIds.Contains(r.ItemId)).Select(r => r.ProductionOrderId).Distinct().ToList();
        var orderInfo = (await orders.FindManyAsync(orderIds, cancellationToken)).ToDictionary(o => o.Id);
        var projectInfo = (await projects.FindManyAsync(rows.Select(r => r.ProjectId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id);
        var available = await db.StockPositions.AsNoTracking().Where(p => itemIds.Contains(p.ItemId)).GroupBy(p => p.ItemId)
            .Select(g => new { g.Key, Available = g.Sum(p => p.QuantityOnHand - p.QuantityReserved) }).ToDictionaryAsync(x => x.Key, x => x.Available, cancellationToken);
        var requests = (await purchaseRequests.ForOrdersAsync(orderIds, cancellationToken)).GroupBy(r => r.ItemId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(r => r.RequestId).Distinct().ToList());
        var result = itemIds.Select(itemId =>
            {
                var name = items.GetValueOrDefault(itemId);
                var byOrder = rows.Where(r => r.ItemId == itemId).GroupBy(r => r.ProductionOrderId)
                    .Select(g =>
                    {
                        var projectId = g.First().ProjectId;
                        var projectSummary = projectInfo.GetValueOrDefault(projectId);
                        return new ShortageOrder(g.Key, orderInfo.GetValueOrDefault(g.Key)?.Number, projectId, projectSummary?.Number, g.Sum(r => r.Shortage), projectSummary?.PromisedDate, g.Any(r => r.IsBackfill));
                    })
                    .OrderBy(o => o.PromisedDate is null ? 1 : 0).ThenBy(o => o.PromisedDate).ThenBy(o => o.OrderNumber, StringComparer.Ordinal).ToList();
                return new ShortageRow(
                    itemId, name?.Code ?? string.Empty, name?.Name ?? "Item removido", name?.Unit ?? string.Empty, byOrder.Sum(o => o.ShortQty), available.GetValueOrDefault(itemId),
                    byOrder.Select(o => o.PromisedDate).Where(d => d is not null).Min(), byOrder,
                    requests.TryGetValue(itemId, out var itemRequests) ? PurchaseRequestLink(itemRequests, itemId) : null);
            })
            .OrderBy(r => r.NearestPromisedDate is null ? 1 : 0).ThenBy(r => r.NearestPromisedDate).ThenBy(r => r.ItemCode, StringComparer.Ordinal).ToList();
        var normalized = page.Normalize();
        return new PagedList<ShortageRow>(result.Skip(normalized.Skip).Take(normalized.PageSize).ToList(), normalized.Page, normalized.PageSize, result.Count);
    }

    /// <summary>
    /// Link "SC gerada" (CMP-01 CA 15): a solicitação, quando o item tem uma só nas OS; a lista filtrada pelo item, quando tem mais.
    /// </summary>
    private static string PurchaseRequestLink(IReadOnlyList<Guid> requestIds, Guid itemId) =>
        requestIds.Count == 1 ? "/app/compras/solicitacoes/" + requestIds[0] : "/app/compras/solicitacoes?item=" + itemId + "&filtro=todas";

    private static StockReservationInfo Info(Reservation r) =>
        new(r.Id, r.ProductionOrderId, r.ItemId, r.LocationId, r.BomLineId, r.BomRevision, r.RequestedQuantity, r.Quantity, r.QuantityIssued, r.Shortage, r.Status, r.IsBackfill);

    private static Error OrderNotFound() => Error.NotFound("production_order_not_found", "OS de fabricação não encontrada.");

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A reserva exige transação aberta: o chamador é dono da transação (ADR 0023 §3).");
        }
    }

    /// <summary>Uma reconciliação por OS de cada vez (fonte lida e gravada sob a mesma trava).</summary>
    internal async Task LockOrderAsync(Guid productionOrderId, CancellationToken ct)
    {
        var key = "inventory.reservations:" + productionOrderId.ToString("N");
        await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))) AS l").ToListAsync(ct);
    }

    /// <summary>Itens que têm estoque (serviço não reserva).</summary>
    private async Task<HashSet<Guid>> StockItemsAsync(List<Guid> itemIds, CancellationToken ct) =>
        itemIds.Count == 0
            ? []
            : (await db.Items.IgnoreQueryFilters().AsNoTracking().Where(i => itemIds.Contains(i.Id) && i.Type != ItemType.Service).Select(i => i.Id).ToListAsync(ct)).ToHashSet();

    /// <summary>Linhas ativas com falta (a falta mora numa linha por OS e item, a do local padrão).</summary>
    private Task<List<Reservation>> ShortRowsAsync(CancellationToken ct) =>
        db.Reservations.AsNoTracking().Where(r => r.Status == ReservationState.Active && r.RequestedQuantity > r.Quantity + r.QuantityIssued).ToListAsync(ct);

    private async Task<PagedList<ReservationChangeRow>> HistoryAsync(IQueryable<ReservationChange> changes, PageRequest page, CancellationToken ct)
    {
        var result = await changes.OrderByDescending(c => c.At).ThenByDescending(c => c.Id).ToPagedListAsync(page, ct);
        var items = await ItemNamesAsync(result.Items.Select(c => c.ItemId).Distinct().ToList(), ct);
        var locations = await LocationNamesAsync(result.Items.Select(c => c.LocationId).Distinct().ToList(), ct);
        var numbers = (await orders.FindManyAsync(result.Items.Select(c => c.ProductionOrderId).Distinct().ToList(), ct)).ToDictionary(o => o.Id, o => o.Number);
        var rows = result.Items.Select(c =>
        {
            var item = items.GetValueOrDefault(c.ItemId);
            return new ReservationChangeRow(
                c.Id, c.ProductionOrderId, numbers.GetValueOrDefault(c.ProductionOrderId), c.ItemId, item?.Code ?? string.Empty, item?.Name ?? "Item removido", item?.Unit ?? string.Empty,
                c.LocationId, locations.GetValueOrDefault(c.LocationId)?.Code ?? string.Empty, c.Kind, c.QuantityDelta, c.QuantityAfter, c.RequestedAfter, c.ShortageAfter, c.Origin,
                c.IsBackfill, c.At);
        }).ToList();
        return new PagedList<ReservationChangeRow>(rows, result.Page, result.PageSize, result.Total);
    }

    private sealed record ItemName(string Code, string Name, string Unit, string Search);

    private sealed record LocationName(string Code, string Name);

    private async Task<Dictionary<Guid, ItemName>> ItemNamesAsync(List<Guid> ids, CancellationToken ct) =>
        ids.Count == 0
            ? []
            : await (from i in db.Items.IgnoreQueryFilters().AsNoTracking()
                     join u in db.Units.IgnoreQueryFilters().AsNoTracking() on i.UnitId equals u.Id
                     where ids.Contains(i.Id)
                     select new { i.Id, i.Code, i.Name, Unit = u.Code, i.SearchText }).ToDictionaryAsync(x => x.Id, x => new ItemName(x.Code, x.Name, x.Unit, x.SearchText), ct);

    private async Task<Dictionary<Guid, LocationName>> LocationNamesAsync(List<Guid> ids, CancellationToken ct) =>
        ids.Count == 0
            ? []
            : await db.Locations.IgnoreQueryFilters().AsNoTracking().Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => new LocationName(l.Code, l.Name), ct);
}
