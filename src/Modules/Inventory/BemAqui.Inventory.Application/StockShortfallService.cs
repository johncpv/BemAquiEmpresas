using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>Uma venda com falta do item no local: a falta, o número e o id da venda e quando ela aconteceu.</summary>
public sealed record StockShortfallSale(Guid ShortfallId, Guid SourceId, string SourceNumber, decimal Quantity, DateTimeOffset OccurredAt);

/// <summary>
/// Linha de Estoque → Divergências (PDV-05 CA 7): por local (o da loja) e item, a falta aberta somada, o disponível agora, desde quando falta e as
/// vendas ligadas.
/// </summary>
public sealed record StockShortfallRow(
    Guid ItemId, string ItemCode, string ItemName, string UnitCode, Guid LocationId, string LocationName, decimal Quantity, decimal Available, DateTimeOffset Since,
    IReadOnlyList<StockShortfallSale> Sales);

public sealed record StockShortfallListQuery(string? Term, Guid? LocationId);

/// <summary>"Aplicar baixa" de um item num local: aplica as faltas abertas, da mais antiga para a mais nova, enquanto o disponível cobre cada uma.</summary>
public sealed record ApplyStockShortfallInput(Guid ItemId, Guid LocationId);

/// <summary>Resultado de "Aplicar baixa": quantas faltas e quanto foi baixado, e quanto ainda falta.</summary>
public sealed record ApplyStockShortfallResult(int Applied, decimal AppliedQuantity, decimal Remaining);

/// <summary>
/// Divergências de estoque do PDV (PDV-05, opção B da PP-02): a venda do caixa nunca é recusada por saldo; o que não havia fica numa falta aberta
/// (<see cref="StockShortfall"/>) até o estoque ser regularizado (ajuste, inventário ou entrada) e alguém aplicar a baixa. O motor
/// (<see cref="StockLedger"/>) e o <c>CHECK</c> do saldo não mudam. A aplicação trava o item antes de ler as faltas (ADR 0023 §3), então ela, o
/// cancelamento da venda e as outras saídas do item ficam em série.
/// </summary>
public sealed class StockShortfallService(
    IInventoryDb db, IStockMovements movements, ReservationAllocator allocator, IEventPublisher events, ICurrentUser currentUser, TimeProvider clock)
    : IStockShortfalls
{
    public const string Link = "/app/estoque/divergencias";

    public async Task<Result> OpenAsync(StockShortfallInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("A falta de estoque é aberta na transação do documento de origem.");
        }

        if (input.Quantity <= 0 || decimal.Round(input.Quantity, 4) != input.Quantity)
        {
            return Error.Validation("invalid_quantity", "A falta precisa ser maior que zero, com até 4 casas.");
        }

        if (await db.StockShortfalls.AnyAsync(
                s => s.SourceType == input.SourceType && s.SourceId == input.SourceId && s.ItemId == input.ItemId && s.LocationId == input.LocationId, cancellationToken))
        {
            return Result.Success();
        }

        db.StockShortfalls.Add(StockShortfall.Open(input.ItemId, input.LocationId, input.Quantity, input.SourceType, input.SourceId, input.SourceNumber, input.OccurredAt, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyDictionary<Guid, decimal>>> CancelSourceAsync(string sourceType, Guid sourceId, CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("O cancelamento da falta roda na transação do documento de origem.");
        }

        var itemIds = await db.StockShortfalls.Where(s => s.SourceType == sourceType && s.SourceId == sourceId && s.Status != StockShortfallStatus.Cancelled)
            .Select(s => s.ItemId).Distinct().ToListAsync(cancellationToken);
        foreach (var itemId in itemIds.Order())
        {
            await allocator.LockItemAsync(itemId, cancellationToken);
        }

        var closed = new Dictionary<Guid, decimal>();
        var now = clock.GetUtcNow();
        var shortfalls = await db.StockShortfalls.Where(s => s.SourceType == sourceType && s.SourceId == sourceId && s.Status != StockShortfallStatus.Cancelled)
            .ToListAsync(cancellationToken);
        foreach (var shortfall in shortfalls)
        {
            await db.Entry(shortfall).ReloadAsync(cancellationToken);
            if (shortfall.Status == StockShortfallStatus.Open)
            {
                // A venda cancelada reduz a falta antes de dar entrada (CA 9): o que nunca saiu do estoque não volta para ele.
                shortfall.Cancel(null, currentUser.UserId, now);
                closed[shortfall.ItemId] = closed.GetValueOrDefault(shortfall.ItemId) + shortfall.Quantity;
            }
            else if (shortfall is { Status: StockShortfallStatus.Applied, MovementId: { } movementId })
            {
                var reversed = await movements.ReverseAsync(new StockReversal(movementId, StockSources.StockShortfall, shortfall.Id, "Venda " + shortfall.SourceNumber + " cancelada"), cancellationToken);
                if (reversed.IsFailure)
                {
                    return reversed.Error!;
                }

                shortfall.Cancel(reversed.Value.MovementId, currentUser.UserId, now);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return closed;
    }

    /// <summary>Estoque → Divergências (CA 7): as faltas abertas por local e item, a mais antiga primeiro.</summary>
    public async Task<PagedList<StockShortfallRow>> ListAsync(StockShortfallListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);
        var open = db.StockShortfalls.AsNoTracking().Where(s => s.Status == StockShortfallStatus.Open);
        if (query.LocationId is { } location)
        {
            open = open.Where(s => s.LocationId == location);
        }

        var shortfalls = await open.OrderBy(s => s.OccurredAt).ToListAsync(cancellationToken);
        var itemIds = shortfalls.Select(s => s.ItemId).Distinct().ToList();
        var locationIds = shortfalls.Select(s => s.LocationId).Distinct().ToList();
        var items = await (from i in db.Items.AsNoTracking()
                           join u in db.Units.AsNoTracking() on i.UnitId equals u.Id
                           where itemIds.Contains(i.Id)
                           select new { i.Id, i.Code, i.Name, Unit = u.Code }).ToDictionaryAsync(i => i.Id, cancellationToken);
        var locations = await db.Locations.AsNoTracking().Where(l => locationIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);
        var available = await db.StockPositions.AsNoTracking().Where(p => itemIds.Contains(p.ItemId) && locationIds.Contains(p.LocationId))
            .Select(p => new { p.ItemId, p.LocationId, Available = p.QuantityOnHand - p.QuantityReserved }).ToListAsync(cancellationToken);
        var term = string.IsNullOrWhiteSpace(query.Term) ? null : SearchText.Normalize(query.Term);
        var rows = shortfalls.GroupBy(s => (s.LocationId, s.ItemId))
            .Select(g =>
            {
                var item = items.GetValueOrDefault(g.Key.ItemId);
                return new StockShortfallRow(
                    g.Key.ItemId, item?.Code ?? string.Empty, item?.Name ?? "Item removido", item?.Unit ?? string.Empty, g.Key.LocationId,
                    locations.GetValueOrDefault(g.Key.LocationId) ?? "Local removido", g.Sum(s => s.Quantity),
                    available.FirstOrDefault(a => a.ItemId == g.Key.ItemId && a.LocationId == g.Key.LocationId)?.Available ?? 0m, g.Min(s => s.OccurredAt),
                    g.Select(s => new StockShortfallSale(s.Id, s.SourceId, s.SourceNumber, s.Quantity, s.OccurredAt)).ToList());
            })
            .Where(r => term is null || SearchText.Normalize(r.ItemCode + " " + r.ItemName).Contains(term, StringComparison.Ordinal))
            .OrderBy(r => r.LocationName, StringComparer.CurrentCulture).ThenBy(r => r.Since).ToList();
        var normalized = page.Normalize();
        return new PagedList<StockShortfallRow>(rows.Skip(normalized.Skip).Take(normalized.PageSize).ToList(), normalized.Page, normalized.PageSize, rows.Count);
    }

    /// <summary>
    /// "Aplicar baixa" (CA 6B): com o estoque regularizado, lança pelo motor cada falta aberta do item no local que o disponível cobre, da mais
    /// antiga para a mais nova, e avisa o dono da origem pelo outbox. Nada coberto: 409 <c>insufficient_stock</c>, nada é gravado.
    /// </summary>
    public async Task<Result<ApplyStockShortfallResult>> ApplyAsync(ApplyStockShortfallInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await allocator.LockItemAsync(input.ItemId, cancellationToken);
        var open = await db.StockShortfalls
            .Where(s => s.ItemId == input.ItemId && s.LocationId == input.LocationId && s.Status == StockShortfallStatus.Open)
            .OrderBy(s => s.OccurredAt).ThenBy(s => s.CreatedAt).ToListAsync(cancellationToken);
        if (open.Count == 0)
        {
            return Error.NotFound("shortfall_not_found", "Nenhuma falta aberta deste item neste local.");
        }

        var available = await db.StockPositions.AsNoTracking().Where(p => p.ItemId == input.ItemId && p.LocationId == input.LocationId)
            .Select(p => p.QuantityOnHand - p.QuantityReserved).FirstOrDefaultAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var applied = 0;
        var appliedQuantity = 0m;
        foreach (var shortfall in open)
        {
            if (shortfall.Quantity > available - appliedQuantity)
            {
                continue;
            }

            var posted = await movements.PostAsync(new StockPosting(
                StockMovementType.Issue, shortfall.ItemId, shortfall.LocationId, null, -shortfall.Quantity, null, StockSources.StockShortfall, shortfall.Id,
                Reason: "Falta da venda " + shortfall.SourceNumber, SourceNumber: shortfall.SourceNumber), cancellationToken);
            if (posted.IsFailure)
            {
                return posted.Error!;
            }

            shortfall.Apply(posted.Value.MovementId, currentUser.UserId, now);
            events.Publish(new StockShortfallApplied(
                shortfall.Id, shortfall.SourceType, shortfall.SourceId, shortfall.ItemId, shortfall.LocationId, shortfall.Quantity, posted.Value.MovementId, posted.Value.AverageCostAfter, now));
            applied++;
            appliedQuantity += shortfall.Quantity;
        }

        var remaining = open.Sum(s => s.Quantity) - appliedQuantity;
        if (applied == 0)
        {
            return Error.Conflict(
                StockLedger.InsufficientStockCode,
                "O disponível no local não cobre a falta mais antiga. Ajuste o estoque ou transfira do depósito e aplique de novo.");
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ApplyStockShortfallResult(applied, appliedQuantity, remaining);
    }
}
