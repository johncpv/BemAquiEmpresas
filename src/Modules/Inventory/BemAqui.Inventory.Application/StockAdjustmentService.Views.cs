using BemAqui.BuildingBlocks.Paging;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

public sealed partial class StockAdjustmentService
{
    /// <summary>"Ajustes" (CA 15): para quem aprova, "Aguardando você" (pendentes de outras pessoas) é o filtro padrão da tela.</summary>
    public async Task<PagedList<StockAdjustmentSummary>> ListAsync(StockAdjustmentListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var me = currentUser.UserId;
        var canApprove = await permissions.HasPermissionAsync("inventory", "approve", cancellationToken);
        var adjustments = db.StockAdjustments.AsNoTracking();
        if (query.Status is { } status)
        {
            adjustments = adjustments.Where(a => a.Status == status);
        }

        if (query.Mode is { } mode)
        {
            adjustments = adjustments.Where(a => a.Mode == mode);
        }

        if (query.LocationId is { } locationId)
        {
            adjustments = adjustments.Where(a => a.LocationId == locationId);
        }

        if (query.AwaitingMe)
        {
            adjustments = canApprove ? adjustments.Where(a => a.Status == StockAdjustmentStatus.Pending && a.RequestedBy != me) : adjustments.Where(_ => false);
        }

        var rows = await adjustments.OrderByDescending(a => a.RequestedAt).ThenByDescending(a => a.Id)
            .Select(a => new
            {
                Adjustment = a,
                Location = db.Locations.IgnoreQueryFilters().Where(l => l.Id == a.LocationId).Select(l => l.Name).FirstOrDefault(),
                Items = db.StockAdjustmentLines.Where(l => l.AdjustmentId == a.Id).Select(l => l.ItemId).Distinct().Count(),
            })
            .ToPagedListAsync(page, cancellationToken);
        var names = await NamesAsync(rows.Items.Select(r => r.Adjustment.RequestedBy), cancellationToken);
        var items = rows.Items.Select(r => new StockAdjustmentSummary(
            r.Adjustment.Id, r.Adjustment.Number, r.Adjustment.LocationId, r.Location ?? "—", r.Adjustment.Mode, r.Adjustment.Origin, r.Adjustment.Status, r.Adjustment.ReversedAt is not null,
            Name(names, r.Adjustment.RequestedBy), r.Adjustment.RequestedAt, r.Items, r.Adjustment.SurplusValue, r.Adjustment.ShortageValue,
            r.Adjustment.SurplusValue + r.Adjustment.ShortageValue, r.Adjustment.SelfApproved || r.Adjustment.ReversalSelfApproved,
            canApprove && r.Adjustment.Status == StockAdjustmentStatus.Pending && r.Adjustment.RequestedBy != me)).ToList();
        return new PagedList<StockAdjustmentSummary>(items, rows.Page, rows.PageSize, rows.Total);
    }

    /// <summary>
    /// Detalhe do ajuste (CA 5, 7, 15 e 17): o que foi enviado e, enquanto pendente, o impacto calculado agora sobre o saldo de agora
    /// (o diálogo mostra "O saldo mudou desde o envio" com os valores novos); depois da aprovação, a diferença aplicada e os movimentos.
    /// </summary>
    public async Task<Result<StockAdjustmentDetail>> DetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var adjustment = await db.StockAdjustments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (adjustment is null)
        {
            return NotFound();
        }

        var location = await db.Locations.IgnoreQueryFilters().AsNoTracking().Where(l => l.Id == adjustment.LocationId).Select(l => new { l.Code, l.Name }).FirstAsync(cancellationToken);
        var pending = adjustment.Status == StockAdjustmentStatus.Pending;
        var (lines, impact) = await LineViewsAsync(adjustment, db.StockAdjustmentLines.AsNoTracking().Where(l => l.AdjustmentId == id), cancellationToken);
        var names = await NamesAsync([adjustment.RequestedBy, adjustment.DecidedBy, adjustment.ReversedBy], cancellationToken);
        var me = currentUser.UserId;
        var canApprove = await permissions.HasPermissionAsync("inventory", "approve", cancellationToken);
        var canAdjust = await permissions.HasPermissionAsync("inventory", "adjust", cancellationToken);
        var involved = await InvolvedAsync(adjustment, cancellationToken);
        var mine = me is { } actor && involved.Contains(actor);
        var approved = adjustment.Status == StockAdjustmentStatus.Approved && adjustment.ReversedAt is null;
        var alone = mine && canApprove && (pending || approved) && await IsDecidingAloneAsync(me!.Value, involved, cancellationToken);
        var canDecide = pending && canApprove && (!mine || alone);
        var canReverse = approved && canApprove && (!mine || alone);
        string? decideBlocked = !pending ? null : !canApprove ? "Quem decide o ajuste é quem aprova no Estoque." : mine && !alone ? OwnMessage(adjustment, me!.Value, "aprova") : null;
        string? reverseBlocked = !approved ? null : !canApprove ? "Quem estorna o ajuste é quem aprova no Estoque." : mine && !alone ? OwnMessage(adjustment, me!.Value, "estorna") : null;
        var authored = me is not null && adjustment.RequestedBy == me;
        var originLink = adjustment.Origin == StockAdjustmentOrigin.InventoryCount && adjustment.OriginId is { } count ? LocationCountLocks.Link + count : null;
        return new StockAdjustmentDetail(
            adjustment.Id, adjustment.Number, adjustment.LocationId, location.Code, location.Name, adjustment.Mode, adjustment.Origin, adjustment.OriginId, adjustment.OriginNumber, originLink,
            adjustment.Reason, StockAdjustmentRules.ReasonLabel(adjustment.Reason), adjustment.ReasonDetail, adjustment.Status, adjustment.RequestedBy, Name(names, adjustment.RequestedBy),
            adjustment.RequestedAt, Name(names, adjustment.DecidedBy), adjustment.DecidedAt, adjustment.DecisionComment, adjustment.SelfApproved, adjustment.CancelReason, adjustment.ReversedAt,
            Name(names, adjustment.ReversedBy), adjustment.ReversalReason, adjustment.ReversalSelfApproved, lines.Select(l => l.ItemId).Distinct().Count(), adjustment.SurplusValue,
            adjustment.ShortageValue, adjustment.SurplusValue + adjustment.ShortageValue, lines, pending ? impact : null, canDecide, pending && authored && canAdjust, canReverse, decideBlocked,
            reverseBlocked, adjustment.Version);
    }

    /// <summary>Linha pelo ajuste (CA 16, IDOR): linha de outro ajuste responde como inexistente.</summary>
    public async Task<Result<StockAdjustmentLineView>> LineAsync(Guid id, Guid lineId, CancellationToken cancellationToken = default)
    {
        var adjustment = await db.StockAdjustments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (adjustment is null)
        {
            return NotFound();
        }

        var (lines, _) = await LineViewsAsync(adjustment, db.StockAdjustmentLines.AsNoTracking().Where(l => l.AdjustmentId == id && l.Id == lineId), cancellationToken);
        return lines is [var line] ? line : Error.NotFound("stock_adjustment_line_not_found", "Linha não encontrada neste ajuste.");
    }

    private async Task<(List<StockAdjustmentLineView> Lines, StockAdjustmentImpact Impact)> LineViewsAsync(StockAdjustment adjustment, IQueryable<StockAdjustmentLine> query, CancellationToken ct)
    {
        var rows = await (from l in query
                          join i in db.Items.IgnoreQueryFilters() on l.ItemId equals i.Id
                          join u in db.Units.IgnoreQueryFilters() on i.UnitId equals u.Id
                          orderby l.Sequence
                          select new
                          {
                              Line = l, ItemCode = i.Code, ItemName = i.Name, i.TracksLot, i.AverageCost, i.MinStock, UnitCode = u.Code,
                              LotNumber = db.Lots.Where(x => x.Id == l.LotId).Select(x => x.LotNumber).FirstOrDefault(),
                              EnteredUnit = db.Units.IgnoreQueryFilters().Where(e => e.Id == l.EnteredUnitId).Select(e => e.Code).FirstOrDefault(),
                              MovementNumber = db.StockMovements.Where(m => m.Id == l.MovementId).Select(m => m.Number).FirstOrDefault(),
                          }).ToListAsync(ct);
        var balances = await BalancesAsync(adjustment.LocationId, rows.Select(r => (r.Line.ItemId, r.Line.LotId)).ToList(), ct);
        var inputs = rows.Select(r => new AdjustmentLineInput(r.Line.ItemId, r.Line.LotId, r.Line.CountedQty, r.Line.DeltaQty)).ToList();
        var fromCount = adjustment.Origin == StockAdjustmentOrigin.InventoryCount;
        var (now, refusal) = StockAdjustmentRules.Evaluate(adjustment.Mode, inputs, balances, rows.Select(r => r.UnitCode).ToList(), fromCount);
        // EST-05: a contagem abaixo do reservado não é recusa; a aprovação reduz as reservas mais recentes e gera a falta.
        var reservationNote = !fromCount ? null : string.Join(" ", rows.Select((r, i) => (r, i)).GroupBy(x => x.r.Line.ItemId).Select(g =>
        {
            var (r, i) = g.First();
            var excess = StockAdjustmentRules.ReservationExcess(now[i].PositionAfter, balances[i].PositionReserved);
            return excess > 0 ? $"{r.ItemCode}: a contagem ficou abaixo do reservado; a aprovação reduz {StockAdjustmentRules.Quantity(excess, r.UnitCode)} das reservas das OS com a BOM liberada mais recentemente e gera a falta." : null;
        }).Where(t => t is not null));
        var itemIds = rows.Select(r => r.Line.ItemId).Distinct().ToList();
        var available = await db.StockPositions.AsNoTracking().Where(p => itemIds.Contains(p.ItemId)).GroupBy(p => p.ItemId)
            .Select(g => new { ItemId = g.Key, Available = g.Sum(p => p.QuantityOnHand - p.QuantityReserved) }).ToDictionaryAsync(x => x.ItemId, x => x.Available, ct);
        var views = rows.Select((r, i) => new StockAdjustmentLineView(
            r.Line.Id, r.Line.Sequence, r.Line.ItemId, r.ItemCode, r.ItemName, r.UnitCode, r.TracksLot, r.Line.LotId, r.LotNumber, r.Line.SystemQtyAtRequest, r.Line.CountedQty,
            r.Line.DeltaQty, r.Line.EnteredQuantity, r.EnteredUnit, r.Line.UnitCostEstimate, StockAdjustmentRules.Value(r.Line.DeltaQty, r.Line.UnitCostEstimate),
            balances[i].PositionOnHand, balances[i].PositionReserved, balances[i].PositionOnHand - balances[i].PositionReserved, now[i].SystemQty, now[i].Delta,
            StockAdjustmentRules.Value(now[i].Delta, r.AverageCost), now[i].PositionAfter, now[i].SystemQty != r.Line.SystemQtyAtRequest || now[i].Delta != r.Line.DeltaQty,
            refusal?.Index == i ? refusal.Message : null, r.Line.SystemQtyAtApproval, r.Line.AppliedDeltaQty, r.Line.UnitCost, r.Line.MovementId, r.MovementNumber,
            r.MinStock > 0 && available.GetValueOrDefault(r.Line.ItemId) < r.MinStock)).ToList();
        var (surplus, shortage) = StockAdjustmentRules.Totals(views.Select((v, i) => (v.DeltaNow, rows[i].AverageCost)));
        var impact = new StockAdjustmentImpact(views.Any(v => v.Changed), surplus, shortage, refusal?.Message, refusal is null ? null : rows[refusal.Index].Line.ItemId,
            string.IsNullOrEmpty(reservationNote) ? null : reservationNote);
        return (views, impact);
    }

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
