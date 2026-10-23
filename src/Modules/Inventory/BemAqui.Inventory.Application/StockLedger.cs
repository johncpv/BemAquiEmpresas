using System.Globalization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Motor único do estoque (ADR 0023 §1 a §4), atrás do <see cref="IStockMovements"/>. Na transação do chamador: trava a linha do
/// item (onde mora o custo médio) → a posição (item, local) → o lote, nessa ordem e, com vários itens, em ordem crescente de
/// <c>item_id</c>; cria a posição e o lote com <c>INSERT … ON CONFLICT DO NOTHING</c> seguido de <c>SELECT … FOR UPDATE</c>;
/// grava o movimento imutável com o físico e o custo médio depois dele. Nenhuma outra classe escreve em
/// <c>stock_positions</c>, <c>stock_balances</c> nem <c>items.average_cost</c> (teste de arquitetura).
/// </summary>
public sealed class StockLedger(
    IInventoryDb db, INumberSequence numbers, IEventPublisher events, ICurrentUser currentUser, TimeProvider clock, ReservationAllocator reservations)
    : IStockMovements, IStockBalances
{
    public const string DocType = "MOV";
    public const string InsufficientStockCode = "insufficient_stock";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<Result<StockMovementResult>> PostAsync(StockPosting posting, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(posting);
        var result = await PostManyAsync([posting], cancellationToken);
        return result.IsFailure ? result.Error! : result.Value[0];
    }

    public async Task<Result<IReadOnlyList<StockMovementResult>>> PostManyAsync(IReadOnlyList<StockPosting> postings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(postings);
        RequireTransaction();
        if (postings.Count == 0)
        {
            return Array.Empty<StockMovementResult>();
        }

        foreach (var posting in postings)
        {
            if (Validate(posting) is { } invalid)
            {
                return invalid;
            }
        }

        // Trava os itens antes de qualquer posição, em ordem crescente de id: dois documentos com os mesmos itens não se travam.
        var items = new Dictionary<Guid, Item>();
        foreach (var itemId in postings.Select(p => p.ItemId).Distinct().Order())
        {
            if (await LockItemAsync(itemId, cancellationToken) is not { } item)
            {
                return Error.Validation("item_not_found", "Item não encontrado.", new Dictionary<string, string[]> { ["itemId"] = ["Item não encontrado."] });
            }

            items[itemId] = item;
        }

        var results = new StockMovementResult[postings.Count];
        string? number = null;
        foreach (var index in PostingOrder(postings))
        {
            var posting = postings[index];
            var posted = await PostLockedAsync(items[posting.ItemId], posting with { Number = posting.Number ?? number }, cancellationToken);
            if (posted.IsFailure)
            {
                return posted.Error!;
            }

            number ??= posted.Value.Number;
            results[index] = posted.Value;
        }

        return results;
    }

    public async Task<Result<StockMovementResult>> ReverseAsync(StockReversal reversal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reversal);
        RequireTransaction();
        var original = await db.StockMovements.AsNoTracking().FirstOrDefaultAsync(m => m.Id == reversal.MovementId, cancellationToken);
        if (original is null)
        {
            return Error.NotFound("movement_not_found", "Movimento não encontrado.");
        }

        if (original.ReversesMovementId is not null)
        {
            return Error.Conflict("movement_is_reversal", "Este movimento já é um estorno e não se estorna.");
        }

        if (original.SourceType != reversal.SourceType || original.SourceId != reversal.SourceId)
        {
            return Error.Conflict("reverse_from_source", "Estorne pelo documento de origem.");
        }

        if (await LockItemAsync(original.ItemId, cancellationToken) is not { } item)
        {
            return Error.NotFound("item_not_found", "Item não encontrado.");
        }

        if (await db.StockMovements.AsNoTracking().FirstOrDefaultAsync(m => m.ReversesMovementId == original.Id, cancellationToken) is { } done)
        {
            return new StockMovementResult(done.Id, done.Number, done.BalanceAfter, done.AverageCostAfter, true);
        }

        var quantity = -original.Quantity;
        var (position, balance) = await LockPositionAsync(original.ItemId, original.LocationId, original.LotId, cancellationToken);
        if (quantity < 0 && (Allowance(position, original.SourceType) + quantity < 0 || balance.QuantityOnHand + quantity < 0))
        {
            return await InsufficientAsync(item, original.LocationId, position, balance, original.SourceType, cancellationToken);
        }

        var onHand = await OnHandAsync(item.Id, cancellationToken);
        var reversesEntry = original.Quantity > 0;
        var average = AverageCostCalculator.Next(original.Type, onHand, item.AverageCost, quantity, original.UnitCost, reversesEntry);
        position.ApplyOnHand(quantity);
        balance.ApplyOnHand(quantity);
        item.ApplyAverageCost(average);
        var now = clock.GetUtcNow();
        var movement = StockMovement.Create(
            reversal.Number ?? await numbers.NextAsync(DocType, cancellationToken), original.Type, original.ItemId, original.LocationId, original.LotId, original.SerialId,
            quantity, original.UnitCost, position.QuantityOnHand, average, original.SourceType, original.SourceId, original.Leg, original.Id, Clean(reversal.Reason),
            original.ProjectId, original.ProductionOrderId, now, now, currentUser.UserId, original.SourceNumber);
        db.StockMovements.Add(movement);
        PublishProjectEvent(original, seq: 2, previous: Math.Abs(original.TotalCost), next: 0m, now);
        return await SaveAndCompleteAsync(item, movement, null, cancellationToken);
    }

    public async Task<Guid> EnsureLotAsync(Guid itemId, string lotNumber, Guid? supplierPartyId = null, DateOnly? expiry = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lotNumber);
        var number = lotNumber.Trim();
        if (await db.Lots.Where(l => l.ItemId == itemId && l.LotNumber == number).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(cancellationToken) is { } found)
        {
            return found;
        }

        // Duas primeiras entradas do mesmo lote em paralelo: o índice único decide, a segunda lê o que a primeira criou.
        var now = clock.GetUtcNow();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO inventory.lots (id, item_id, lot_number, supplier_party_id, received_at, expiry, created_at) VALUES ({Guid.CreateVersion7()}, {itemId}, {number}, {supplierPartyId}, {now}, {expiry}, {now}) ON CONFLICT DO NOTHING",
            cancellationToken);
        return await db.Lots.Where(l => l.ItemId == itemId && l.LotNumber == number).Select(l => l.Id).FirstAsync(cancellationToken);
    }

    public async Task<OrderMaterialCost> CostByOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default)
    {
        var rows = await db.StockMovements.AsNoTracking()
            .Where(m => m.ProductionOrderId == productionOrderId && (m.Type == StockMovementType.Issue || m.Type == StockMovementType.Return))
            .GroupBy(m => m.ItemId)
            .Select(g => new { ItemId = g.Key, Quantity = g.Sum(m => m.Quantity), Amount = g.Sum(m => m.TotalCost) })
            .ToListAsync(cancellationToken);
        var lines = rows.Select(r => new OrderMaterialCostLine(r.ItemId, -r.Quantity, -r.Amount)).OrderBy(l => l.ItemId).ToList();
        return new OrderMaterialCost(productionOrderId, lines, lines.Sum(l => l.Amount));
    }

    public async Task<IReadOnlyList<ProjectStockMovement>> ProjectMovementsAsync(long afterPostedSeq, int take, CancellationToken cancellationToken = default)
    {
        var originals = await db.StockMovements.AsNoTracking()
            .Where(m => m.PostedSeq > afterPostedSeq && m.ProjectId != null && m.ReversesMovementId == null && (m.Type == StockMovementType.Issue || m.Type == StockMovementType.Return))
            .OrderBy(m => m.PostedSeq).Take(Math.Clamp(take, 1, 1000)).ToListAsync(cancellationToken);
        var ids = originals.Select(m => m.Id).ToList();
        var reversals = await db.StockMovements.AsNoTracking().Where(m => m.ReversesMovementId != null && ids.Contains(m.ReversesMovementId.Value))
            .ToDictionaryAsync(m => m.ReversesMovementId!.Value, cancellationToken);
        var facts = new List<ProjectStockMovement>();
        foreach (var m in originals)
        {
            var total = Math.Abs(m.TotalCost);
            facts.Add(new ProjectStockMovement(m.Id, m.PostedSeq, m.Type, 1, m.ProjectId!.Value, m.ProductionOrderId, m.ItemId, Math.Abs(m.Quantity), 0m, total, m.PostedAt));
            if (reversals.TryGetValue(m.Id, out var reversal))
            {
                facts.Add(new ProjectStockMovement(m.Id, m.PostedSeq, m.Type, 2, m.ProjectId.Value, m.ProductionOrderId, m.ItemId, Math.Abs(m.Quantity), total, 0m, reversal.PostedAt));
            }
        }

        return facts;
    }

    public async Task<IReadOnlyList<StockAvailability>> GetAsync(IReadOnlyCollection<Guid> itemIds, Guid? locationId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        if (itemIds.Count == 0)
        {
            return [];
        }

        var positions = db.StockPositions.AsNoTracking().Where(p => itemIds.Contains(p.ItemId));
        if (locationId is { } location)
        {
            positions = positions.Where(p => p.LocationId == location);
        }

        return await positions.OrderBy(p => p.ItemId).ThenBy(p => p.LocationId)
            .Select(p => new StockAvailability(p.ItemId, p.LocationId, p.QuantityOnHand, p.QuantityReserved, p.QuantityOnHand - p.QuantityReserved))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Ordem de trava e de gravação (ADR 0023 §3, CA 14): por <c>item_id</c> crescente e, no mesmo item, na ordem do documento. Os
    /// itens são travados todos antes de qualquer posição, nesta mesma ordem.
    /// </summary>
    public static IReadOnlyList<int> PostingOrder(IReadOnlyList<StockPosting> postings)
    {
        ArgumentNullException.ThrowIfNull(postings);
        return Enumerable.Range(0, postings.Count).OrderBy(i => postings[i].ItemId).ThenBy(i => i).ToList();
    }

    /// <summary>
    /// Mensagem do 409 quando parte do físico está reservada (EST-02 CA 13): "Há 6,0000 UN disponível; 4,0000 UN está reservado para OS."
    /// </summary>
    public static Error Reserved(decimal available, decimal reserved, string unit)
    {
        var suffix = string.IsNullOrEmpty(unit) ? string.Empty : " " + unit;
        return Error.Conflict(InsufficientStockCode, $"Há {available.ToString("N4", PtBr)}{suffix} disponível; {reserved.ToString("N4", PtBr)}{suffix} está reservado para OS.") with
        {
            Data = new Dictionary<string, object?> { ["available"] = available, ["reserved"] = reserved },
        };
    }

    /// <summary>Mensagem do 409 de saldo (CA 13): "Saldo insuficiente: há 5,0000 UN de Chapa em Almoxarifado."</summary>
    public static Error Insufficient(decimal available, string unit, string item, string location) =>
        Error.Conflict(InsufficientStockCode, $"Saldo insuficiente: há {available.ToString("N4", PtBr)} {unit} de {item} em {location}.");

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("IStockMovements exige transação aberta: o chamador é dono da transação (ADR 0023 §3).");
        }
    }

    private static Error? Validate(StockPosting posting)
    {
        if (!AverageCostCalculator.SignAllowed(posting.Type, posting.Quantity))
        {
            return Field("quantity", "Quantidade com o sentido do tipo do movimento: entrada positiva, saída negativa.");
        }

        if (string.IsNullOrWhiteSpace(posting.SourceType) || posting.SourceId == Guid.Empty || posting.Leg < 1)
        {
            return Field("sourceId", "Movimento sem documento de origem.");
        }

        if (posting.Quantity > 0 && posting.Type is not (StockMovementType.Transfer or StockMovementType.Adjustment) && posting.UnitCost is not > 0)
        {
            return Field("unitCost", "Informe o custo unitário da entrada, maior que zero.");
        }

        return posting.UnitCost is < 0 ? Field("unitCost", "Custo unitário não pode ser negativo.") : null;
    }

    private async Task<Result<StockMovementResult>> PostLockedAsync(Item item, StockPosting posting, CancellationToken ct)
    {
        // Idempotência pela perna (CA 25), conferida com o item já travado: a segunda chamada devolve o mesmo movimento.
        if (await db.StockMovements.AsNoTracking().FirstOrDefaultAsync(
                m => m.SourceType == posting.SourceType && m.SourceId == posting.SourceId && m.Leg == posting.Leg && m.Type == posting.Type && m.ReversesMovementId == null, ct) is { } existing)
        {
            return new StockMovementResult(existing.Id, existing.Number, existing.BalanceAfter, existing.AverageCostAfter, true);
        }

        if (item.Type == ItemType.Service)
        {
            return Field("itemId", "Serviço não tem estoque.");
        }

        if (item.TracksLot && posting.LotId is null)
        {
            return Field("lotNumber", "Este item controla lote: informe o lote.");
        }

        if (posting.LotId is { } lotId && !await db.Lots.AnyAsync(l => l.Id == lotId && l.ItemId == item.Id, ct))
        {
            return Field("lotId", "Lote não encontrado neste item.");
        }

        if (!await db.Locations.AnyAsync(l => l.Id == posting.LocationId, ct))
        {
            return Field("locationId", "Local não encontrado.");
        }

        var (position, balance) = await LockPositionAsync(item.Id, posting.LocationId, posting.LotId, ct);
        if (posting.Quantity < 0 && (Allowance(position, posting.SourceType) + posting.Quantity < 0 || balance.QuantityOnHand + posting.Quantity < 0))
        {
            return await InsufficientAsync(item, posting.LocationId, position, balance, posting.SourceType, ct);
        }

        var onHand = await OnHandAsync(item.Id, ct);
        var entry = posting.Quantity > 0 && posting.Type != StockMovementType.Transfer;
        var unitCost = entry ? posting.UnitCost ?? item.AverageCost : item.AverageCost;
        var average = AverageCostCalculator.Next(posting.Type, onHand, item.AverageCost, posting.Quantity, unitCost, reversesEntry: false);
        position.ApplyOnHand(posting.Quantity);
        balance.ApplyOnHand(posting.Quantity);
        item.ApplyAverageCost(average);
        var now = clock.GetUtcNow();
        var movement = StockMovement.Create(
            posting.Number ?? await numbers.NextAsync(DocType, ct), posting.Type, item.Id, posting.LocationId, posting.LotId, posting.SerialId, posting.Quantity, unitCost,
            position.QuantityOnHand, average, posting.SourceType, posting.SourceId, posting.Leg, null, Clean(posting.Reason), posting.ProjectId, posting.ProductionOrderId,
            posting.At ?? now, now, currentUser.UserId, posting.SourceNumber);
        db.StockMovements.Add(movement);
        PublishProjectEvent(movement, seq: 1, previous: 0m, next: Math.Abs(movement.TotalCost), now);
        return await SaveAndCompleteAsync(item, movement, posting.ProductionOrderId, ct);
    }

    /// <summary>
    /// O que a saída pode tirar da posição (EST-02 CA 13): o disponível (físico − reservado); só a requisição da OS (EST-03) consome o
    /// reservado, baixando a própria reserva na mesma transação (o <c>CHECK</c> do banco segura o resto).
    /// </summary>
    public static decimal Allowance(StockPosition position, string sourceType)
    {
        ArgumentNullException.ThrowIfNull(position);
        return sourceType == StockSources.Requisition ? position.QuantityOnHand : position.QuantityOnHand - position.QuantityReserved;
    }

    /// <summary>
    /// Grava o movimento e, se ele trouxe material (entrada, devolução, transferência de entrada, estorno de saída), completa as faltas
    /// do item no local na mesma transação (EST-02 CA 11, pontos 28 e 29), com a OS de origem do lançamento primeiro.
    /// </summary>
    private async Task<Result<StockMovementResult>> SaveAndCompleteAsync(Item item, StockMovement movement, Guid? originOrderId, CancellationToken ct)
    {
        var saved = await SaveAsync(item, movement, ct);
        if (saved.IsFailure || movement.Quantity <= 0)
        {
            return saved;
        }

        await reservations.CompleteAsync(item.Id, movement.LocationId, originOrderId ?? movement.ProductionOrderId, ReservationOrigins.Entry(movement.Number), ct);
        await db.SaveChangesAsync(ct);
        return saved;
    }

    /// <summary>Trava a linha do item; se ela já estava no contexto sem mudança, relê o custo médio de depois da trava.</summary>
    private async Task<Item?> LockItemAsync(Guid itemId, CancellationToken ct)
    {
        var locked = await db.Database.SqlQuery<Guid>($"SELECT id AS \"Value\" FROM inventory.items WHERE id = {itemId} AND NOT is_deleted FOR UPDATE").ToListAsync(ct);
        if (locked.Count == 0)
        {
            return null;
        }

        var item = await db.Items.FirstAsync(i => i.Id == itemId, ct);
        var entry = db.Entry(item);
        if (entry.State == EntityState.Unchanged)
        {
            await entry.ReloadAsync(ct);
        }

        return item;
    }

    /// <summary>Posição (item, local) e depois o lote, criados se faltarem e travados (CA 15).</summary>
    private async Task<(StockPosition Position, StockBalance Balance)> LockPositionAsync(Guid itemId, Guid locationId, Guid? lotId, CancellationToken ct)
    {
        var position = await LockedPositionAsync(itemId, locationId, ct);
        if (position is null)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO inventory.stock_positions (id, item_id, location_id, quantity_on_hand, quantity_reserved) VALUES ({Guid.CreateVersion7()}, {itemId}, {locationId}, 0, 0) ON CONFLICT DO NOTHING", ct);
            position = (await LockedPositionAsync(itemId, locationId, ct))!;
        }

        var balance = await LockedBalanceAsync(itemId, locationId, lotId, ct);
        if (balance is null)
        {
            if (lotId is { } lot)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO inventory.stock_balances (id, item_id, location_id, lot_id, quantity_on_hand) VALUES ({Guid.CreateVersion7()}, {itemId}, {locationId}, {lot}, 0) ON CONFLICT DO NOTHING", ct);
            }
            else
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"INSERT INTO inventory.stock_balances (id, item_id, location_id, lot_id, quantity_on_hand) VALUES ({Guid.CreateVersion7()}, {itemId}, {locationId}, NULL, 0) ON CONFLICT DO NOTHING", ct);
            }

            balance = (await LockedBalanceAsync(itemId, locationId, lotId, ct))!;
        }

        return (position, balance);
    }

    private async Task<StockPosition?> LockedPositionAsync(Guid itemId, Guid locationId, CancellationToken ct)
    {
        var position = await db.StockPositions.FromSql($"SELECT * FROM inventory.stock_positions WHERE item_id = {itemId} AND location_id = {locationId} FOR UPDATE").FirstOrDefaultAsync(ct);
        return await FreshAsync(position, ct);
    }

    private async Task<StockBalance?> LockedBalanceAsync(Guid itemId, Guid locationId, Guid? lotId, CancellationToken ct)
    {
        var balance = lotId is { } lot
            ? await db.StockBalances.FromSql($"SELECT * FROM inventory.stock_balances WHERE item_id = {itemId} AND location_id = {locationId} AND lot_id = {lot} FOR UPDATE").FirstOrDefaultAsync(ct)
            : await db.StockBalances.FromSql($"SELECT * FROM inventory.stock_balances WHERE item_id = {itemId} AND location_id = {locationId} AND lot_id IS NULL FOR UPDATE").FirstOrDefaultAsync(ct);
        return await FreshAsync(balance, ct);
    }

    /// <summary>Linha já rastreada e sem mudança pendente pode ter sido lida antes da trava: relê (ADR 0019 §2).</summary>
    private async Task<T?> FreshAsync<T>(T? entity, CancellationToken ct)
        where T : class
    {
        if (entity is not null && db.Entry(entity).State == EntityState.Unchanged)
        {
            await db.Entry(entity).ReloadAsync(ct);
        }

        return entity;
    }

    /// <summary>Físico total do item em todos os locais, antes do movimento (o custo médio é global, <c>04-fluxos</c> §3.1).</summary>
    private Task<decimal> OnHandAsync(Guid itemId, CancellationToken ct) =>
        db.StockPositions.Where(p => p.ItemId == itemId).SumAsync(p => p.QuantityOnHand, ct);

    private async Task<Error> InsufficientAsync(Item item, Guid locationId, StockPosition position, StockBalance balance, string sourceType, CancellationToken ct)
    {
        var unit = await db.Units.IgnoreQueryFilters().Where(u => u.Id == item.UnitId).Select(u => u.Code).FirstOrDefaultAsync(ct) ?? string.Empty;
        var location = await db.Locations.IgnoreQueryFilters().Where(l => l.Id == locationId).Select(l => l.Name).FirstOrDefaultAsync(ct) ?? "o local";
        var available = Math.Max(Math.Min(Allowance(position, sourceType), balance.QuantityOnHand), 0);
        var reserved = sourceType == StockSources.Requisition ? 0m : position.QuantityReserved;
        return reserved > 0 ? Reserved(available, reserved, unit) : Insufficient(available, unit, item.Name, location);
    }

    /// <summary>Saída ou devolução com projeto (CA 25, ADR 0025 §2): valor anterior e novo da fonte, no outbox da transação.</summary>
    private void PublishProjectEvent(StockMovement source, int seq, decimal previous, decimal next, DateTimeOffset at)
    {
        if (source.ProjectId is not { } project)
        {
            return;
        }

        var quantity = Math.Abs(source.Quantity);
        if (source.Type == StockMovementType.Issue)
        {
            events.Publish(new StockIssued(source.Id, source.SourceType, source.SourceId, seq, project, source.ProductionOrderId, source.ItemId, quantity, previous, next, at));
        }
        else if (source.Type == StockMovementType.Return)
        {
            events.Publish(new StockReturned(source.Id, source.SourceType, source.SourceId, seq, project, source.ProductionOrderId, source.ItemId, quantity, previous, next, at));
        }
    }

    private async Task<Result<StockMovementResult>> SaveAsync(Item item, StockMovement movement, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.CheckViolation })
        {
            // Rede de segurança: os CHECKs do banco recusaram saldo negativo (CA 14).
            return Insufficient(0, string.Empty, item.Name, "o local");
        }

        return new StockMovementResult(movement.Id, movement.Number, movement.BalanceAfter, movement.AverageCostAfter, false);
    }

    private static Error Field(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
