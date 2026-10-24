using BemAqui.BuildingBlocks.Paging;
using BemAqui.Commercial.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>Série na aba Séries da OS (PRD-06a2 CA 11): número, situação e local. Sem custo (CA 14).</summary>
public sealed record OrderSerialRow(Guid Id, string SerialNumber, SerialState Status, Guid? LocationId, string? LocationName, DateTimeOffset CreatedAt);

/// <summary>Lote de matéria-prima consumido pela OS (CA 8): item, lote e quantidade líquida, sem custo.</summary>
public sealed record OrderConsumedLotRow(Guid ItemId, string ItemCode, string ItemName, string UnitCode, Guid? LotId, string? LotNumber, decimal Quantity);

/// <summary>
/// Aba Séries da OS (CA 11): o produto, a entrada no estoque (o movimento com o link, a quantidade, o local e se foi estornada), as séries e
/// os lotes consumidos (que cada série herda). Sem custo: o custo apurado fica na aba Custos, com <c>projects.view_cost</c> (CA 14).
/// </summary>
public sealed record OrderSerialsView(
    Guid ProductionOrderId, Guid? ProductId, string? ProductCode, string? ProductName, bool TracksSerial, Guid? EntryMovementId, string? EntryMovementNumber, decimal? EntryQuantity,
    string? UnitCode, string? EntryLocationName, DateTimeOffset? EntryPostedAt, bool EntryReversed, IReadOnlyList<OrderSerialRow> Serials, IReadOnlyList<OrderConsumedLotRow> ConsumedLots);

/// <summary>Uma série da OS (IDOR: a série de outra OS responde 404), com os lotes herdados da OS.</summary>
public sealed record OrderSerialDetail(OrderSerialRow Serial, Guid ItemId, string ItemCode, string ItemName, Guid ProductionOrderId, Guid? ProjectId, IReadOnlyList<OrderConsumedLotRow> ConsumedLots);

/// <summary>Série na aba Séries do item (CA 11): série, OS, projeto, situação e local.</summary>
public sealed record ItemSerialRow(
    Guid Id, string SerialNumber, SerialState Status, Guid? LocationId, string? LocationName, Guid? ProductionOrderId, string? ProductionOrderNumber, Guid? ProjectId, string? ProjectNumber,
    DateTimeOffset CreatedAt);

/// <summary>Leituras das séries para as abas da OS e do item (PRD-06a2 CA 11 e 12), sem custo.</summary>
public sealed class SerialQueryService(IInventoryDb db, IStockSerials serials, IStockTraceability traceability, IProductionOrders orders, IProjects projects)
{
    public async Task<Result<OrderSerialsView>> ForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default)
    {
        var order = await orders.FindAsync(productionOrderId, cancellationToken);
        if (order is null)
        {
            return OrderNotFound();
        }

        var product = order.ProductId is { } productId
            ? await db.Items.IgnoreQueryFilters().AsNoTracking().Where(i => i.Id == productId)
                .Select(i => new { i.Id, i.Code, i.Name, i.TracksSerial, Unit = db.Units.IgnoreQueryFilters().Where(u => u.Id == i.UnitId).Select(u => u.Code).FirstOrDefault() }).FirstOrDefaultAsync(cancellationToken)
            : null;
        var entry = await serials.EntryAsync(productionOrderId, cancellationToken);
        var rows = await serials.ForOrderAsync(productionOrderId, cancellationToken);
        var locationIds = rows.Where(r => r.LocationId is not null).Select(r => r.LocationId!.Value).Append(entry?.LocationId ?? Guid.Empty).Distinct().ToList();
        var locations = await LocationNamesAsync(locationIds, cancellationToken);
        return new OrderSerialsView(
            productionOrderId, product?.Id, product?.Code, product?.Name, product?.TracksSerial == true, entry?.MovementId, entry?.Number, entry?.Quantity, product?.Unit,
            entry is null ? null : locations.GetValueOrDefault(entry.LocationId), entry?.PostedAt, entry?.Reversed == true,
            rows.Select(r => Row(r, locations)).ToList(), await LotsAsync(await traceability.ForOrderAsync(productionOrderId, cancellationToken), cancellationToken));
    }

    /// <summary>Uma série da OS (CA 12, IDOR): 404 se a OS não existe ou a série é de outra OS.</summary>
    public async Task<Result<OrderSerialDetail>> SerialAsync(Guid productionOrderId, Guid serialId, CancellationToken cancellationToken = default)
    {
        var serial = await db.Serials.AsNoTracking().FirstOrDefaultAsync(s => s.Id == serialId && s.ProductionOrderId == productionOrderId, cancellationToken);
        if (serial is null || await orders.FindAsync(productionOrderId, cancellationToken) is null)
        {
            return Error.NotFound("serial_not_found", "Série não encontrada nesta OS.");
        }

        var item = await db.Items.IgnoreQueryFilters().AsNoTracking().Where(i => i.Id == serial.ItemId).Select(i => new { i.Code, i.Name }).FirstAsync(cancellationToken);
        var locations = await LocationNamesAsync(serial.LocationId is { } l ? [l] : [], cancellationToken);
        var info = StockSerialService.Info(serial);
        return new OrderSerialDetail(Row(info, locations), serial.ItemId, item.Code, item.Name, productionOrderId, serial.ProjectId,
            await LotsAsync(await traceability.ForSerialAsync(serialId, cancellationToken), cancellationToken));
    }

    /// <summary>Aba Séries do item (CA 11): série, OS, projeto, situação e local, as mais novas primeiro; 404 se o item não existe.</summary>
    public async Task<Result<PagedList<ItemSerialRow>>> ForItemAsync(Guid itemId, SerialState? status, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!await db.Items.IgnoreQueryFilters().AsNoTracking().AnyAsync(i => i.Id == itemId, cancellationToken))
        {
            return Error.NotFound("item_not_found", "Item não encontrado.");
        }

        var query = db.Serials.AsNoTracking().Where(s => s.ItemId == itemId);
        if (status is { } wanted)
        {
            query = query.Where(s => s.Status == wanted);
        }

        var paged = await query.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.SerialNumber).ToPagedListAsync(page, cancellationToken);
        var locations = await LocationNamesAsync(paged.Items.Where(s => s.LocationId is not null).Select(s => s.LocationId!.Value).Distinct().ToList(), cancellationToken);
        var orderNumbers = (await orders.FindManyAsync(paged.Items.Where(s => s.ProductionOrderId is not null).Select(s => s.ProductionOrderId!.Value).Distinct().ToList(), cancellationToken))
            .ToDictionary(o => o.Id, o => o.Number);
        var projectNumbers = (await projects.FindManyAsync(paged.Items.Where(s => s.ProjectId is not null).Select(s => s.ProjectId!.Value).Distinct().ToList(), cancellationToken))
            .ToDictionary(p => p.Id, p => p.Number);
        var rows = paged.Items.Select(s => new ItemSerialRow(
            s.Id, s.SerialNumber, s.Status, s.LocationId, s.LocationId is { } l ? locations.GetValueOrDefault(l) : null, s.ProductionOrderId,
            s.ProductionOrderId is { } o ? orderNumbers.GetValueOrDefault(o) : null, s.ProjectId, s.ProjectId is { } p ? projectNumbers.GetValueOrDefault(p) : null, s.CreatedAt)).ToList();
        return new PagedList<ItemSerialRow>(rows, paged.Page, paged.PageSize, paged.Total);
    }

    private static OrderSerialRow Row(SerialInfo s, IReadOnlyDictionary<Guid, string> locations) =>
        new(s.Id, s.SerialNumber, s.Status, s.LocationId, s.LocationId is { } l ? locations.GetValueOrDefault(l) : null, s.CreatedAt);

    private async Task<IReadOnlyList<OrderConsumedLotRow>> LotsAsync(IReadOnlyList<ConsumedLot> lots, CancellationToken ct)
    {
        var ids = lots.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.Items.IgnoreQueryFilters().AsNoTracking().Where(i => ids.Contains(i.Id))
            .Select(i => new { i.Id, i.Code, i.Name, Unit = db.Units.IgnoreQueryFilters().Where(u => u.Id == i.UnitId).Select(u => u.Code).FirstOrDefault() })
            .ToDictionaryAsync(i => i.Id, ct);
        return lots.Select(l =>
        {
            var item = items.GetValueOrDefault(l.ItemId);
            return new OrderConsumedLotRow(l.ItemId, item?.Code ?? string.Empty, item?.Name ?? string.Empty, item?.Unit ?? string.Empty, l.LotId, l.LotNumber, l.Quantity);
        }).OrderBy(l => l.ItemCode).ThenBy(l => l.LotNumber).ToList();
    }

    private async Task<Dictionary<Guid, string>> LocationNamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        await db.Locations.IgnoreQueryFilters().AsNoTracking().Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name, ct);

    private static Error OrderNotFound() => Error.NotFound("production_order_not_found", "OS de fabricação não encontrada.");
}
