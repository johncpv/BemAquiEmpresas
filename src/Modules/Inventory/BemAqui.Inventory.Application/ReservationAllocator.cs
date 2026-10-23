using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>OS dona da reserva: o projeto vai no aviso de falta.</summary>
public sealed record ReservationOrder(Guid ProductionOrderId, Guid ProjectId);

/// <summary>Linha da BOM vigente que pede o item: id da linha, revisão e quando a BOM foi liberada.</summary>
public sealed record ReservationLineSource(Guid? BomLineId, int Revision, DateTimeOffset? ReleasedAt);

/// <summary>
/// Por quem a reserva mudou (CA 18): a origem legível, a marca da recomposição (D3) e se a OS está fechada (cancelada ou
/// concluída: a reserva fica <c>Released</c>).
/// </summary>
public sealed record ReservationContext(string Origin, bool IsBackfill = false, bool OrderClosed = false);

/// <summary>
/// Quem escreve a reserva e o reservado da posição (EST-02, ADR 0023 §2, §3 e §6). O chamador já travou a linha do item
/// (<see cref="LockItemAsync"/>, a mesma trava do motor, em ordem crescente de <c>item_id</c>); aqui a posição do item é travada
/// (<c>SELECT … FOR UPDATE</c>), o plano puro (<see cref="ReservationPlanner"/>) leva a reserva até o alvo e o
/// <c>quantity_reserved</c> da posição muda na mesma transação. As reservas de um item só são lidas e mudadas com a trava do item:
/// duas reconciliações que disputam o mesmo saldo ficam em fila e a soma reservada nunca passa do físico (CA 7). Grava o histórico
/// (CA 18) e publica <see cref="StockShortageChanged"/> quando a falta da OS no item muda (só aviso, no outbox: sai depois do commit).
/// Não salva: quem chama é dono da transação e do <c>SaveChanges</c>.
/// </summary>
public sealed class ReservationAllocator(IInventoryDb db, IEventPublisher events, TimeProvider clock)
{
    /// <summary>Trava a linha do item, inclusive excluído (a reserva de item excluído ainda é liberada).</summary>
    public async Task LockItemAsync(Guid itemId, CancellationToken cancellationToken = default) =>
        await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM inventory.items WHERE id = {itemId} FOR UPDATE").ToListAsync(cancellationToken);

    /// <summary>Leva as reservas da OS no item até o alvo (CA 1, 3 a 6, 8 e 9). O que for liberado completa as faltas de outras OS.</summary>
    public async Task ReconcileAsync(
        ReservationOrder order, Guid itemId, decimal target, ReservationLineSource? line, ReservationContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(context);
        var positions = await LockPositionsAsync(itemId, cancellationToken);
        var rows = await RowsAsync(itemId, order.ProductionOrderId, cancellationToken);
        var (priority, usable) = await PriorityAsync(positions, rows, cancellationToken);
        var shortageLocation = await DefaultLocationAsync(cancellationToken) ?? priority.FirstOrDefault();
        if (shortageLocation == Guid.Empty)
        {
            return;
        }

        var available = positions.Where(p => usable.Contains(p.LocationId)).ToDictionary(p => p.LocationId, p => p.QuantityOnHand - p.QuantityReserved);
        var plan = ReservationPlanner.Plan(target, Slots(rows), priority, available, shortageLocation);
        var freed = Apply(order, itemId, rows, plan, positions, line, context, completing: false);
        foreach (var location in freed)
        {
            await CompleteLockedAsync(itemId, location, null, order.ProductionOrderId, context with { OrderClosed = false, IsBackfill = false }, positions, cancellationToken);
        }
    }

    /// <summary>
    /// Material novo no local (CA 11, pontos 28 e 29): completa as faltas do item, primeiro a OS que originou a compra e depois a
    /// BOM liberada há mais tempo, até acabar o disponível do local.
    /// </summary>
    public async Task CompleteAsync(Guid itemId, Guid locationId, Guid? originOrderId, string origin, CancellationToken cancellationToken = default)
    {
        var positions = await LockPositionsAsync(itemId, cancellationToken);
        await CompleteLockedAsync(itemId, locationId, originOrderId, null, new ReservationContext(origin), positions, cancellationToken);
    }

    private async Task CompleteLockedAsync(
        Guid itemId, Guid locationId, Guid? originOrderId, Guid? excludeOrderId, ReservationContext context, List<StockPosition> positions, CancellationToken ct)
    {
        var position = positions.FirstOrDefault(p => p.LocationId == locationId);
        if (position is null || position.QuantityOnHand - position.QuantityReserved <= 0)
        {
            return;
        }

        var waiting = await db.Reservations.AsNoTracking()
            .Where(r => r.ItemId == itemId && r.Status == ReservationState.Active && r.RequestedQuantity > r.Quantity + r.QuantityIssued)
            .Select(r => new { r.ProductionOrderId, r.ProjectId, r.BomReleasedAt }).ToListAsync(ct);
        var projects = waiting.GroupBy(r => r.ProductionOrderId).ToDictionary(g => g.Key, g => g.First().ProjectId);
        var order = ReservationPlanner.CompletionOrder(waiting.Where(r => r.ProductionOrderId != excludeOrderId).Select(r => (r.ProductionOrderId, r.BomReleasedAt)), originOrderId);
        var defaultLocation = await DefaultLocationAsync(ct);
        foreach (var orderId in order)
        {
            var available = position.QuantityOnHand - position.QuantityReserved;
            if (available <= 0)
            {
                return;
            }

            var rows = await RowsAsync(itemId, orderId, ct);
            var target = rows.Sum(r => r.RequestedQuantity);
            var shortageAt = rows.FirstOrDefault(r => r.Shortage > 0)?.LocationId ?? defaultLocation ?? locationId;
            var plan = ReservationPlanner.Plan(target, Slots(rows), [locationId], new Dictionary<Guid, decimal> { [locationId] = available }, shortageAt);
            Apply(new ReservationOrder(orderId, projects[orderId]), itemId, rows, plan, positions, null, context, completing: true);
        }
    }

    /// <summary>Aplica o plano: reservas, posição, histórico e aviso; devolve os locais em que sobrou disponível (liberação).</summary>
    private List<Guid> Apply(
        ReservationOrder order, Guid itemId, List<Reservation> rows, ReservationPlan plan, List<StockPosition> positions, ReservationLineSource? line,
        ReservationContext context, bool completing)
    {
        var before = rows.Sum(r => r.Shortage);
        var now = clock.GetUtcNow();
        var freed = new List<Guid>();
        foreach (var slot in plan.Slots)
        {
            var row = rows.FirstOrDefault(r => r.LocationId == slot.LocationId);
            if (row is null)
            {
                if (slot.Requested == 0 && slot.Quantity == 0)
                {
                    continue;
                }

                row = new Reservation
                {
                    ItemId = itemId, LocationId = slot.LocationId, ProductionOrderId = order.ProductionOrderId, ProjectId = order.ProjectId, Status = ReservationState.Released,
                    CreatedAt = now, UpdatedAt = now, IsBackfill = context.IsBackfill, Origin = context.Origin,
                };
                db.Reservations.Add(row);
                rows.Add(row);
            }

            var delta = slot.Quantity - row.Quantity;
            var status = Status(slot, context.OrderClosed);
            if (line is not null)
            {
                row.BomLineId = line.BomLineId ?? row.BomLineId;
                row.BomRevision = line.Revision;
                row.BomReleasedAt = line.ReleasedAt;
            }

            if (delta == 0 && slot.Requested == row.RequestedQuantity && status == row.Status)
            {
                continue;
            }

            if (delta != 0)
            {
                positions.First(p => p.LocationId == slot.LocationId).ApplyReserved(delta);
                if (delta < 0)
                {
                    freed.Add(slot.LocationId);
                }
            }

            row.ProjectId = order.ProjectId;
            row.RequestedQuantity = slot.Requested;
            row.Quantity = slot.Quantity;
            row.Status = status;
            row.Origin = context.Origin;
            row.IsBackfill = context.IsBackfill;
            row.UpdatedAt = now;
            db.ReservationChanges.Add(new ReservationChange
            {
                ReservationId = row.Id, ProductionOrderId = order.ProductionOrderId, ItemId = itemId, LocationId = row.LocationId,
                Kind = delta > 0 ? (completing ? ReservationChangeKind.Completed : ReservationChangeKind.Reserved) : delta < 0 ? ReservationChangeKind.Released : ReservationChangeKind.Requested,
                QuantityDelta = delta, QuantityAfter = row.Quantity, RequestedAfter = row.RequestedQuantity, ShortageAfter = row.Shortage, Origin = context.Origin,
                IsBackfill = context.IsBackfill, At = now,
            });
        }

        var after = rows.Sum(r => r.Shortage);
        if (after != before)
        {
            var lineId = line?.BomLineId ?? rows.Select(r => r.BomLineId).FirstOrDefault(id => id is not null);
            events.Publish(new StockShortageChanged(itemId, order.ProductionOrderId, order.ProjectId, lineId, after));
        }

        return freed;
    }

    /// <summary>
    /// Situação depois do plano: com reservado ou falta, <c>Active</c>; senão <c>Released</c> (OS fechada ou alvo zerado), ou
    /// <c>Consumed</c> quando tudo o que pedia já foi baixado (EST-03).
    /// </summary>
    private static ReservationState Status(ReservationSlot slot, bool orderClosed)
    {
        if (slot.Quantity > 0 || slot.Requested > slot.Quantity + slot.Issued)
        {
            return ReservationState.Active;
        }

        return !orderClosed && slot.Issued > 0 ? ReservationState.Consumed : ReservationState.Released;
    }

    private static List<ReservationSlot> Slots(IEnumerable<Reservation> rows) =>
        rows.Select(r => new ReservationSlot(r.LocationId, r.RequestedQuantity, r.Quantity, r.QuantityIssued)).ToList();

    /// <summary>A posição do item em todos os locais, travada; a linha já rastreada sem mudança é relida (ADR 0019 §2).</summary>
    private async Task<List<StockPosition>> LockPositionsAsync(Guid itemId, CancellationToken ct)
    {
        var tracked = db.StockPositions.Local.Where(p => p.ItemId == itemId).ToHashSet();
        var positions = await db.StockPositions.FromSql($"SELECT * FROM inventory.stock_positions WHERE item_id = {itemId} ORDER BY location_id FOR UPDATE").ToListAsync(ct);
        foreach (var position in positions.Where(p => tracked.Contains(p) && db.Entry(p).State == EntityState.Unchanged))
        {
            await db.Entry(position).ReloadAsync(ct);
        }

        return positions;
    }

    private async Task<List<Reservation>> RowsAsync(Guid itemId, Guid orderId, CancellationToken ct)
    {
        var tracked = db.Reservations.Local.Where(r => r.ItemId == itemId && r.ProductionOrderId == orderId).ToHashSet();
        var rows = await db.Reservations.Where(r => r.ItemId == itemId && r.ProductionOrderId == orderId).ToListAsync(ct);
        foreach (var row in rows.Where(r => tracked.Contains(r) && db.Entry(r).State == EntityState.Unchanged))
        {
            await db.Entry(row).ReloadAsync(ct);
        }

        return rows.Union(tracked.Where(r => db.Entry(r).State == EntityState.Added)).ToList();
    }

    /// <summary>Prioridade dos locais (ponto 27, CA 6): o padrão primeiro, depois os demais ativos em ordem de código.</summary>
    private async Task<(List<Guid> Priority, HashSet<Guid> Usable)> PriorityAsync(List<StockPosition> positions, List<Reservation> rows, CancellationToken ct)
    {
        var ids = positions.Select(p => p.LocationId).Union(rows.Select(r => r.LocationId)).ToList();
        var locations = await db.Locations.IgnoreQueryFilters().AsNoTracking().Where(l => ids.Contains(l.Id))
            .Select(l => new { l.Id, l.Code, l.IsDefault, Usable = l.IsActive && !l.IsDeleted }).ToListAsync(ct);
        var priority = locations.OrderBy(l => l.IsDefault && l.Usable ? 0 : l.Usable ? 1 : 2).ThenBy(l => l.Code, StringComparer.Ordinal).Select(l => l.Id).ToList();
        return (priority, locations.Where(l => l.Usable).Select(l => l.Id).ToHashSet());
    }

    private async Task<Guid?> DefaultLocationAsync(CancellationToken ct) =>
        await db.Locations.AsNoTracking().Where(l => l.IsDefault && l.IsActive).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(ct);
}
