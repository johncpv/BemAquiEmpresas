using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>Arquivo exportado (CA 4): nome, tipo e bytes (UTF-8 com BOM).</summary>
public sealed record TraceExport(string FileName, string ContentType, byte[] Content);

/// <summary>
/// Rastreabilidade (EST-04): busca por série ou lote (CA 10), série → origem (CA 1 e 2) e lote → destino (CA 3), com a exportação auditada
/// (CA 4). Lê só as tabelas do Inventory, pelos índices de <c>lot_id</c>, <c>production_order_id</c> e <c>source_id</c>; os nomes de OS,
/// projeto e cliente vêm dos Contracts dos donos, numa chamada por lista (CA 7, ADR 0006). O cliente sai pelo
/// <see cref="CustomerNameMasker"/> (CA 9). Sem custo.
/// </summary>
public sealed class TraceabilityService(
    IInventoryDb db, IStockTraceability traceability, IProductionOrders orders, IProjects projects, IParties parties, IPermissionChecker permissions, IAuditWriter audit,
    TimeProvider clock)
{
    public const string LotEntity = nameof(Lot);

    /// <summary>
    /// CA 10: exata ou por prefixo, só com parâmetros (o <c>StartsWith</c> do EF vira <c>LIKE</c> com o termo escapado como parâmetro, nunca
    /// texto do usuário montado no SQL), até 50 séries e 50 lotes, os exatos primeiro.
    /// </summary>
    public async Task<Result<TraceSearchResult>> SearchAsync(string? q, CancellationToken cancellationToken = default)
    {
        var (term, error) = TraceabilityRules.Term(q);
        if (error is not null)
        {
            return Error.Validation(Error.Codes.Validation, error, new Dictionary<string, string[]> { ["q"] = [error] });
        }

        if (term is null)
        {
            return new TraceSearchResult(string.Empty, false, [], []);
        }

        var serials = await (from s in db.Serials.AsNoTracking()
                             where s.SerialNumber == term || s.SerialNumber.StartsWith(term)
                             join i in db.Items.IgnoreQueryFilters().AsNoTracking() on s.ItemId equals i.Id
                             orderby s.SerialNumber == term descending, s.SerialNumber
                             select new TraceSerialHit(s.Id, s.ItemId, s.SerialNumber, i.Code, i.Name, s.Status)).Take(TraceabilityRules.MaxResults).ToListAsync(cancellationToken);
        var lots = await (from l in db.Lots.AsNoTracking()
                          where l.LotNumber == term || l.LotNumber.StartsWith(term)
                          join i in db.Items.IgnoreQueryFilters().AsNoTracking() on l.ItemId equals i.Id
                          orderby l.LotNumber == term descending, l.LotNumber, i.Code
                          select new TraceLotHit(l.Id, l.ItemId, l.LotNumber, i.Code, i.Name, l.Expiry)).Take(TraceabilityRules.MaxResults).ToListAsync(cancellationToken);
        return new TraceSearchResult(term, TraceabilityRules.LooksLikeSerial(term), serials, lots);
    }

    /// <summary>Série → origem (CA 1 e 2), pelo item (IDOR, CA 8): série de outro item responde 404.</summary>
    public async Task<Result<SerialTrace>> SerialAsync(Guid itemId, Guid serialId, CancellationToken cancellationToken = default)
    {
        var serial = await db.Serials.AsNoTracking().FirstOrDefaultAsync(s => s.Id == serialId && s.ItemId == itemId, cancellationToken);
        if (serial is null)
        {
            return Error.NotFound("serial_not_found", "Série não encontrada neste item.");
        }

        var item = await db.Items.IgnoreQueryFilters().AsNoTracking().Where(i => i.Id == itemId).Select(i => new { i.Code, i.Name }).FirstAsync(cancellationToken);
        var location = serial.LocationId is { } l ? await db.Locations.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == l).Select(x => x.Name).FirstOrDefaultAsync(cancellationToken) : null;
        var order = serial.ProductionOrderId is { } o ? await orders.FindAsync(o, cancellationToken) : null;
        var project = serial.ProjectId is { } p ? await projects.FindAsync(p, cancellationToken) : null;
        var customer = CustomerNameMasker.Customer(project is null ? null : await parties.FindAsync(project.PartyId, cancellationToken), await CanViewPartiesAsync(cancellationToken));
        var units = serial.ProductionOrderId is { } orderId
            ? await db.Serials.AsNoTracking().CountAsync(s => s.ProductionOrderId == orderId && s.Status != SerialState.Cancelled, cancellationToken)
            : 0;
        var shipment = serial.ShipmentId is { } shipmentId ? (await ShipmentNumbersAsync([shipmentId], cancellationToken)).GetValueOrDefault(shipmentId) : null;
        var consumed = await traceability.ForSerialAsync(serialId, cancellationToken);
        return new SerialTrace(
            serial.Id, serial.SerialNumber, serial.Status, itemId, item.Code, item.Name, serial.LocationId, location, serial.CreatedAt, serial.ProductionOrderId, order?.Number, order?.Status,
            order?.CompletedAt, units, serial.ProjectId, project?.Number, project?.Name, customer, serial.ShipmentId, shipment, serial.ShippedAt,
            consumed.Count > 0 ? TraceabilityRules.SharedLotsNote(order?.Number, units) : null, await ConsumedLotsAsync(consumed, cancellationToken));
    }

    /// <summary>Lote → destino (CA 3), pelo item (IDOR, CA 8): lote de outro item responde 404.</summary>
    public async Task<Result<LotTrace>> LotAsync(Guid itemId, Guid lotId, CancellationToken cancellationToken = default)
    {
        var lot = await db.Lots.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lotId && l.ItemId == itemId, cancellationToken);
        if (lot is null)
        {
            return Error.NotFound("lot_not_found", "Lote não encontrado neste item.");
        }

        var item = await db.Items.IgnoreQueryFilters().AsNoTracking().Where(i => i.Id == itemId)
            .Select(i => new { i.Code, i.Name, Unit = db.Units.IgnoreQueryFilters().Where(u => u.Id == i.UnitId).Select(u => u.Code).FirstOrDefault() }).FirstAsync(cancellationToken);
        var ledger = await db.StockMovements.AsNoTracking().Where(m => m.LotId == lotId).OrderBy(m => m.PostedSeq)
            .Select(m => new LotLedgerLine(m.Id, m.Number, m.Type, m.SourceType, m.SourceId, m.SourceNumber, m.ProductionOrderId, m.ReversesMovementId, m.Quantity, m.At, m.Reason))
            .ToListAsync(cancellationToken);
        var balances = await (from b in db.StockBalances.AsNoTracking()
                              where b.LotId == lotId && b.QuantityOnHand != 0
                              join l in db.Locations.IgnoreQueryFilters().AsNoTracking() on b.LocationId equals l.Id
                              orderby l.Name
                              select new TraceLotBalance(b.LocationId, l.Name, b.QuantityOnHand)).ToListAsync(cancellationToken);
        var reconciliation = TraceabilityRules.Reconcile(ledger, balances.Sum(b => b.OnHand));
        var consumptions = await ConsumptionsAsync(reconciliation.Consumptions, cancellationToken);
        var supplier = lot.SupplierPartyId is { } s ? await parties.FindAsync(s, cancellationToken) : null;
        var (state, days) = TraceabilityRules.Expiry(lot.Expiry, BusinessTime.Today(clock));
        return new LotTrace(
            lot.Id, lot.LotNumber, itemId, item.Code, item.Name, item.Unit ?? string.Empty, lot.Expiry, state, days, lot.SupplierPartyId, supplier?.LegalName, lot.ReceivedAt,
            reconciliation.Entries.Select(e => Entry(e, reconciliation.ReversedIds)).ToList(), reconciliation.TotalReceived, balances, reconciliation.OnHand, consumptions,
            reconciliation.TotalConsumed, reconciliation.Others, reconciliation.TotalOther, reconciliation.Balanced);
    }

    /// <summary>
    /// CA 4: CSV do destino completo do lote (separador <c>;</c>, UTF-8 com BOM, sem injeção de fórmula), com o cliente na regra do CA 9; a
    /// exportação vai para a auditoria do lote (quem, quando, qual lote e quantas linhas).
    /// </summary>
    public async Task<Result<TraceExport>> ExportLotAsync(Guid itemId, Guid lotId, CancellationToken cancellationToken = default)
    {
        var trace = await LotAsync(itemId, lotId, cancellationToken);
        if (trace.IsFailure)
        {
            return trace.Error!;
        }

        var rows = TraceabilityRules.CsvRows(trace.Value);
        var bytes = SafeCsv.Bytes(SafeCsv.Text(TraceabilityRules.CsvHeader, rows));
        await audit.RecordExportAsync(LotEntity, lotId.ToString(), "csv", rows.Count, cancellationToken);
        return new TraceExport(TraceabilityRules.CsvFileName(trace.Value.LotNumber), SafeCsv.ContentType, bytes);
    }

    private async Task<bool> CanViewPartiesAsync(CancellationToken ct) => await permissions.HasPermissionAsync("parties", StandardActions.View, ct);

    private static TraceEntry Entry(LotLedgerLine e, IReadOnlySet<Guid> reversed)
    {
        var number = e.SourceNumber ?? e.Number;
        return new TraceEntry(e.MovementId, e.SourceType, e.SourceId, number, TraceabilityRules.EntryLabel(e.SourceType, e.Type, number), e.At, e.Quantity, reversed.Contains(e.MovementId));
    }

    /// <summary>Consumos por OS com os nomes por lista: OS, projetos, clientes, séries e romaneios em uma consulta cada (CA 7).</summary>
    private async Task<IReadOnlyList<TraceConsumption>> ConsumptionsAsync(IReadOnlyList<LotOrderConsumption> uses, CancellationToken ct)
    {
        if (uses.Count == 0)
        {
            return [];
        }

        var orderIds = uses.Select(u => u.ProductionOrderId).Distinct().ToList();
        var orderInfo = (await orders.FindManyAsync(orderIds, ct)).ToDictionary(o => o.Id);
        var projectIds = orderInfo.Values.Select(o => o.ProjectId).Distinct().ToList();
        var projectInfo = (await projects.FindManyAsync(projectIds, ct)).ToDictionary(p => p.Id);
        var canView = await CanViewPartiesAsync(ct);
        var partyInfo = (await parties.FindManyAsync(projectInfo.Values.Select(p => p.PartyId).Distinct().ToList(), ct)).ToDictionary(p => p.Id);
        var serials = await db.Serials.AsNoTracking().Where(s => s.ProductionOrderId != null && orderIds.Contains(s.ProductionOrderId.Value)).OrderBy(s => s.SerialNumber).ToListAsync(ct);
        var shipments = await ShipmentNumbersAsync(serials.Where(s => s.ShipmentId is not null).Select(s => s.ShipmentId!.Value).Distinct().ToList(), ct);
        var entries = await db.StockMovements.AsNoTracking()
            .Where(m => m.SourceType == StockSources.ProductionOrder && orderIds.Contains(m.SourceId) && m.Type == StockMovementType.Production)
            .Select(m => new { m.SourceId, m.Id, m.ReversesMovementId, m.ItemId }).ToListAsync(ct);
        var productIds = orderInfo.Values.Where(o => o.ProductId is not null).Select(o => o.ProductId!.Value).Distinct().ToList();
        var tracksSerial = await db.Items.IgnoreQueryFilters().AsNoTracking().Where(i => productIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.TracksSerial, ct);
        return uses.Select(u =>
        {
            var order = orderInfo.GetValueOrDefault(u.ProductionOrderId);
            var project = order is null ? null : projectInfo.GetValueOrDefault(order.ProjectId);
            var party = project is null ? null : partyInfo.GetValueOrDefault(project.PartyId);
            var mine = serials.Where(s => s.ProductionOrderId == u.ProductionOrderId && s.Status != SerialState.Cancelled).ToList();
            var orderEntries = entries.Where(e => e.SourceId == u.ProductionOrderId).ToList();
            var originals = orderEntries.Where(e => e.ReversesMovementId is null).ToList();
            var reversedAll = originals.Count > 0 && originals.All(o => orderEntries.Any(r => r.ReversesMovementId == o.Id));
            var tracks = order?.ProductId is { } product && tracksSerial.GetValueOrDefault(product);
            return new TraceConsumption(
                u.ProductionOrderId, order?.Number, order?.Status, order?.ProjectId, project?.Number, project?.Name, CustomerNameMasker.Customer(party, canView), u.At, u.Quantity,
                mine.Select(s => new TraceSerial(s.Id, s.SerialNumber, s.Status, s.ShipmentId, s.ShipmentId is { } sh ? shipments.GetValueOrDefault(sh) : null, s.ShippedAt)).ToList(),
                TraceabilityRules.NoSerialNote(mine.Count, order?.Status, originals.Count > 0, reversedAll, tracks), TraceabilityRules.SharedLotsNote(order?.Number, mine.Count));
        }).ToList();
    }

    /// <summary>Número do romaneio pela saída <c>Shipment</c> que ele lançou (o movimento guarda o <c>ROM</c>), sem chamar a Produção por série.</summary>
    private async Task<Dictionary<Guid, string?>> ShipmentNumbersAsync(List<Guid> shipmentIds, CancellationToken ct)
    {
        if (shipmentIds.Count == 0)
        {
            return [];
        }

        var rows = await db.StockMovements.AsNoTracking().Where(m => m.SourceType == StockSources.Shipment && shipmentIds.Contains(m.SourceId))
            .Select(m => new { m.SourceId, m.SourceNumber }).Distinct().ToListAsync(ct);
        return rows.GroupBy(r => r.SourceId).ToDictionary(g => g.Key, g => g.Select(r => r.SourceNumber).FirstOrDefault(n => n is not null));
    }

    /// <summary>Lotes consumidos pela OS da série (CA 1): item, lote, quantidade, fornecedor, entradas e validade, numa consulta por lista.</summary>
    private async Task<IReadOnlyList<TraceConsumedLot>> ConsumedLotsAsync(IReadOnlyList<ConsumedLot> consumed, CancellationToken ct)
    {
        if (consumed.Count == 0)
        {
            return [];
        }

        var itemIds = consumed.Select(c => c.ItemId).Distinct().ToList();
        var items = await db.Items.IgnoreQueryFilters().AsNoTracking().Where(i => itemIds.Contains(i.Id))
            .Select(i => new { i.Id, i.Code, i.Name, Unit = db.Units.IgnoreQueryFilters().Where(u => u.Id == i.UnitId).Select(u => u.Code).FirstOrDefault() }).ToDictionaryAsync(i => i.Id, ct);
        var lotIds = consumed.Where(c => c.LotId is not null).Select(c => c.LotId!.Value).Distinct().ToList();
        var lots = await db.Lots.AsNoTracking().Where(l => lotIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var suppliers = (await parties.FindManyAsync(lots.Values.Where(l => l.SupplierPartyId is not null).Select(l => l.SupplierPartyId!.Value).Distinct().ToList(), ct))
            .ToDictionary(p => p.Id, p => p.LegalName);
        var entryTypes = new[] { StockMovementType.Receipt, StockMovementType.OpeningBalance, StockMovementType.Production };
        var ledger = await db.StockMovements.AsNoTracking().Where(m => m.LotId != null && lotIds.Contains(m.LotId.Value) && entryTypes.Contains(m.Type)).OrderBy(m => m.PostedSeq)
            .Select(m => new { m.LotId, Line = new LotLedgerLine(m.Id, m.Number, m.Type, m.SourceType, m.SourceId, m.SourceNumber, m.ProductionOrderId, m.ReversesMovementId, m.Quantity, m.At, m.Reason) })
            .ToListAsync(ct);
        var reversed = ledger.Where(l => l.Line.ReversesMovementId is not null).Select(l => l.Line.ReversesMovementId!.Value).ToHashSet();
        var today = BusinessTime.Today(clock);
        return consumed.Select(c =>
        {
            var item = items.GetValueOrDefault(c.ItemId);
            var lot = c.LotId is { } id ? lots.GetValueOrDefault(id) : null;
            var expiry = TraceabilityRules.Expiry(lot?.Expiry, today).State;
            var lotEntries = c.LotId is { } lid ? ledger.Where(l => l.LotId == lid && l.Line.ReversesMovementId is null).Select(l => Entry(l.Line, reversed)).ToList() : [];
            return new TraceConsumedLot(c.ItemId, item?.Code ?? string.Empty, item?.Name ?? string.Empty, item?.Unit ?? string.Empty, c.LotId, c.LotNumber ?? lot?.LotNumber, c.Quantity,
                lot?.SupplierPartyId is { } sp ? suppliers.GetValueOrDefault(sp) : null, lot?.Expiry, expiry, lotEntries);
        }).OrderBy(l => l.ItemCode, StringComparer.Ordinal).ThenBy(l => l.LotNumber, StringComparer.Ordinal).ToList();
    }
}
