using System.Text.Json;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

public sealed partial class InventoryCountService
{
    /// <summary>"Inventários": os mais recentes primeiro, com o progresso e o ajuste gerado.</summary>
    public async Task<PagedList<InventoryCountSummary>> ListAsync(InventoryCountListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var counts = db.InventoryCounts.AsNoTracking();
        if (query.Status is { } status)
        {
            counts = counts.Where(c => c.Status == status);
        }

        if (query.LocationId is { } locationId)
        {
            counts = counts.Where(c => c.LocationId == locationId);
        }

        var rows = await counts.OrderByDescending(c => c.SnapshotAt).ThenByDescending(c => c.Id)
            .Select(c => new
            {
                Count = c,
                Location = db.Locations.IgnoreQueryFilters().Where(l => l.Id == c.LocationId).Select(l => l.Name).FirstOrDefault(),
                Lines = db.InventoryCountLines.Count(l => l.CountId == c.Id && !l.Superseded),
                Counted = db.InventoryCountLines.Count(l => l.CountId == c.Id && !l.Superseded && l.CountedQty != null),
            })
            .ToPagedListAsync(page, cancellationToken);
        var names = await NamesAsync(rows.Items.Select(r => r.Count.OpenedBy), cancellationToken);
        var items = rows.Items.Select(r => new InventoryCountSummary(
            r.Count.Id, r.Count.Number, r.Count.LocationId, r.Location ?? "—", r.Count.Status, r.Count.SnapshotAt, Name(names, r.Count.OpenedBy), r.Lines, r.Counted, r.Count.AdjustmentId,
            r.Count.AdjustmentNumber)).ToList();
        return new PagedList<InventoryCountSummary>(items, rows.Page, rows.PageSize, rows.Total);
    }

    /// <summary>Detalhe sem saldo nem custo (CA 12 e 15): cabeçalho, progresso, situação do ajuste e o que o usuário pode fazer.</summary>
    public async Task<Result<InventoryCountDetail>> DetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var count = await db.InventoryCounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (count is null)
        {
            return NotFound();
        }

        var location = await db.Locations.IgnoreQueryFilters().AsNoTracking().Where(l => l.Id == count.LocationId).Select(l => new { l.Code, l.Name }).FirstAsync(cancellationToken);
        var lines = await db.InventoryCountLines.AsNoTracking().Where(l => l.CountId == id && !l.Superseded)
            .Select(l => new { Counted = l.CountedQty != null, Recount = l.RecountOfLineId != null }).ToListAsync(cancellationToken);
        var adjustment = count.AdjustmentId is { } adjustmentId
            ? await db.StockAdjustments.AsNoTracking().Where(a => a.Id == adjustmentId).Select(a => new { a.Status, a.ReversedAt }).FirstOrDefaultAsync(cancellationToken)
            : null;
        var names = await NamesAsync([count.OpenedBy, count.ClosedBy, count.CancelledBy], cancellationToken);
        var canAdjust = await permissions.HasPermissionAsync("inventory", "adjust", cancellationToken);
        var canCreate = await permissions.HasPermissionAsync("inventory", "create", cancellationToken);
        var counting = count.Status == InventoryCountStatus.Counting;
        var counted = lines.Count(l => l.Counted);
        string? situation = adjustment is null || count.AdjustmentNumber is null ? null : $"Ajuste {count.AdjustmentNumber} {AdjustmentWord(adjustment.Status, adjustment.ReversedAt is not null)}";
        var reopenable = count.Status == InventoryCountStatus.Closed && adjustment?.Status is StockAdjustmentStatus.Rejected or StockAdjustmentStatus.Cancelled;
        return new InventoryCountDetail(
            count.Id, count.Number, count.LocationId, location.Code, location.Name, count.Status, InventoryCountRules.StatusLabel(count.Status), await ScopeLabelAsync(count.Scope, cancellationToken),
            count.SnapshotAt, Name(names, count.OpenedBy), Name(names, count.ClosedBy), count.ClosedAt, count.UncountedPolicy, count.CancelReason, Name(names, count.CancelledBy), count.CancelledAt,
            count.ReopenCount, lines.Count, counted, lines.Count(l => l.Recount && !l.Counted), InventoryCountRules.Progress(counted, lines.Count), count.AdjustmentId, count.AdjustmentNumber,
            adjustment?.Status, situation, counting && canCreate, canAdjust, counting && canAdjust, reopenable && canAdjust, count.Version);
    }

    /// <summary>
    /// A contagem cega (CA 3, 8 e 15): as linhas vigentes sem saldo, movimento, diferença nem custo; a recontagem pendente não aparece
    /// para quem contou a linha original. Fechado ou cancelado, fica em modo leitura.
    /// </summary>
    public async Task<Result<InventoryCountingView>> CountingAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var count = await db.InventoryCounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (count is null)
        {
            return NotFound();
        }

        var location = await db.Locations.IgnoreQueryFilters().AsNoTracking().Where(l => l.Id == count.LocationId).Select(l => l.Name).FirstAsync(cancellationToken);
        var all = await CountingLinesAsync(db.InventoryCountLines.AsNoTracking().Where(l => l.CountId == id && !l.Superseded), cancellationToken);
        var originals = await OriginalCountersAsync(id, cancellationToken);
        var me = currentUser.UserId;
        var visible = all.Where(l => !(l.IsRecount && l.CountedQty is null && me is not null && originals.GetValueOrDefault(l.Id) == me)).ToList();
        var counted = all.Count(l => l.CountedQty is not null);
        var canCreate = await permissions.HasPermissionAsync("inventory", "create", cancellationToken);
        return new InventoryCountingView(count.Id, count.Number, count.LocationId, location, count.Status, all.Count, counted, InventoryCountRules.Progress(counted, all.Count),
            count.Status == InventoryCountStatus.Counting && canCreate, visible, count.Version);
    }

    /// <summary>Linha da contagem pelo inventário (CA 14, IDOR): de outro inventário responde como inexistente.</summary>
    public async Task<Result<InventoryCountingLine>> CountingLineAsync(Guid id, Guid lineId, CancellationToken cancellationToken = default)
    {
        if (!await db.InventoryCounts.AsNoTracking().AnyAsync(c => c.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var lines = await CountingLinesAsync(db.InventoryCountLines.AsNoTracking().Where(l => l.CountId == id && l.Id == lineId), cancellationToken);
        return lines is [var line] ? line : LineNotFound();
    }

    /// <summary>
    /// "Ver divergências" (CA 5, 7, 9 e 10), só com <c>inventory.adjust</c>: em contagem, calculadas agora (com a escolha dos não
    /// contados, quando informada); fechado, as gravadas. Só as linhas com diferença, pelo valor absoluto; o antes → depois do fechamento.
    /// </summary>
    public async Task<Result<InventoryDivergences>> DivergencesAsync(Guid id, UncountedPolicy? policy, CancellationToken cancellationToken = default)
    {
        var count = await db.InventoryCounts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (count is null)
        {
            return NotFound();
        }

        var rows = await (from l in db.InventoryCountLines.AsNoTracking()
                          join i in db.Items.IgnoreQueryFilters() on l.ItemId equals i.Id
                          join u in db.Units.IgnoreQueryFilters() on i.UnitId equals u.Id
                          where l.CountId == id && !l.Superseded
                          select new
                          {
                              Line = l, ItemCode = i.Code, ItemName = i.Name, i.AverageCost, UnitCode = u.Code,
                              LotNumber = db.Lots.Where(x => x.Id == l.LotId).Select(x => x.LotNumber).FirstOrDefault(),
                              Previous = db.InventoryCountLines.Where(p => p.Id == l.RecountOfLineId).Select(p => new { p.CountedQty, p.CountedBy }).FirstOrDefault(),
                          }).ToListAsync(cancellationToken);
        var counting = count.Status == InventoryCountStatus.Counting;
        var now = await LotBalancesAsync(count.LocationId, rows.Select(r => (r.Line.ItemId, r.Line.LotId)).ToList(), cancellationToken);
        var effective = counting ? policy : count.UncountedPolicy ?? UncountedPolicy.KeepSystem;
        var names = await NamesAsync(rows.SelectMany(r => new[] { r.Line.CountedBy, r.Previous?.CountedBy }), cancellationToken);
        var views = new List<InventoryDivergenceLine>();
        foreach (var r in rows)
        {
            var l = r.Line;
            var systemNow = now.GetValueOrDefault((l.ItemId, l.LotId));
            decimal movement, difference, cost;
            if (counting)
            {
                if (l.CountedQty is null && effective is null)
                {
                    continue;
                }

                var result = InventoryCountRules.Resolve(l.SystemQty, l.CountedQty, l.SystemQtyAtCount, systemNow, effective ?? UncountedPolicy.KeepSystem);
                (movement, difference, cost) = (result.MovementDelta, result.Difference, r.AverageCost);
            }
            else
            {
                (movement, difference, cost) = (l.MovementDelta ?? 0m, l.Difference ?? 0m, l.UnitCost ?? r.AverageCost);
            }

            if (difference == 0m)
            {
                continue;
            }

            views.Add(new InventoryDivergenceLine(
                l.Id, l.Sequence, l.ItemId, r.ItemCode, r.ItemName, r.UnitCode, l.LotId, r.LotNumber, l.SystemQty, l.CountedQty, l.CountedQty is not null, Name(names, l.CountedBy), l.CountedAt,
                movement, movement != 0m, difference, cost, StockAdjustmentRules.Value(difference, cost), systemNow, InventoryCountRules.Round(systemNow + difference),
                l.RecountOfLineId is not null, Name(names, r.Previous?.CountedBy), r.Previous?.CountedQty, l.RecountOfLineId is not null && l.CountedQty is null,
                counting && l.CountedQty is not null));
        }

        var ordered = InventoryCountRules.ByValue(views, v => v.Value, v => v.Difference, v => v.Sequence);
        var (surplus, shortage) = StockAdjustmentRules.Totals(ordered.Select(v => (v.Difference, v.UnitCost)));
        var uncounted = rows.Count(r => r.Line.CountedQty is null);
        return new InventoryDivergences(
            count.Id, count.Number, count.Status, uncounted, uncounted > 0 ? InventoryCountRules.UncountedQuestion(uncounted) : null, counting ? policy : count.UncountedPolicy, surplus, shortage,
            surplus - shortage, ordered, count.Version);
    }

    private async Task<List<InventoryCountingLine>> CountingLinesAsync(IQueryable<InventoryCountLine> query, CancellationToken ct)
    {
        var rows = await (from l in query
                          join i in db.Items.IgnoreQueryFilters() on l.ItemId equals i.Id
                          join u in db.Units.IgnoreQueryFilters() on i.UnitId equals u.Id
                          orderby l.Sequence
                          select new
                          {
                              Line = l, ItemCode = i.Code, ItemName = i.Name, i.TracksLot, StockUnitId = i.UnitId, UnitCode = u.Code,
                              LotNumber = db.Lots.Where(x => x.Id == l.LotId).Select(x => x.LotNumber).FirstOrDefault(),
                              EnteredUnit = db.Units.IgnoreQueryFilters().Where(e => e.Id == l.EnteredUnitId).Select(e => e.Code).FirstOrDefault(),
                          }).ToListAsync(ct);
        var itemIds = rows.Select(r => r.Line.ItemId).Distinct().ToList();
        var conversions = await (from c in db.ItemUnitConversions.AsNoTracking()
                                 join f in db.Units.IgnoreQueryFilters() on c.FromUnitId equals f.Id
                                 join t in db.Units.IgnoreQueryFilters() on c.ToUnitId equals t.Id
                                 where itemIds.Contains(c.ItemId)
                                 select new { c.ItemId, c.FromUnitId, FromCode = f.Code, c.ToUnitId, ToCode = t.Code }).ToListAsync(ct);
        var names = await NamesAsync(rows.Select(r => r.Line.CountedBy), ct);
        var me = currentUser.UserId;
        return rows.Select(r =>
        {
            var units = new List<CountUnitOption> { new(r.StockUnitId, r.UnitCode) };
            foreach (var c in conversions.Where(c => c.ItemId == r.Line.ItemId))
            {
                var other = c.FromUnitId == r.StockUnitId ? new CountUnitOption(c.ToUnitId, c.ToCode) : new CountUnitOption(c.FromUnitId, c.FromCode);
                if (units.All(u => u.Id != other.Id))
                {
                    units.Add(other);
                }
            }

            var l = r.Line;
            return new InventoryCountingLine(
                l.Id, l.Sequence, l.ItemId, r.ItemCode, r.ItemName, r.UnitCode, r.StockUnitId, units, r.TracksLot, l.LotId, r.LotNumber, l.CountedQty, l.EnteredQuantity, r.EnteredUnit,
                Name(names, l.CountedBy), l.CountedAt, me is not null && l.CountedBy == me, l.RecountOfLineId is not null, l.AddedDuringCount, l.Version);
        }).ToList();
    }

    /// <summary>Quem contou a linha original de cada recontagem do inventário (CA 8).</summary>
    private async Task<Dictionary<Guid, Guid?>> OriginalCountersAsync(Guid countId, CancellationToken ct) =>
        await (from r in db.InventoryCountLines.AsNoTracking()
               join o in db.InventoryCountLines.AsNoTracking() on r.RecountOfLineId equals o.Id
               where r.CountId == countId
               select new { r.Id, o.CountedBy }).ToDictionaryAsync(x => x.Id, x => x.CountedBy, ct);

    private async Task<string> ScopeLabelAsync(string json, CancellationToken ct)
    {
        InventoryCountScope? scope;
        try
        {
            scope = JsonSerializer.Deserialize<InventoryCountScope>(json);
        }
        catch (JsonException)
        {
            scope = null;
        }

        if (scope is null)
        {
            return "Itens com saldo no local";
        }

        var parts = new List<string> { scope.IncludeZeroWithMovement ? "Itens com saldo ou movimento no local" : "Itens com saldo no local" };
        if (scope.CategoryId is { } categoryId)
        {
            parts.Add("categoria " + (await db.ItemCategories.IgnoreQueryFilters().AsNoTracking().Where(c => c.Id == categoryId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "removida"));
        }

        if (scope.ItemType is { } type)
        {
            parts.Add("tipo " + TypeLabel(type));
        }

        return string.Join(" · ", parts);
    }

    private static string TypeLabel(BemAqui.Inventory.Contracts.ItemType type) => type switch
    {
        BemAqui.Inventory.Contracts.ItemType.RawMaterial => "Matéria-prima",
        BemAqui.Inventory.Contracts.ItemType.Component => "Componente",
        BemAqui.Inventory.Contracts.ItemType.Finished => "Produto acabado",
        BemAqui.Inventory.Contracts.ItemType.Consumable => "Consumível",
        _ => "Serviço",
    };

    private static string AdjustmentWord(StockAdjustmentStatus status, bool reversed) => reversed ? "estornado" : status switch
    {
        StockAdjustmentStatus.Pending => "aguardando aprovação",
        StockAdjustmentStatus.Approved => "aprovado",
        StockAdjustmentStatus.Rejected => "reprovado",
        _ => "cancelado",
    };

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
