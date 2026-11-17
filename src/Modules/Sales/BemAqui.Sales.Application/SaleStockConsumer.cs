using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Inventory.Contracts;
using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Sales.Application;

/// <summary>
/// Consumidor do estoque da venda de canal sem caixa (VND-01 CA 9 e 10). Roda na transação do handler (outbox + inbox, ADR 0006): cada item sai do
/// local da venda pelo <see cref="IStockMovements"/>, com origem <see cref="StockSource"/> e rastreio até a venda, e o custo unitário da baixa
/// (<c>decimal(18,6)</c>) vai para a linha. Idempotente: a inbox e a chave do motor (<c>Sale</c>, id da venda, perna, tipo) impedem a segunda
/// baixa, e uma venda já baixada não é tocada. A regra de saldo nunca negativo continua (ADR 0023): sem saldo, nada sai e a baixa fica com falta
/// (<see cref="SaleStockPostingStatus.Shortfall"/>) para a PDV-05 e a PP-02 decidirem o tratamento. Venda do PDV (com sessão de caixa) fica
/// para a baixa agregada (SPK-03, PDV-05).
/// </summary>
public sealed class SaleStockConsumer(ISalesDb db, IStockMovements movements, IStockBalances balances, IItems items, TimeProvider clock)
    : IIntegrationEventConsumer<SaleCompleted>, IIntegrationEventConsumer<SaleCancelled>
{
    /// <summary>Origem dos movimentos de venda no estoque (<c>inventory.stock_movements.source_type</c>).</summary>
    public const string StockSource = StockSources.Sale;

    public async Task HandleAsync(SaleCompleted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var sale = await db.Sales.FirstOrDefaultAsync(s => s.Id == integrationEvent.SaleId, cancellationToken);
        if (sale is null || sale.CashSessionId is not null || sale.StockStatus != SaleStockStatus.Pending || sale.Status != SaleStatus.Completed)
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

        var blocked = Blocked(sale, perItem.Select(p => info.GetValueOrDefault(p.ItemId)).OfType<ItemSummary>().ToList());
        if (blocked is not null)
        {
            Park(sale, postings, SaleStockPostingStatus.Blocked, blocked);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var available = (await balances.GetAsync(perItem.Select(p => p.ItemId).ToList(), sale.StockLocationId, cancellationToken)).ToDictionary(a => a.ItemId, a => a.Available);
        var missing = perItem.Where(p => available.GetValueOrDefault(p.ItemId) < p.Quantity).ToList();
        if (missing.Count > 0)
        {
            Park(sale, postings, SaleStockPostingStatus.Shortfall, "Saldo insuficiente no local da venda: " + missing.Count + " item(ns).");
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var requests = perItem.Select(p => new StockPosting(StockMovementType.Issue, p.ItemId, sale.StockLocationId!.Value, null, -p.Quantity, null, StockSource, sale.Id, p.Leg,
            At: sale.CompletedAt, Reason: "Venda " + sale.Number, SourceNumber: sale.Number)).ToList();
        var posted = await movements.PostManyAsync(requests, cancellationToken);
        if (posted.IsFailure)
        {
            // Saldo mudou entre a leitura e a baixa (outra venda, outro documento): a transação do handler é desfeita e o outbox tenta de novo.
            throw new InvalidOperationException("Baixa da venda " + sale.Number + " recusada: " + posted.Error!.Message);
        }

        var costs = new Dictionary<Guid, decimal>();
        for (var i = 0; i < perItem.Count; i++)
        {
            postings[i].StockMovementId = posted.Value[i].MovementId;
            postings[i].UnitCost = posted.Value[i].AverageCostAfter;
            postings[i].Status = SaleStockPostingStatus.Posted;
            costs[perItem[i].ItemId] = posted.Value[i].AverageCostAfter;
        }

        foreach (var line in lines)
        {
            line.UnitCost = costs[line.ItemId!.Value];
        }

        db.SaleStockPostings.AddRange(postings);
        sale.StockStatus = SaleStockStatus.Posted;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Venda cancelada: cada baixa feita volta ao estoque por estorno do próprio movimento (ADR 0023 §4), uma vez só.</summary>
    public async Task HandleAsync(SaleCancelled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var sale = await db.Sales.FirstOrDefaultAsync(s => s.Id == integrationEvent.SaleId, cancellationToken);
        if (sale is null || sale.CashSessionId is not null)
        {
            return;
        }

        var postings = await db.SaleStockPostings.Where(p => p.SaleId == sale.Id && p.Status != SaleStockPostingStatus.Reversed).ToListAsync(cancellationToken);
        foreach (var posting in postings)
        {
            if (posting is { Status: SaleStockPostingStatus.Posted, StockMovementId: { } movementId })
            {
                var reversed = await movements.ReverseAsync(new StockReversal(movementId, StockSource, sale.Id, "Venda " + sale.Number + " cancelada"), cancellationToken);
                if (reversed.IsFailure)
                {
                    throw new InvalidOperationException("Estorno da baixa da venda " + sale.Number + " recusado: " + reversed.Error!.Message);
                }

                posting.ReversalMovementId = reversed.Value.MovementId;
            }

            posting.Status = SaleStockPostingStatus.Reversed;
        }

        if (sale.StockStatus is SaleStockStatus.Posted or SaleStockStatus.Shortfall or SaleStockStatus.Pending)
        {
            sale.StockStatus = SaleStockStatus.Reversed;
        }

        await db.SaveChangesAsync(cancellationToken);
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
