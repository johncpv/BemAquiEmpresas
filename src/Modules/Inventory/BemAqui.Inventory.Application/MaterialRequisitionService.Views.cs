using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

public sealed partial class MaterialRequisitionService
{
    /// <summary>
    /// Diálogo "Requisitar material" (CA 1): uma linha por reserva ativa com reservado, com todo o reservado a baixar, o disponível livre
    /// no local, o custo médio do momento e, no item com lote, os lotes do local e a divisão sugerida (validade mais próxima, depois o
    /// mais antigo pela entrada).
    /// </summary>
    public async Task<Result<RequisitionDraft>> DraftAsync(Guid productionOrderId, CancellationToken cancellationToken = default)
    {
        if (await orders.FindAsync(productionOrderId, cancellationToken) is not { } order)
        {
            return OrderNotFound();
        }

        var project = await projects.FindAsync(order.ProjectId, cancellationToken);
        var rows = await db.Reservations.AsNoTracking().Where(r => r.ProductionOrderId == productionOrderId && r.Status == ReservationState.Active && r.Quantity > 0).ToListAsync(cancellationToken);
        var lines = new List<RequisitionDraftLine>();
        foreach (var row in rows)
        {
            var stock = await StockAsync(row.ItemId, row.LocationId, cancellationToken);
            if (stock is null)
            {
                continue;
            }

            var (item, location) = stock.Value;
            var picks = item.TracksLot ? RequisitionRules.SplitByLots(location.Lots.Select((l, i) => ToStock(l, i)), row.Quantity).Select(p => new RequisitionLotPick(p.LotId, p.LotNumber, p.Quantity, p.Suggested)).ToList() : [];
            lines.Add(new RequisitionDraftLine(
                row.Id, item.ItemId, item.ItemCode, item.ItemName, item.UnitCode, item.TracksLot, location.LocationId, location.LocationCode, location.LocationName, row.Quantity,
                location.OnHand, location.Available, item.AverageCost, location.Lots, picks));
        }

        var limit = (await db.InventoryParameters.AsNoTracking().FirstOrDefaultAsync(cancellationToken))?.ManualApprovalAmount ?? InventoryParameters.DefaultApprovalAmount;
        var bomReleased = await db.Reservations.AsNoTracking().AnyAsync(r => r.ProductionOrderId == productionOrderId && r.BomRevision > 0, cancellationToken);
        return new RequisitionDraft(
            productionOrderId, order.Number, order.ProjectId, project?.Number, order.Status is ProductionOrderState.Cancelled or ProductionOrderState.Completed, bomReleased, limit,
            lines.OrderBy(l => l.ItemCode, StringComparer.Ordinal).ThenBy(l => l.LocationCode, StringComparer.Ordinal).ToList());
    }

    /// <summary>Saldo do item por local para o "Material extra" (CA 3): disponível livre, lotes na ordem da sugestão e o custo médio.</summary>
    public async Task<Result<RequisitionItemStock>> ItemStockAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var item = await ItemHeadAsync(itemId, cancellationToken);
        if (item is null)
        {
            return Error.NotFound("item_not_found", "Item não encontrado.");
        }

        var locations = await db.Locations.AsNoTracking().Where(l => l.IsActive).OrderByDescending(l => l.IsDefault).ThenBy(l => l.Code).ToListAsync(cancellationToken);
        var result = new List<RequisitionItemLocation>();
        foreach (var location in locations)
        {
            var stock = await StockAsync(itemId, location.Id, cancellationToken);
            if (stock is { } found && (found.Location.OnHand > 0 || location.IsDefault))
            {
                result.Add(found.Location);
            }
        }

        return item with { Locations = result };
    }

    /// <summary>Requisições (padrão 5): "Aguardando você" junta os pedidos dos operadores (com <c>inventory.create</c>) e as requisições aguardando aprovação (com <c>inventory.approve</c>, de outra pessoa).</summary>
    public async Task<PagedList<RequisitionSummary>> ListAsync(RequisitionListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var me = currentUser.UserId;
        var canCreate = await permissions.HasPermissionAsync("inventory", "create", cancellationToken);
        var canApprove = await permissions.HasPermissionAsync("inventory", "approve", cancellationToken);
        var rows = db.MaterialRequisitions.AsNoTracking();
        if (query.Status is { } status)
        {
            rows = rows.Where(r => r.Status == status);
        }

        if (query.Kind is { } kind)
        {
            rows = rows.Where(r => r.Kind == kind);
        }

        if (query.ProductionOrderId is { } orderId)
        {
            rows = rows.Where(r => r.ProductionOrderId == orderId);
        }

        if (query.AwaitingMe)
        {
            rows = rows.Where(r => (canCreate && r.Status == MaterialRequisitionStatus.Requested)
                || (canApprove && r.Status == MaterialRequisitionStatus.PendingApproval && (r.CompletedBy ?? r.RequestedBy) != me));
        }

        var paged = await rows.OrderByDescending(r => r.RequestedAt).ThenByDescending(r => r.Id).ToPagedListAsync(page, cancellationToken);
        var items = await SummariesAsync(paged.Items, canCreate, canApprove, cancellationToken);
        return new PagedList<RequisitionSummary>(items, paged.Page, paged.PageSize, paged.Total);
    }

    /// <summary>
    /// "Para separar hoje" (propositivo, padrão 1): OS em aberto com material reservado a baixar, as que têm pedido do operador primeiro,
    /// depois a etapa atual mais adiantada e o número da OS.
    /// </summary>
    public async Task<IReadOnlyList<PickingRow>> ToPickAsync(CancellationToken cancellationToken = default)
    {
        var reserved = await db.Reservations.AsNoTracking().Where(r => r.Status == ReservationState.Active && r.Quantity > 0)
            .GroupBy(r => new { r.ProductionOrderId, r.ProjectId })
            .Select(g => new { g.Key.ProductionOrderId, g.Key.ProjectId, Items = g.Select(r => r.ItemId).Distinct().Count(), Reserved = g.Sum(r => r.Quantity) })
            .ToListAsync(cancellationToken);
        if (reserved.Count == 0)
        {
            return [];
        }

        var orderIds = reserved.Select(r => r.ProductionOrderId).ToList();
        var requests = await db.MaterialRequisitions.AsNoTracking().Where(r => orderIds.Contains(r.ProductionOrderId) && r.Status == MaterialRequisitionStatus.Requested)
            .GroupBy(r => r.ProductionOrderId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var info = (await orders.FindManyAsync(orderIds, cancellationToken)).ToDictionary(o => o.Id);
        var numbers = (await projects.FindManyAsync(reserved.Select(r => r.ProjectId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id, p => p.Number);
        return reserved
            .Where(r => info.TryGetValue(r.ProductionOrderId, out var o) && o.Status is not (ProductionOrderState.Cancelled or ProductionOrderState.Completed) && o.CurrentStepName is not null)
            .Select(r => new PickingRow(r.ProductionOrderId, info[r.ProductionOrderId].Number, r.ProjectId, numbers.GetValueOrDefault(r.ProjectId), info[r.ProductionOrderId].CurrentStepName, r.Items,
                r.Reserved, requests.GetValueOrDefault(r.ProductionOrderId)))
            .OrderByDescending(r => r.OpenRequests).ThenBy(r => info[r.ProductionOrderId].CurrentStepSequence ?? int.MaxValue).ThenBy(r => r.OrderNumber, StringComparer.Ordinal)
            .Take(50).ToList();
    }

    /// <summary>Detalhe da requisição (CA 17) com o que o usuário pode fazer agora.</summary>
    public async Task<Result<RequisitionDetail>> DetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var requisition = await db.MaterialRequisitions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return requisition is null ? NotFound() : await DetailOfAsync(requisition, cancellationToken);
    }

    /// <summary>Requisição pela OS (CA 15, IDOR): requisição de outra OS responde como inexistente.</summary>
    public async Task<Result<RequisitionDetail>> OrderRequisitionAsync(Guid productionOrderId, Guid id, CancellationToken cancellationToken = default)
    {
        var requisition = await db.MaterialRequisitions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id && r.ProductionOrderId == productionOrderId, cancellationToken);
        return requisition is null ? Error.NotFound("requisition_not_found", "Requisição não encontrada nesta OS.") : await DetailOfAsync(requisition, cancellationToken);
    }

    /// <summary>Linha pela requisição (CA 15, IDOR): linha de outra requisição responde como inexistente.</summary>
    public async Task<Result<RequisitionLineView>> LineAsync(Guid id, Guid lineId, CancellationToken cancellationToken = default)
    {
        var lines = await LineViewsAsync(db.MaterialRequisitionLines.AsNoTracking().Where(l => l.RequisitionId == id && l.Id == lineId), cancellationToken);
        return lines is [var line] ? line : Error.NotFound("requisition_line_not_found", "Linha não encontrada nesta requisição.");
    }

    /// <summary>Requisições da OS na aba Material (CA 12, 13 e 17) e as linhas que ainda podem voltar ao estoque.</summary>
    public async Task<Result<OrderRequisitions>> OrderRequisitionsAsync(Guid productionOrderId, CancellationToken cancellationToken = default)
    {
        if (await orders.StatusAsync(productionOrderId, cancellationToken) is not { } status)
        {
            return OrderNotFound();
        }

        var rows = await db.MaterialRequisitions.AsNoTracking().Where(r => r.ProductionOrderId == productionOrderId).OrderByDescending(r => r.RequestedAt).ThenByDescending(r => r.Id)
            .Take(100).ToListAsync(cancellationToken);
        var canCreate = await permissions.HasPermissionAsync("inventory", "create", cancellationToken);
        var canApprove = await permissions.HasPermissionAsync("inventory", "approve", cancellationToken);
        var summaries = await SummariesAsync(rows, canCreate, canApprove, cancellationToken);
        var issued = (await IssuedLinesAsync(productionOrderId, cancellationToken)).Where(l => l.Returnable > 0).ToList();
        var heads = await LineHeadsAsync(issued.Select(i => i.Line).ToList(), cancellationToken);
        var returnable = issued.Select(i =>
        {
            var head = heads[i.Line.Id];
            return new ReturnableLine(
                i.Line.Id, i.Requisition.Id, i.Requisition.Number, i.Line.ItemId, head.ItemCode, head.ItemName, head.UnitCode, i.Line.LotId, head.LotNumber, i.Line.LocationId,
                head.LocationCode, i.Line.Quantity, i.Returnable, i.UnitCost);
        }).ToList();
        return new OrderRequisitions(
            productionOrderId, status is ProductionOrderState.Cancelled or ProductionOrderState.Completed, status == ProductionOrderState.Cancelled, summaries, returnable);
    }

    /// <summary>Linha Material da aba Custos da OS (CA 10): o custo baixado por item, líquido de devolução e estorno, pelo motor.</summary>
    public async Task<Result<OrderMaterialCostView>> MaterialCostAsync(Guid productionOrderId, CancellationToken cancellationToken = default)
    {
        if (await orders.StatusAsync(productionOrderId, cancellationToken) is null)
        {
            return OrderNotFound();
        }

        var cost = await ledger.CostByOrderAsync(productionOrderId, cancellationToken);
        var ids = cost.Lines.Select(l => l.ItemId).ToList();
        var names = await (from i in db.Items.IgnoreQueryFilters().AsNoTracking()
                           join u in db.Units.IgnoreQueryFilters().AsNoTracking() on i.UnitId equals u.Id
                           where ids.Contains(i.Id)
                           select new { i.Id, i.Code, i.Name, Unit = u.Code }).ToDictionaryAsync(x => x.Id, cancellationToken);
        var items = cost.Lines.Where(l => l.Quantity != 0 || l.Amount != 0)
            .Select(l => new OrderMaterialCostItem(l.ItemId, names.GetValueOrDefault(l.ItemId)?.Code ?? string.Empty, names.GetValueOrDefault(l.ItemId)?.Name ?? "Item removido",
                names.GetValueOrDefault(l.ItemId)?.Unit ?? string.Empty, l.Quantity, l.Amount))
            .OrderBy(l => l.ItemCode, StringComparer.Ordinal).ToList();
        return new OrderMaterialCostView(productionOrderId, items, cost.Total);
    }

    private async Task<RequisitionDetail> DetailOfAsync(MaterialRequisition r, CancellationToken ct)
    {
        var lines = await LineViewsAsync(db.MaterialRequisitionLines.AsNoTracking().Where(l => l.RequisitionId == r.Id), ct);
        var names = await NamesAsync([r.RequestedBy, r.CompletedBy, r.DecidedBy, r.ReversedBy], ct);
        var order = await orders.FindAsync(r.ProductionOrderId, ct);
        var project = await projects.FindAsync(r.ProjectId, ct);
        var me = currentUser.UserId;
        var canCreate = await permissions.HasPermissionAsync("inventory", "create", ct);
        var canApprove = await permissions.HasPermissionAsync("inventory", "approve", ct);
        var author = r.CompletedBy ?? r.RequestedBy;
        var pending = r.Status == MaterialRequisitionStatus.PendingApproval;
        var mine = me is not null && author == me;
        var alone = pending && canApprove && mine && me is { } actor && await SoleOwnerRule.NoOtherEligibleAsync(users, "inventory.approve", actor, [actor], ct);
        string? decideBlocked = !pending ? null
            : !canApprove ? "Quem decide a requisição é quem aprova no Estoque."
            : mine && !alone ? "Quem envia a requisição não aprova a própria." : null;
        string? reverseBlocked = null;
        var canReverse = false;
        if (r.Status == MaterialRequisitionStatus.Completed && r.Kind != MaterialRequisitionKind.Loss)
        {
            var rule = await ReverseRuleAsync(r, await db.MaterialRequisitionLines.AsNoTracking().Where(l => l.RequisitionId == r.Id).ToListAsync(ct), ct);
            canReverse = rule.Error is null;
            reverseBlocked = rule.Error?.Message;
        }

        return new RequisitionDetail(
            r.Id, r.Number, r.Kind, r.Status, r.ProductionOrderId, order?.Number, r.ProjectId, project?.Number, r.StepId, r.StepName, Name(names, r.RequestedBy), r.RequestedAt,
            Name(names, r.CompletedBy), r.CompletedAt, Name(names, r.DecidedBy), r.DecidedAt, r.DecisionComment, r.SelfApproved, r.RejectReason, r.OverBomValue,
            lines.Sum(l => l.Amount ?? 0m), Name(names, r.ReversedBy), r.ReversedAt, r.ReversalReason, r.ReversalSelfApproved, lines,
            r.Status == MaterialRequisitionStatus.Requested && canCreate, pending && canApprove && (!mine || alone), decideBlocked, canReverse, reverseBlocked, r.Version);
    }

    private async Task<List<RequisitionSummary>> SummariesAsync(IReadOnlyList<MaterialRequisition> rows, bool canCreate, bool canApprove, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(r => r.Id).ToList();
        var lines = await (from l in db.MaterialRequisitionLines.AsNoTracking()
                           join i in db.Items.IgnoreQueryFilters().AsNoTracking() on l.ItemId equals i.Id
                           where ids.Contains(l.RequisitionId)
                           orderby l.Sequence
                           select new { l.RequisitionId, l.MovementId, Item = i.Code + " — " + i.Name }).ToListAsync(ct);
        var movementIds = lines.Where(l => l.MovementId is not null).Select(l => l.MovementId!.Value).ToList();
        var amounts = await db.StockMovements.AsNoTracking().Where(m => movementIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => Math.Abs(m.TotalCost), ct);
        var orderNumbers = (await orders.FindManyAsync(rows.Select(r => r.ProductionOrderId).Distinct().ToList(), ct)).ToDictionary(o => o.Id, o => o.Number);
        var projectNumbers = (await projects.FindManyAsync(rows.Select(r => r.ProjectId).Distinct().ToList(), ct)).ToDictionary(p => p.Id, p => p.Number);
        var names = await NamesAsync(rows.Select(r => r.RequestedBy), ct);
        var me = currentUser.UserId;
        return rows.Select(r =>
        {
            var mine = lines.Where(l => l.RequisitionId == r.Id).ToList();
            var awaiting = (canCreate && r.Status == MaterialRequisitionStatus.Requested)
                || (canApprove && r.Status == MaterialRequisitionStatus.PendingApproval && (r.CompletedBy ?? r.RequestedBy) != me);
            return new RequisitionSummary(
                r.Id, r.Number, r.Kind, r.Status, r.ProductionOrderId, orderNumbers.GetValueOrDefault(r.ProductionOrderId), r.ProjectId, projectNumbers.GetValueOrDefault(r.ProjectId),
                r.StepName, mine.Count, mine.FirstOrDefault()?.Item, mine.Sum(l => l.MovementId is { } m ? amounts.GetValueOrDefault(m) : 0m), r.OverBomValue,
                Name(names, r.RequestedBy), r.RequestedAt, r.StepId is not null, awaiting);
        }).ToList();
    }

    private sealed record LineHead(string ItemCode, string ItemName, string UnitCode, string LocationCode, string LocationName, string? LotNumber);

    private async Task<Dictionary<Guid, LineHead>> LineHeadsAsync(IReadOnlyList<MaterialRequisitionLine> lines, CancellationToken ct)
    {
        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var locationIds = lines.Select(l => l.LocationId).Distinct().ToList();
        var lotIds = lines.Where(l => l.LotId is not null).Select(l => l.LotId!.Value).Distinct().ToList();
        var items = await (from i in db.Items.IgnoreQueryFilters().AsNoTracking()
                           join u in db.Units.IgnoreQueryFilters().AsNoTracking() on i.UnitId equals u.Id
                           where itemIds.Contains(i.Id)
                           select new { i.Id, i.Code, i.Name, Unit = u.Code }).ToDictionaryAsync(x => x.Id, ct);
        var locations = await db.Locations.IgnoreQueryFilters().AsNoTracking().Where(l => locationIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var lots = await db.Lots.AsNoTracking().Where(l => lotIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.LotNumber, ct);
        return lines.ToDictionary(l => l.Id, l => new LineHead(
            items.GetValueOrDefault(l.ItemId)?.Code ?? string.Empty, items.GetValueOrDefault(l.ItemId)?.Name ?? "Item removido", items.GetValueOrDefault(l.ItemId)?.Unit ?? string.Empty,
            locations.GetValueOrDefault(l.LocationId)?.Code ?? string.Empty, locations.GetValueOrDefault(l.LocationId)?.Name ?? "Local removido",
            l.LotId is { } lot ? lots.GetValueOrDefault(lot) : null));
    }

    private async Task<List<RequisitionLineView>> LineViewsAsync(IQueryable<MaterialRequisitionLine> query, CancellationToken ct)
    {
        var lines = await query.OrderBy(l => l.Sequence).ToListAsync(ct);
        if (lines.Count == 0)
        {
            return [];
        }

        var heads = await LineHeadsAsync(lines, ct);
        var movementIds = lines.Where(l => l.MovementId is not null).Select(l => l.MovementId!.Value).ToList();
        var movements = await db.StockMovements.AsNoTracking().Where(m => movementIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
        var ids = lines.Select(l => l.Id).ToList();
        var back = await (from l in db.MaterialRequisitionLines.AsNoTracking()
                          join r in db.MaterialRequisitions.AsNoTracking() on l.RequisitionId equals r.Id
                          where l.ReturnsLineId != null && ids.Contains(l.ReturnsLineId.Value) && r.Status == MaterialRequisitionStatus.Completed
                          select new { Of = l.ReturnsLineId!.Value, l.Quantity }).ToListAsync(ct);
        var returned = back.GroupBy(b => b.Of).ToDictionary(g => g.Key, g => g.Sum(b => b.Quantity));
        return lines.Select(l =>
        {
            var head = heads[l.Id];
            var movement = l.MovementId is { } m ? movements.GetValueOrDefault(m) : null;
            return new RequisitionLineView(
                l.Id, l.Sequence, l.ReservationId, l.ItemId, head.ItemCode, head.ItemName, head.UnitCode, l.LocationId, head.LocationCode, head.LocationName, l.LotId, head.LotNumber,
                l.Quantity, l.OverQuantity, l.OverReason, l.IsExtra, l.ReturnsLineId, l.MovementId, movement?.Number, movement?.UnitCost,
                movement is null ? null : Math.Abs(movement.TotalCost),
                movement is not null && movement.Type == StockMovementType.Issue ? RequisitionRules.Returnable(l.Quantity, returned.GetValueOrDefault(l.Id), 0m) : 0m);
        }).ToList();
    }

    /// <summary>Cabeçalho do item e o saldo dele num local: físico, disponível livre (físico − reservado) e os lotes na ordem da sugestão.</summary>
    private async Task<(RequisitionItemStock Item, RequisitionItemLocation Location)?> StockAsync(Guid itemId, Guid locationId, CancellationToken ct)
    {
        var item = await ItemHeadAsync(itemId, ct);
        var location = await db.Locations.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(l => l.Id == locationId, ct);
        if (item is null || location is null)
        {
            return null;
        }

        var position = await db.StockPositions.AsNoTracking().FirstOrDefaultAsync(p => p.ItemId == itemId && p.LocationId == locationId, ct);
        var lots = item.TracksLot
            ? await (from b in db.StockBalances.AsNoTracking()
                     join l in db.Lots.AsNoTracking() on b.LotId equals l.Id
                     where b.ItemId == itemId && b.LocationId == locationId && b.QuantityOnHand > 0
                     select new LotStock(l.Id, l.LotNumber, l.Expiry, l.ReceivedAt ?? l.CreatedAt, b.QuantityOnHand)).ToListAsync(ct)
            : [];
        var ordered = RequisitionRules.SuggestionOrder(lots);
        var options = ordered.Select((l, i) => new RequisitionLotOption(l.LotId, l.LotNumber, l.Expiry, l.OnHand, i == 0)).ToList();
        var onHand = position?.QuantityOnHand ?? 0m;
        return (item, new RequisitionItemLocation(location.Id, location.Code, location.Name, location.IsDefault, onHand, onHand - (position?.QuantityReserved ?? 0m), options));
    }

    private async Task<RequisitionItemStock?> ItemHeadAsync(Guid itemId, CancellationToken ct) =>
        await (from i in db.Items.AsNoTracking()
               join u in db.Units.IgnoreQueryFilters().AsNoTracking() on i.UnitId equals u.Id
               where i.Id == itemId && i.Type != ItemType.Service
               select new RequisitionItemStock(i.Id, i.Code, i.Name, u.Code, i.TracksLot, i.AverageCost, new List<RequisitionItemLocation>())).FirstOrDefaultAsync(ct);

    /// <summary>Os lotes já vêm na ordem da sugestão: a posição vira a "entrada" para a divisão manter a mesma ordem.</summary>
    private static LotStock ToStock(RequisitionLotOption lot, int index) => new(lot.LotId, lot.LotNumber, lot.Expiry, DateTimeOffset.UnixEpoch.AddSeconds(index), lot.OnHand);

    private async Task<Dictionary<Guid, string?>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string?>();
        foreach (var id in ids.Where(i => i is not null).Select(i => i!.Value).Distinct())
        {
            names[id] = await users.DisplayNameAsync(id, ct);
        }

        return names;
    }

    private static string? Name(Dictionary<Guid, string?> names, Guid? id) => id is { } key ? names.GetValueOrDefault(key) : null;
}
