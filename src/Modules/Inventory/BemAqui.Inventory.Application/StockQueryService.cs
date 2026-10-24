using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Leituras do estoque (EST-01a CA 24, 26, 28): Saldos pela posição, Movimentos e kardex pela ordem de lançamento, detalhe do
/// movimento com quem pode estornar e o contexto do item para os diálogos (custo médio, lotes, local padrão, limites da D7).
/// </summary>
public sealed class StockQueryService(IInventoryDb db, IProjects projects, IUserDirectory users, ReversePolicy policy)
{
    /// <summary>Saldos (CA 28): físico, reservado, disponível, mínimo, custo médio e situação, com filtros; 50 mil posições em &lt; 1 s.</summary>
    public async Task<PagedList<BalanceRow>> BalancesAsync(BalanceListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rows = from p in db.StockPositions.AsNoTracking()
                   join i in db.Items.IgnoreQueryFilters().AsNoTracking() on p.ItemId equals i.Id
                   join u in db.Units.IgnoreQueryFilters().AsNoTracking() on i.UnitId equals u.Id
                   join l in db.Locations.IgnoreQueryFilters().AsNoTracking() on p.LocationId equals l.Id
                   let itemAvailable = db.StockPositions.Where(x => x.ItemId == p.ItemId).Sum(x => x.QuantityOnHand - x.QuantityReserved)
                   select new
                   {
                       Position = p, Item = i, UnitCode = u.Code, Location = l,
                       Situation = p.QuantityOnHand - p.QuantityReserved <= 0 ? StockSituation.Out
                           : i.MinStock > 0 && itemAvailable < i.MinStock ? StockSituation.Low : StockSituation.Normal,
                   };
        if (query.LocationId is { } location)
        {
            rows = rows.Where(r => r.Position.LocationId == location);
        }

        if (query.CategoryId is { } category)
        {
            rows = rows.Where(r => r.Item.CategoryId == category);
        }

        if (query.Type is { } type)
        {
            rows = rows.Where(r => r.Item.Type == type);
        }

        if (query.Situation is { } situation)
        {
            rows = rows.Where(r => r.Situation == situation);
        }

        if (!string.IsNullOrWhiteSpace(query.Term))
        {
            var text = SearchText.Normalize(query.Term);
            rows = rows.Where(r => r.Item.SearchText.Contains(text));
        }

        return await rows.OrderBy(r => r.Item.Code).ThenBy(r => r.Location.Code)
            .Select(r => new BalanceRow(
                r.Item.Id, r.Item.Code, r.Item.Name, r.UnitCode, r.Item.Type, r.Location.Id, r.Location.Code, r.Location.Name, r.Position.QuantityOnHand,
                r.Position.QuantityReserved, r.Position.QuantityOnHand - r.Position.QuantityReserved, r.Item.MinStock, r.Item.AverageCost, r.Situation))
            .ToPagedListAsync(page, cancellationToken);
    }

    /// <summary>Movimentos (CA 24): do mais recente para o mais antigo pela ordem de lançamento, com os filtros da tela.</summary>
    public async Task<PagedList<MovementRow>> MovementsAsync(MovementListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var movements = db.StockMovements.AsNoTracking();
        if (query.ItemId is { } item)
        {
            movements = movements.Where(m => m.ItemId == item);
        }

        if (query.LocationId is { } location)
        {
            movements = movements.Where(m => m.LocationId == location);
        }

        if (query.Type is { } type)
        {
            movements = movements.Where(m => m.Type == type);
        }

        if (query.From is { } from)
        {
            var start = BusinessTime.StartOfDayUtc(from);
            movements = movements.Where(m => m.At >= start);
        }

        if (query.To is { } to)
        {
            var end = BusinessTime.StartOfDayUtc(to.AddDays(1));
            movements = movements.Where(m => m.At < end);
        }

        if (query.ProjectId is { } project)
        {
            movements = movements.Where(m => m.ProjectId == project);
        }

        if (query.ProductionOrderId is { } order)
        {
            movements = movements.Where(m => m.ProductionOrderId == order);
        }

        if (!string.IsNullOrWhiteSpace(query.SourceType))
        {
            movements = movements.Where(m => m.SourceType == query.SourceType);
        }

        if (query.SourceId is { } source)
        {
            movements = movements.Where(m => m.SourceId == source);
        }

        if (!string.IsNullOrWhiteSpace(query.Origin))
        {
            movements = movements.Where(m => m.Origin == query.Origin);
        }

        if (!string.IsNullOrWhiteSpace(query.Number))
        {
            var number = query.Number.Trim().ToUpperInvariant();
            movements = movements.Where(m => m.Number == number || m.SourceNumber == number);
        }

        return await PageAsync(movements, descending: true, page, cancellationToken);
    }

    /// <summary>
    /// Kardex do item (CA 24): pela ordem de lançamento (<c>posted_seq</c>), nunca pela data de negócio, com o físico da posição e o
    /// custo médio depois de cada movimento. Índice <c>(item_id, posted_seq)</c>: 10 mil movimentos paginados em &lt; 1 s.
    /// </summary>
    public async Task<Result<PagedList<MovementRow>>> KardexAsync(Guid itemId, Guid? locationId, PageRequest page, CancellationToken cancellationToken = default)
    {
        if (!await db.Items.AnyAsync(i => i.Id == itemId, cancellationToken))
        {
            return Error.NotFound("item_not_found", "Item não encontrado.");
        }

        var movements = db.StockMovements.AsNoTracking().Where(m => m.ItemId == itemId);
        if (locationId is { } location)
        {
            movements = movements.Where(m => m.LocationId == location);
        }

        return await PageAsync(movements, descending: false, page, cancellationToken);
    }

    /// <summary>Detalhe do movimento (CA 20 a 23): estorno lido do movimento inverso e quem pode estornar agora.</summary>
    public async Task<Result<MovementDetail>> MovementAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var movement = await db.StockMovements.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (movement is null)
        {
            return Error.NotFound("movement_not_found", "Movimento não encontrado.");
        }

        var row = (await PageAsync(db.StockMovements.AsNoTracking().Where(m => m.Id == id), descending: false, new PageRequest(1, 1), cancellationToken)).Items[0];
        var line = await db.ManualPostingLines.AsNoTracking().FirstOrDefaultAsync(l => l.MovementId == id || l.InMovementId == id, cancellationToken)
            ?? (movement.ReversesMovementId is { } original
                ? await db.ManualPostingLines.AsNoTracking().FirstOrDefaultAsync(l => l.MovementId == original || l.InMovementId == original, cancellationToken)
                : null);
        var posting = line is null ? null : await db.ManualPostings.AsNoTracking().FirstOrDefaultAsync(p => p.Id == line.PostingId, cancellationToken);
        var reversal = await db.StockMovements.AsNoTracking().FirstOrDefaultAsync(m => m.ReversesMovementId == id, cancellationToken);
        var reverses = movement.ReversesMovementId is { } of
            ? await db.StockMovements.AsNoTracking().Where(m => m.Id == of).Select(m => m.Number).FirstOrDefaultAsync(cancellationToken)
            : null;
        var project = movement.ProjectId is { } projectId ? await projects.FindAsync(projectId, cancellationToken) : null;
        var (rule, message) = await policy.EvaluateAsync(movement, movement.ReversesMovementId is null ? line : null, cancellationToken);
        var onHand = await db.StockPositions.AsNoTracking().Where(p => p.ItemId == movement.ItemId && p.LocationId == movement.LocationId)
            .Select(p => p.QuantityOnHand).FirstOrDefaultAsync(cancellationToken);
        var average = await db.Items.IgnoreQueryFilters().AsNoTracking().Where(i => i.Id == movement.ItemId).Select(i => i.AverageCost).FirstOrDefaultAsync(cancellationToken);
        return new MovementDetail(
            row, movement.Reason, project?.Number, reversal?.UserId is { } by ? await users.DisplayNameAsync(by, cancellationToken) : null, reversal?.Reason,
            line?.ReversalSelfApproved == true && reversal is not null, reverses, posting?.Id,
            posting?.RequestedBy is { } author ? await users.DisplayNameAsync(author, cancellationToken) : null, rule, message, onHand, average);
    }

    /// <summary>Contexto do item para lançar (padrões propositivos, CA 1 e 6 a 9): custo médio, saldos por local, lotes e limites.</summary>
    public async Task<Result<StockItemContext>> ItemContextAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);
        if (item is null)
        {
            return Error.NotFound("item_not_found", "Item não encontrado.");
        }

        var unit = await db.Units.IgnoreQueryFilters().Where(u => u.Id == item.UnitId).Select(u => u.Code).FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        var positions = await (from p in db.StockPositions.AsNoTracking()
                               join l in db.Locations.IgnoreQueryFilters().AsNoTracking() on p.LocationId equals l.Id
                               where p.ItemId == itemId
                               orderby l.Code
                               select new StockLocationPosition(l.Id, l.Code, l.Name, p.QuantityOnHand, p.QuantityReserved, p.QuantityOnHand - p.QuantityReserved,
                                   db.StockMovements.Any(m => m.ItemId == itemId && m.LocationId == l.Id))).ToListAsync(cancellationToken);
        var lots = await (from b in db.StockBalances.AsNoTracking()
                          join lot in db.Lots.AsNoTracking() on b.LotId equals lot.Id
                          where b.ItemId == itemId && b.QuantityOnHand > 0
                          orderby lot.CreatedAt
                          select new StockLotBalance(lot.Id, lot.LotNumber, b.LocationId, b.QuantityOnHand)).ToListAsync(cancellationToken);
        var lastLot = await db.Lots.AsNoTracking().Where(l => l.ItemId == itemId).OrderByDescending(l => l.CreatedAt).Select(l => l.LotNumber).FirstOrDefaultAsync(cancellationToken);
        var defaultLocation = await db.Locations.AsNoTracking().Where(l => l.IsDefault && l.IsActive).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(cancellationToken);
        var parameters = await db.InventoryParameters.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? new InventoryParameters();
        return new StockItemContext(
            item.Id, item.Code, item.Name, item.UnitId, unit, item.TracksLot, item.IsActive, item.Type, item.AverageCost, positions.Sum(p => p.OnHand), positions, lots, lastLot,
            defaultLocation, parameters.ManualApprovalAmount, parameters.ManualCostDeviationPct, parameters.OpeningCompletedAt is null);
    }

    /// <summary>Projetos em andamento para a saída avulsa com projeto (ponto 12), sem exigir permissão do Commercial.</summary>
    public async Task<IReadOnlyList<StockProjectOption>> ProjectsAsync(string? term, CancellationToken cancellationToken = default) =>
        (await projects.SearchAsync(term, 20, cancellationToken)).Where(p => p.Status is not ("Closed" or "Cancelled"))
            .Select(p => new StockProjectOption(p.Id, p.Number, p.Name)).ToList();

    private async Task<PagedList<MovementRow>> PageAsync(IQueryable<StockMovement> movements, bool descending, PageRequest page, CancellationToken ct)
    {
        var query = from m in movements
                    join i in db.Items.IgnoreQueryFilters() on m.ItemId equals i.Id
                    join u in db.Units.IgnoreQueryFilters() on i.UnitId equals u.Id
                    join l in db.Locations.IgnoreQueryFilters() on m.LocationId equals l.Id
                    join lot in db.Lots on m.LotId equals lot.Id into lots
                    from lot in lots.DefaultIfEmpty()
                    select new
                    {
                        Movement = m, ItemCode = i.Code, ItemName = i.Name, UnitCode = u.Code, LocationCode = l.Code, LocationName = l.Name,
                        LotNumber = lot == null ? null : lot.LotNumber,
                        ReversedBy = db.StockMovements.Where(r => r.ReversesMovementId == m.Id).Select(r => (Guid?)r.Id).FirstOrDefault(),
                        ReversedAt = db.StockMovements.Where(r => r.ReversesMovementId == m.Id).Select(r => (DateTimeOffset?)r.PostedAt).FirstOrDefault(),
                    };
        var result = await (descending ? query.OrderByDescending(r => r.Movement.PostedSeq) : query.OrderBy(r => r.Movement.PostedSeq)).ToPagedListAsync(page, ct);
        var names = new Dictionary<Guid, string?>();
        foreach (var user in result.Items.Select(r => r.Movement.UserId).Where(u => u is not null).Select(u => u!.Value).Distinct())
        {
            names[user] = await users.DisplayNameAsync(user, ct);
        }

        var items = result.Items.Select(r => new MovementRow(
            r.Movement.Id, r.Movement.Number, r.Movement.Type, r.Movement.At, r.Movement.PostedAt, r.Movement.PostedSeq, r.Movement.ItemId, r.ItemCode, r.ItemName, r.UnitCode,
            r.Movement.LocationId, r.LocationCode, r.LocationName, r.Movement.LotId, r.LotNumber, r.Movement.Quantity, r.Movement.UnitCost, r.Movement.TotalCost,
            r.Movement.BalanceAfter, r.Movement.AverageCostAfter, r.Movement.Origin, r.Movement.SourceType, r.Movement.SourceId, r.Movement.SourceNumber,
            ReversePolicy.SourceLink(r.Movement.SourceType, r.Movement.SourceId), r.Movement.ProjectId, r.Movement.ProductionOrderId, r.Movement.ReversesMovementId,
            r.ReversedBy, r.ReversedAt, r.Movement.UserId is { } u ? names.GetValueOrDefault(u) : null)).ToList();
        return new PagedList<MovementRow>(items, result.Page, result.PageSize, result.Total);
    }
}
