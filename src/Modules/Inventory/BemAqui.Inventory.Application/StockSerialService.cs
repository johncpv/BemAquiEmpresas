using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Séries do produto acabado e rastreabilidade dos lotes consumidos (PRD-06a2 CA 4, 5, 8, 10 e 13), atrás de <see cref="IStockSerials"/> e
/// <see cref="IStockTraceability"/>. Grava só na transação do chamador (a da conclusão ou do estorno, de que a Produção é dona). A série
/// segue o formato D15 <c>{código}-{AAAA}-{seq}</c> pela sequência do item e do ano (<c>inventory.serial_sequences</c>), pulando número já
/// usado (inclusive o editado à mão); o índice único <c>(item_id, serial_number)</c> segura a corrida.
/// </summary>
public sealed class StockSerialService(IInventoryDb db, TimeProvider clock) : IStockSerials, IStockTraceability
{
    public const string ShippedCode = "serial_shipped";
    public const string ShippedMessage = "O produto já foi expedido.";
    public const string NotInStockCode = "serial_not_in_stock";

    public async Task<IReadOnlyList<string>> SuggestAsync(Guid itemId, int count, CancellationToken cancellationToken = default)
    {
        if (count <= 0 || await db.Items.AsNoTracking().Where(i => i.Id == itemId).Select(i => i.Code).FirstOrDefaultAsync(cancellationToken) is not { } code)
        {
            return [];
        }

        var year = BusinessTime.Today(clock).Year;
        var last = await db.SerialSequences.AsNoTracking().Where(s => s.ItemId == itemId && s.Year == year).Select(s => (long?)s.LastValue).FirstOrDefaultAsync(cancellationToken);
        var next = last is { } value ? value + 1 : await StartAsync(cancellationToken);
        var result = new List<string>(count);
        while (result.Count < count)
        {
            var candidates = Enumerable.Range(0, count - result.Count + 5).Select(i => SerialRules.Format(code, year, next + i)).ToList();
            var taken = await db.Serials.AsNoTracking().Where(s => s.ItemId == itemId && candidates.Contains(s.SerialNumber)).Select(s => s.SerialNumber).ToListAsync(cancellationToken);
            result.AddRange(candidates.Where(c => !taken.Contains(c)).Take(count - result.Count));
            next += candidates.Count;
        }

        return result;
    }

    public async Task<Result<IReadOnlyList<SerialInfo>>> CreateAsync(SerialCreate create, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);
        RequireTransaction();
        var code = await db.Items.AsNoTracking().Where(i => i.Id == create.ItemId).Select(i => i.Code).FirstOrDefaultAsync(cancellationToken);
        if (code is null || create.Units <= 0)
        {
            return Field("serialNumbers", "Item não encontrado para gerar as séries.");
        }

        List<string> numbers;
        if (create.SerialNumbers is { } typed)
        {
            if (SerialRules.Invalid(typed, create.Units) is { } invalid)
            {
                return Field("serialNumbers", invalid);
            }

            numbers = typed.Select(s => s.Trim()).ToList();
            if (await db.Serials.AsNoTracking().Where(s => s.ItemId == create.ItemId && numbers.Contains(s.SerialNumber)).Select(s => s.SerialNumber).FirstOrDefaultAsync(cancellationToken) is { } taken)
            {
                return Field("serialNumbers", $"O número de série {taken} já existe neste item.");
            }
        }
        else
        {
            numbers = await NextNumbersAsync(create.ItemId, code, create.Units, cancellationToken);
        }

        var now = clock.GetUtcNow();
        var serials = numbers.Select(n => new Serial
        {
            ItemId = create.ItemId, SerialNumber = n, ProductionOrderId = create.ProductionOrderId, ProjectId = create.ProjectId, LocationId = create.LocationId,
            Status = SerialState.InStock, EntryMovementId = create.EntryMovementId, CreatedAt = now,
        }).ToList();
        foreach (var serial in serials)
        {
            db.Serials.Add(serial);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, "ux_serials_item_number"))
        {
            // CA 13: outra conclusão do mesmo item gravou o número entre a conferência e o insert. O chamador desfaz a transação.
            return Error.Conflict("serial_conflict", "Outro usuário gravou o mesmo número de série. Confira as séries e tente de novo.");
        }

        return serials.Select(Info).ToList();
    }

    public async Task<Result<int>> CancelForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        var serials = await db.Serials.Where(s => s.ProductionOrderId == productionOrderId && s.Status != SerialState.Cancelled).ToListAsync(cancellationToken);
        if (serials.Any(s => s.Status == SerialState.Shipped))
        {
            return Error.Conflict(ShippedCode, ShippedMessage);
        }

        var now = clock.GetUtcNow();
        foreach (var serial in serials)
        {
            serial.Status = SerialState.Cancelled;
            serial.CancelledAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return serials.Count;
    }

    public async Task<IReadOnlyList<SerialInfo>> ForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default)
    {
        var rows = await db.Serials.AsNoTracking().Where(s => s.ProductionOrderId == productionOrderId).OrderBy(s => s.SerialNumber).ToListAsync(cancellationToken);
        return rows.Select(Info).ToList();
    }

    public async Task<FinishedGoodsEntry?> EntryAsync(Guid productionOrderId, CancellationToken cancellationToken = default)
    {
        var entry = await db.StockMovements.AsNoTracking()
            .Where(m => m.SourceType == StockSources.ProductionOrder && m.SourceId == productionOrderId && m.Type == StockMovementType.Production && m.ReversesMovementId == null)
            .OrderByDescending(m => m.PostedSeq).FirstOrDefaultAsync(cancellationToken);
        if (entry is null)
        {
            return null;
        }

        var reversed = await db.StockMovements.AsNoTracking().AnyAsync(m => m.ReversesMovementId == entry.Id, cancellationToken);
        return new FinishedGoodsEntry(entry.Id, entry.Number, entry.ItemId, entry.LocationId, entry.Quantity, entry.UnitCost, entry.TotalCost, entry.PostedAt, reversed, entry.Leg);
    }

    public async Task<IReadOnlyList<SerialInfo>> FindAsync(IReadOnlyCollection<Guid> serialIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serialIds);
        if (serialIds.Count == 0)
        {
            return [];
        }

        var rows = await db.Serials.AsNoTracking().Where(s => serialIds.Contains(s.Id)).OrderBy(s => s.SerialNumber).ToListAsync(cancellationToken);
        return rows.Select(Info).ToList();
    }

    public async Task<Result<int>> ShipAsync(Guid shipmentId, IReadOnlyCollection<Guid> serialIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serialIds);
        RequireTransaction();
        var ids = serialIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return 0;
        }

        var serials = await db.Serials.Where(s => ids.Contains(s.Id)).OrderBy(s => s.SerialNumber).ToListAsync(cancellationToken);
        if (serials.Count != ids.Count)
        {
            return Error.Conflict(NotInStockCode, "Uma série do romaneio não existe mais. Recarregue o romaneio.");
        }

        // PRD-06b CA 16: quem chega depois encontra a série já expedida e não grava nada (o chamador desfaz a transação).
        if (serials.FirstOrDefault(s => s.Status != SerialState.InStock) is { } gone)
        {
            return Error.Conflict(NotInStockCode, $"A série {gone.SerialNumber} não está mais no estoque.");
        }

        var now = clock.GetUtcNow();
        foreach (var serial in serials)
        {
            serial.Status = SerialState.Shipped;
            serial.ShippedAt = now;
            serial.ShipmentId = shipmentId;
        }

        await db.SaveChangesAsync(cancellationToken);
        return serials.Count;
    }

    public async Task<Result<int>> UnshipAsync(Guid shipmentId, IReadOnlyCollection<Guid> serialIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serialIds);
        RequireTransaction();
        var ids = serialIds.Distinct().ToList();
        var serials = await db.Serials.Where(s => ids.Contains(s.Id) && s.ShipmentId == shipmentId && s.Status == SerialState.Shipped).ToListAsync(cancellationToken);
        foreach (var serial in serials)
        {
            serial.Status = SerialState.InStock;
            serial.ShippedAt = null;
            serial.ShipmentId = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        return serials.Count;
    }

    public async Task<IReadOnlyList<ConsumedLot>> RecordOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        if (await db.SerialComponents.AnyAsync(c => c.ProductionOrderId == productionOrderId && c.ReversedAt == null, cancellationToken))
        {
            return await ConsumedAsync(productionOrderId, cancellationToken);
        }

        // CA 8: líquido das baixas menos as devoluções (e os estornos, que repetem o tipo com o sinal oposto), por item e lote.
        var net = await db.StockMovements.AsNoTracking()
            .Where(m => m.ProductionOrderId == productionOrderId && (m.Type == StockMovementType.Issue || m.Type == StockMovementType.Return))
            .GroupBy(m => new { m.ItemId, m.LotId })
            .Select(g => new { g.Key.ItemId, g.Key.LotId, Quantity = -g.Sum(m => m.Quantity) })
            .ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();
        foreach (var line in net.Where(l => l.Quantity > 0).OrderBy(l => l.ItemId).ThenBy(l => l.LotId))
        {
            db.SerialComponents.Add(new SerialComponent { ProductionOrderId = productionOrderId, ItemId = line.ItemId, LotId = line.LotId, Quantity = line.Quantity, CreatedAt = now });
        }

        await db.SaveChangesAsync(cancellationToken);
        return await ConsumedAsync(productionOrderId, cancellationToken);
    }

    public async Task ReverseOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default)
    {
        RequireTransaction();
        var now = clock.GetUtcNow();
        foreach (var component in await db.SerialComponents.Where(c => c.ProductionOrderId == productionOrderId && c.ReversedAt == null).ToListAsync(cancellationToken))
        {
            component.ReversedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    Task<IReadOnlyList<ConsumedLot>> IStockTraceability.ForOrderAsync(Guid productionOrderId, CancellationToken cancellationToken) => ConsumedAsync(productionOrderId, cancellationToken);

    public async Task<IReadOnlyList<ConsumedLot>> ForSerialAsync(Guid serialId, CancellationToken cancellationToken = default)
    {
        var order = await db.Serials.AsNoTracking().Where(s => s.Id == serialId && s.Status != SerialState.Cancelled).Select(s => s.ProductionOrderId).FirstOrDefaultAsync(cancellationToken);
        return order is { } id ? await ConsumedAsync(id, cancellationToken) : [];
    }

    private async Task<IReadOnlyList<ConsumedLot>> ConsumedAsync(Guid productionOrderId, CancellationToken ct) =>
        await (from c in db.SerialComponents.AsNoTracking()
               where c.ProductionOrderId == productionOrderId && c.ReversedAt == null
               join l in db.Lots.AsNoTracking() on c.LotId equals l.Id into lots
               from l in lots.DefaultIfEmpty()
               orderby c.ItemId, l.LotNumber
               select new ConsumedLot(c.ItemId, c.LotId, l == null ? null : l.LotNumber, c.Quantity)).ToListAsync(ct);

    /// <summary>Próximos números da sequência do item e do ano (consome a sequência), pulando os que já existem no item.</summary>
    private async Task<List<string>> NextNumbersAsync(Guid itemId, string code, int units, CancellationToken ct)
    {
        var year = BusinessTime.Today(clock).Year;
        var start = await StartAsync(ct);
        var numbers = new List<string>(units);
        while (numbers.Count < units)
        {
            var values = await db.Database.SqlQuery<long>($"""
                INSERT INTO inventory.serial_sequences (item_id, year, last_value) VALUES ({itemId}, {year}, {start})
                ON CONFLICT (item_id, year) DO UPDATE SET last_value = inventory.serial_sequences.last_value + 1
                RETURNING last_value AS "Value"
                """).ToListAsync(ct);
            var number = SerialRules.Format(code, year, values.Single());
            if (!numbers.Contains(number) && !await db.Serials.AnyAsync(s => s.ItemId == itemId && s.SerialNumber == number, ct))
            {
                numbers.Add(number);
            }
        }

        return numbers;
    }

    /// <summary>Valor inicial da sequência (parâmetro do estoque, padrão 1).</summary>
    private async Task<long> StartAsync(CancellationToken ct) =>
        Math.Max(1, await db.InventoryParameters.AsNoTracking().Select(p => (long?)p.SerialStartValue).FirstOrDefaultAsync(ct) ?? 1);

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("IStockSerials e IStockTraceability gravam só na transação do chamador (ADR 0023 §1).");
        }
    }

    internal static SerialInfo Info(Serial s) =>
        new(s.Id, s.ItemId, s.SerialNumber, s.ProductionOrderId, s.ProjectId, s.LocationId, s.Status, s.EntryMovementId, s.CreatedAt, s.ShipmentId, s.ShippedAt);

    private static Error Field(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });
}
