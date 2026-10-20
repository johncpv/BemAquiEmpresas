using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed partial class QuoteService
{
    /// <summary>Custo, preço sugerido e composição por item (D7: só com <c>quotes.view_cost</c>).</summary>
    public async Task<Result<QuoteCostDetail>> CostsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await CanViewCostAsync(cancellationToken))
        {
            return NoCostAccess();
        }

        var quote = await db.Quotes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return quote is null ? NotFound() : await CostDetailAsync(quote, cancellationToken);
    }

    /// <summary>Custo e margem das linhas da lista (com <c>quotes.view_cost</c>).</summary>
    public async Task<Result<IReadOnlyList<QuoteCostSummary>>> CostSummariesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (!await CanViewCostAsync(cancellationToken))
        {
            return NoCostAccess();
        }

        var list = await db.Quotes.AsNoTracking().Where(q => ids.Contains(q.Id)).Select(q => new QuoteCostSummary(q.Id, q.TotalCost, q.MarginPct)).ToListAsync(cancellationToken);
        return list;
    }

    private async Task<QuoteCostDetail> CostDetailAsync(Quote quote, CancellationToken ct)
    {
        var quoteItems = await db.QuoteItems.AsNoTracking().Where(i => i.QuoteId == quote.Id).OrderBy(i => i.Sequence).ToListAsync(ct);
        var ids = quoteItems.Select(i => i.Id).ToList();
        var lines = await db.QuoteItemCosts.AsNoTracking().Where(c => ids.Contains(c.QuoteItemId)).OrderBy(c => c.CostType).ThenBy(c => c.Id).ToListAsync(ct);
        var stock = (await items.FindAsync(lines.Where(l => l.ItemId != null).Select(l => l.ItemId!.Value).Distinct().ToList(), ct)).ToDictionary(i => i.Id);
        var roles = (await positions.FindAsync(lines.Where(l => l.PositionId != null).Select(l => l.PositionId!.Value).Distinct().ToList(), ct)).ToDictionary(p => p.Id, p => p.Name);
        var defaultPct = await commercialParameters.DefaultPricingPctAsync(ct);
        return new QuoteCostDetail(quote.Id, quote.PricingMode, defaultPct, quote.TotalCost, quote.TotalPrice, quote.MarginPct, quoteItems.Select(i =>
        {
            var effective = i.Pct ?? defaultPct;
            var shown = i.PriceOverridden ? QuotePricing.PctOf(i.UnitCost, i.UnitPrice, quote.PricingMode) ?? 0 : effective ?? 0;
            return new QuoteItemCostView(i.Id, i.UnitCost, i.Pct, shown, effective is { } p ? QuotePricing.SuggestedPrice(i.UnitCost, quote.PricingMode, p) : null,
                lines.Where(l => l.QuoteItemId == i.Id).Select(l => new QuoteCostLineView(
                    l.Id, l.CostType, l.Description, l.ItemId, l.ItemId is { } s && stock.TryGetValue(s, out var si) ? si.Name : null, l.ItemId is { } u && stock.TryGetValue(u, out var ui) ? ui.UnitCode : null,
                    l.WorkCenterId, l.PositionId, l.PositionId is { } r ? roles.GetValueOrDefault(r) : null, l.PartyId, l.Quantity, l.UnitCost, l.Total)).ToList());
        }).ToList());
    }

    /// <summary>Linhas de mão de obra com custo-hora diferente do atual do cargo (CA 13), sem gravar.</summary>
    public async Task<Result<CostRefreshPreview>> RefreshPreviewAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await CanViewCostAsync(cancellationToken))
        {
            return NoCostAccess();
        }

        var quote = await db.Quotes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (quote is null)
        {
            return NotFound();
        }

        var (changes, newTotal) = await RefreshChangesAsync(quote, cancellationToken);
        return new CostRefreshPreview(changes, quote.TotalCost, newTotal);
    }

    /// <summary>"Atualizar custos" (CA 13): grava o custo-hora atual nas linhas; preços digitados à mão não mudam.</summary>
    public async Task<Result<QuoteCostDetail>> RefreshCostsAsync(Guid id, VersionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanViewCostAsync(cancellationToken))
        {
            return NoCostAccess();
        }

        var editable = await EditableAsync(id, input.Version, cancellationToken);
        if (editable.IsFailure)
        {
            return editable.Error!;
        }

        var quote = editable.Value;
        var (changes, _) = await RefreshChangesAsync(quote, cancellationToken);
        var lineIds = changes.Select(c => c.LineId).ToList();
        var tracked = await db.QuoteItemCosts.Where(c => lineIds.Contains(c.Id)).ToListAsync(cancellationToken);
        foreach (var line in tracked)
        {
            var change = changes.Single(c => c.LineId == line.Id);
            line.UnitCost = change.NewUnitCost;
            line.Total = change.NewTotal;
        }

        var itemIds = changes.Select(c => c.ItemId).Distinct().ToList();
        foreach (var item in await db.QuoteItems.Where(i => itemIds.Contains(i.Id)).ToListAsync(cancellationToken))
        {
            item.UnitCost = await db.QuoteItemCosts.Where(c => c.QuoteItemId == item.Id && !lineIds.Contains(c.Id)).SumAsync(c => c.Total, cancellationToken) + tracked.Where(t => t.QuoteItemId == item.Id).Sum(t => t.Total);
            if (!item.PriceOverridden && await SuggestAsync(quote, item, item.Pct, cancellationToken) is { } error)
            {
                return error;
            }
        }

        await RecalculateAsync(quote, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await CostDetailAsync(quote, cancellationToken);
    }

    private async Task<(List<CostRefreshLine> Changes, decimal NewTotal)> RefreshChangesAsync(Quote quote, CancellationToken ct)
    {
        var quoteItems = await db.QuoteItems.AsNoTracking().Where(i => i.QuoteId == quote.Id).ToListAsync(ct);
        var ids = quoteItems.Select(i => i.Id).ToList();
        var labor = await db.QuoteItemCosts.AsNoTracking().Where(c => ids.Contains(c.QuoteItemId) && c.CostType == CostType.Labor && c.PositionId != null).ToListAsync(ct);
        var names = (await positions.FindAsync(labor.Select(l => l.PositionId!.Value).Distinct().ToList(), ct)).ToDictionary(p => p.Id, p => p.Name);
        var changes = new List<CostRefreshLine>();
        foreach (var line in labor)
        {
            var current = Math.Round(await hourlyCost.ForPositionAsync(line.PositionId!.Value, ct) ?? line.UnitCost, 6, MidpointRounding.AwayFromZero);
            if (current != line.UnitCost)
            {
                var item = quoteItems.Single(i => i.Id == line.QuoteItemId);
                changes.Add(new CostRefreshLine(item.Id, item.Description, line.Id, names.GetValueOrDefault(line.PositionId!.Value), line.Quantity, line.UnitCost, current, line.Total,
                    QuotePricing.LineTotal(line.Quantity, current)));
            }
        }

        var delta = changes.GroupBy(c => c.ItemId).Sum(g => QuotePricing.Money(quoteItems.Single(i => i.Id == g.Key).Quantity * g.Sum(c => c.NewTotal - c.OldTotal)));
        return (changes, quote.TotalCost + delta);
    }
}
