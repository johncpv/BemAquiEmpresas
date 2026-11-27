using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Inventory.Contracts;
using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Sales.Application;

/// <summary>
/// Consumidor do estoque da venda (VND-01 CA 9 e 10, PDV-05). Roda na transação do handler (outbox + inbox, ADR 0006), nunca no caminho do caixa:
/// cada item sai do local da venda pelo <see cref="IStockMovements"/>, com origem <see cref="StockSource"/> e rastreio até a venda, e o custo
/// unitário da baixa (<c>decimal(18,6)</c>) vai para a linha. Idempotente: a inbox, a chave do motor (<c>Sale</c>, id da venda, perna, tipo) e a
/// falta única por venda e item impedem a segunda baixa, e uma venda já baixada não é tocada.
/// <list type="bullet">
/// <item>Canal sem caixa: a regra de saldo nunca negativo continua (ADR 0023); sem saldo, nada sai e a baixa fica com falta.</item>
/// <item>Venda do caixa (com sessão, PDV-05, opção B da PP-02): a mercadoria já saiu da loja, então a venda nunca é recusada; sai o que há em
/// saldo e o restante vira falta aberta no Estoque (<see cref="IStockShortfalls"/>), que o estoquista regulariza em Estoque → Divergências.
/// O motor e o <c>CHECK</c> do saldo não mudam.</item>
/// </list>
/// </summary>
public sealed class SaleStockConsumer(
    ISalesDb db, IStockMovements movements, IStockBalances balances, IStockShortfalls shortfalls, IItems items, IItemCosts costs, ISalesReservations reservations,
    TimeProvider clock)
    : IIntegrationEventConsumer<SaleCompleted>, IIntegrationEventConsumer<SaleCancelled>, IIntegrationEventConsumer<StockShortfallApplied>
{
    /// <summary>Origem dos movimentos de venda no estoque (<c>inventory.stock_movements.source_type</c>).</summary>
    public const string StockSource = StockSources.Sale;

    public async Task HandleAsync(SaleCompleted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var sale = await db.Sales.FirstOrDefaultAsync(s => s.Id == integrationEvent.SaleId, cancellationToken);
        if (sale is null || sale.StockStatus != SaleStockStatus.Pending || !ReturnRules.Done.Contains(sale.Status))
        {
            return;
        }

        var lines = await db.SaleLines.Where(l => l.SaleId == sale.Id && l.ItemId != null).OrderBy(l => l.LineNumber).ToListAsync(cancellationToken);
        // Serviço (item do tipo Serviço) não tem estoque: sai da baixa e fica sem custo.
        var info = (await items.FindAsync(lines.Select(l => l.ItemId!.Value).Distinct().ToList(), cancellationToken)).ToDictionary(i => i.Id);
        lines = lines.Where(l => info.GetValueOrDefault(l.ItemId!.Value)?.Type != ItemType.Service).ToList();
        if (lines.Count == 0)
        {
            sale.StockStatus = SaleStockStatus.NotRequired;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        // Uma perna por item (somando as linhas do mesmo item), na ordem do primeiro aparecimento: a perna é a chave de idempotência no motor.
        var perItem = lines.GroupBy(l => l.ItemId!.Value).Select((g, i) => (ItemId: g.Key, Leg: i + 1, Quantity: SaleRules.Quantity(g.Sum(l => l.Quantity)))).ToList();
        var now = clock.GetUtcNow();
        var postings = perItem.Select(p => new SaleStockPosting
        {
            SaleId = sale.Id, ItemId = p.ItemId, LocationId = sale.StockLocationId ?? Guid.Empty, Quantity = p.Quantity, CreatedAt = now,
        }).ToList();

        if (sale.CashSessionId is not null)
        {
            await PostFromRegisterAsync(sale, lines, perItem, postings, info, now, cancellationToken);
            return;
        }

        var blocked = Blocked(sale, perItem.Select(p => info.GetValueOrDefault(p.ItemId)).OfType<ItemSummary>().ToList());
        if (blocked is not null)
        {
            Park(sale, postings, SaleStockPostingStatus.Blocked, blocked);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var available = (await balances.GetAsync(perItem.Select(p => p.ItemId).ToList(), sale.StockLocationId, cancellationToken)).ToDictionary(a => a.ItemId, a => a.Available);
        // VND-02a CA 22: a venda do pedido de venda consome a própria reserva; o reservado para ela conta como disponível.
        if (sale.SourceType == SalesOrderRules.SourceType && sale.SourceId is { } orderId)
        {
            foreach (var held in (await reservations.ForSourceAsync(SalesOrderRules.SourceType, orderId, cancellationToken))
                .Where(r => r.Status == ReservationState.Active && r.LocationId == sale.StockLocationId))
            {
                available[held.ItemId] = available.GetValueOrDefault(held.ItemId) + held.Quantity;
            }
        }

        var missing = perItem.Where(p => available.GetValueOrDefault(p.ItemId) < p.Quantity).ToList();
        if (missing.Count > 0)
        {
            Park(sale, postings, SaleStockPostingStatus.Shortfall, "Saldo insuficiente no local da venda: " + missing.Count + " item(ns).");
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var requests = perItem.Select(p => Issue(sale, p.ItemId, p.Leg, p.Quantity)).ToList();
        var posted = await movements.PostManyAsync(requests, cancellationToken);
        if (posted.IsFailure)
        {
            // Saldo mudou entre a leitura e a baixa (outra venda, outro documento): a transação do handler é desfeita e o outbox tenta de novo.
            throw new InvalidOperationException("Baixa da venda " + sale.Number + " recusada: " + posted.Error!.Message);
        }

        var unitCosts = new Dictionary<Guid, decimal>();
        for (var i = 0; i < perItem.Count; i++)
        {
            postings[i].StockMovementId = posted.Value[i].MovementId;
            postings[i].UnitCost = posted.Value[i].AverageCostAfter;
            postings[i].Status = SaleStockPostingStatus.Posted;
            unitCosts[perItem[i].ItemId] = posted.Value[i].AverageCostAfter;
        }

        SetLineCosts(lines, unitCosts);
        db.SaleStockPostings.AddRange(postings);
        sale.StockStatus = SaleStockStatus.Posted;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Venda cancelada: primeiro a falta aberta da venda fecha sem movimento (o que nunca saiu não volta; a baixa já aplicada é estornada pelo
    /// Estoque), depois cada baixa feita volta ao estoque por estorno do próprio movimento (ADR 0023 §4), uma vez só (PDV-05 CA 9).
    /// </summary>
    public async Task HandleAsync(SaleCancelled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var sale = await db.Sales.FirstOrDefaultAsync(s => s.Id == integrationEvent.SaleId, cancellationToken);
        if (sale is null)
        {
            return;
        }

        var cancelled = await shortfalls.CancelSourceAsync(StockSource, sale.Id, cancellationToken);
        if (cancelled.IsFailure)
        {
            throw new InvalidOperationException("Cancelamento da falta da venda " + sale.Number + " recusado: " + cancelled.Error!.Message);
        }

        var postings = await db.SaleStockPostings.Where(p => p.SaleId == sale.Id && p.Status != SaleStockPostingStatus.Reversed).ToListAsync(cancellationToken);
        foreach (var posting in postings)
        {
            if (posting is { Status: SaleStockPostingStatus.Posted or SaleStockPostingStatus.Shortfall, StockMovementId: { } movementId })
            {
                var reversed = await movements.ReverseAsync(new StockReversal(movementId, StockSource, sale.Id, "Venda " + sale.Number + " cancelada"), cancellationToken);
                if (reversed.IsFailure)
                {
                    throw new InvalidOperationException("Estorno da baixa da venda " + sale.Number + " recusado: " + reversed.Error!.Message);
                }

                posting.ReversalMovementId = reversed.Value.MovementId;
            }

            posting.ShortfallQuantity = 0;
            posting.Status = SaleStockPostingStatus.Reversed;
        }

        if (sale.StockStatus is SaleStockStatus.Posted or SaleStockStatus.Shortfall or SaleStockStatus.Pending)
        {
            sale.StockStatus = SaleStockStatus.Reversed;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// A falta da venda foi aplicada no Estoque (PDV-05 CA 6B): a linha da baixa deixa de ter falta e, sem nenhuma falta na venda, a venda fica
    /// baixada. Se nada tinha saído antes, o custo da linha passa a ser o da baixa aplicada.
    /// </summary>
    public async Task HandleAsync(StockShortfallApplied integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.SourceType != StockSource)
        {
            return;
        }

        var posting = await db.SaleStockPostings.FirstOrDefaultAsync(
            p => p.SaleId == integrationEvent.SourceId && p.ItemId == integrationEvent.ItemId && p.Status == SaleStockPostingStatus.Shortfall, cancellationToken);
        var sale = await db.Sales.FirstOrDefaultAsync(s => s.Id == integrationEvent.SourceId, cancellationToken);
        if (posting is null || sale is null)
        {
            return;
        }

        posting.ShortfallQuantity = Math.Max(0, SaleRules.Quantity(posting.ShortfallQuantity - integrationEvent.Quantity));
        if (posting.StockMovementId is null)
        {
            posting.UnitCost = integrationEvent.UnitCost;
            var lines = await db.SaleLines.Where(l => l.SaleId == sale.Id && l.ItemId == integrationEvent.ItemId).ToListAsync(cancellationToken);
            SetLineCosts(lines, new Dictionary<Guid, decimal> { [integrationEvent.ItemId] = integrationEvent.UnitCost });
        }

        if (posting.ShortfallQuantity == 0)
        {
            posting.Status = SaleStockPostingStatus.Posted;
            posting.Note = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (sale.StockStatus == SaleStockStatus.Shortfall
            && !await db.SaleStockPostings.AnyAsync(p => p.SaleId == sale.Id && (p.Status == SaleStockPostingStatus.Shortfall || p.Status == SaleStockPostingStatus.Blocked), cancellationToken))
        {
            sale.StockStatus = SaleStockStatus.Posted;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Baixa da venda do caixa (PDV-05 CA 1, 5 e 6B): por item, sai o que o local tem disponível e o restante vira falta aberta no Estoque; nenhuma
    /// exceção de saldo sobe (a venda continua válida). Item com lote ou série, ou venda sem local, fica bloqueado com a venda em falta.
    /// </summary>
    private async Task PostFromRegisterAsync(
        Sale sale, List<SaleLine> lines, List<(Guid ItemId, int Leg, decimal Quantity)> perItem, List<SaleStockPosting> postings, Dictionary<Guid, ItemSummary> info,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var tracked = perItem.Where(p => sale.StockLocationId is null || info.GetValueOrDefault(p.ItemId) is { TracksLot: true } or { TracksSerial: true })
            .Select(p => p.ItemId).ToHashSet();
        var moving = perItem.Where(p => !tracked.Contains(p.ItemId)).ToList();
        var available = moving.Count == 0
            ? []
            : (await balances.GetAsync(moving.Select(p => p.ItemId).ToList(), sale.StockLocationId, cancellationToken)).ToDictionary(a => a.ItemId, a => a.Available);
        var split = moving.Select(p =>
            {
                var issued = SaleRules.Quantity(Math.Clamp(available.GetValueOrDefault(p.ItemId), 0, p.Quantity));
                return (p.ItemId, p.Leg, Issued: issued, Missing: SaleRules.Quantity(p.Quantity - issued));
            })
            .ToList();

        var issuing = split.Where(s => s.Issued > 0).ToList();
        var posted = await movements.PostManyAsync(issuing.Select(s => Issue(sale, s.ItemId, s.Leg, s.Issued)).ToList(), cancellationToken);
        if (posted.IsFailure)
        {
            // O saldo mudou entre a leitura e a baixa: a transação do handler é desfeita e o outbox tenta de novo, com o saldo novo.
            throw new InvalidOperationException("Baixa da venda " + sale.Number + " recusada: " + posted.Error!.Message);
        }

        var results = issuing.Select((s, i) => (s.ItemId, Result: posted.Value[i])).ToDictionary(r => r.ItemId, r => r.Result);
        foreach (var missing in split.Where(s => s.Missing > 0))
        {
            var opened = await shortfalls.OpenAsync(
                new StockShortfallInput(missing.ItemId, sale.StockLocationId!.Value, missing.Missing, StockSource, sale.Id, sale.Number, sale.CompletedAt ?? now), cancellationToken);
            if (opened.IsFailure)
            {
                throw new InvalidOperationException("Falta da venda " + sale.Number + " recusada: " + opened.Error!.Message);
            }
        }

        // Custo da linha (CA 8): o da baixa; sem baixa (nada em saldo), o custo médio vigente do item, que é o que a saída usaria.
        var averages = await costs.AverageCostsAsync(split.Where(s => s.Issued == 0).Select(s => s.ItemId).ToList(), cancellationToken);
        var unitCosts = new Dictionary<Guid, decimal>();
        foreach (var posting in postings)
        {
            if (tracked.Contains(posting.ItemId))
            {
                posting.Status = SaleStockPostingStatus.Blocked;
                posting.ShortfallQuantity = posting.Quantity;
                posting.Note = sale.StockLocationId is null ? "Venda sem local de estoque." : "Item com controle de lote ou série: baixe pelo estoque.";
                continue;
            }

            var part = split.Single(s => s.ItemId == posting.ItemId);
            posting.ShortfallQuantity = part.Missing;
            posting.Status = part.Missing > 0 ? SaleStockPostingStatus.Shortfall : SaleStockPostingStatus.Posted;
            posting.Note = part.Missing > 0 ? "Falta de saldo no local: aplique a baixa em Estoque, Divergências." : null;
            if (results.TryGetValue(posting.ItemId, out var result))
            {
                posting.StockMovementId = result.MovementId;
                posting.UnitCost = result.AverageCostAfter;
            }
            else if (averages.TryGetValue(posting.ItemId, out var average))
            {
                posting.UnitCost = average;
            }

            if (posting.UnitCost is { } cost)
            {
                unitCosts[posting.ItemId] = cost;
            }
        }

        SetLineCosts(lines, unitCosts);
        db.SaleStockPostings.AddRange(postings);
        sale.StockStatus = postings.Any(p => p.Status != SaleStockPostingStatus.Posted) ? SaleStockStatus.Shortfall : SaleStockStatus.Posted;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static StockPosting Issue(Sale sale, Guid itemId, int leg, decimal quantity) =>
        new(StockMovementType.Issue, itemId, sale.StockLocationId!.Value, null, -quantity, null, StockSource, sale.Id, leg, At: sale.CompletedAt, Reason: "Venda " + sale.Number,
            SourceNumber: sale.Number, ReservationSourceType: sale.SourceType == SalesOrderRules.SourceType ? SalesOrderRules.SourceType : null,
            ReservationSourceId: sale.SourceType == SalesOrderRules.SourceType ? sale.SourceId : null);

    private static void SetLineCosts(IEnumerable<SaleLine> lines, Dictionary<Guid, decimal> unitCosts)
    {
        foreach (var line in lines)
        {
            if (line.ItemId is { } itemId && unitCosts.TryGetValue(itemId, out var cost))
            {
                line.UnitCost = cost;
            }
        }
    }

    /// <summary>Item com lote ou série não sai pelo canal sem dizer qual lote ou série; sem local, nada sai.</summary>
    private static string? Blocked(Sale sale, IReadOnlyCollection<ItemSummary> stockItems)
    {
        if (sale.StockLocationId is null)
        {
            return "Venda sem local de estoque.";
        }

        var tracked = stockItems.Where(i => i.TracksLot || i.TracksSerial).ToList();
        return tracked.Count == 0 ? null : "Item com controle de lote ou série: " + string.Join(", ", tracked.Select(i => i.Code)) + ".";
    }

    private void Park(Sale sale, List<SaleStockPosting> postings, SaleStockPostingStatus status, string note)
    {
        foreach (var posting in postings)
        {
            posting.Status = status;
            posting.Note = note.Length <= 200 ? note : note[..200];
        }

        db.SaleStockPostings.AddRange(postings);
        sale.StockStatus = SaleStockStatus.Shortfall;
    }
}
