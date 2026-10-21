using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed partial class QuoteService
{
    /// <summary>
    /// "Copiar de…" (CA 13 e 16): novo rascunho com itens, composições, preços e marcos; cliente, contato e obra escolhidos
    /// (padrão: os do original); validade recalculada; o original não muda.
    /// </summary>
    public async Task<Result<QuoteDetail>> CopyAsync(Guid sourceId, QuoteCopyInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var source = await db.Quotes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sourceId, cancellationToken);
        if (source is null)
        {
            return NotFound();
        }

        var partyId = input.PartyId ?? source.PartyId;
        if (await PartyErrorAsync(partyId, cancellationToken) is { } partyError)
        {
            return partyError;
        }

        var sameParty = partyId == source.PartyId;
        var contact = input.ContactId ?? (sameParty ? source.ContactId : null);
        var site = input.SiteAddressId ?? (sameParty ? source.SiteAddressId : null);
        if (await LinkErrorAsync(partyId, contact, site, cancellationToken) is { } linkError)
        {
            return linkError;
        }

        var quote = new Quote
        {
            PartyId = partyId, ContactId = contact, SiteAddressId = site, DeliveryDays = source.DeliveryDays, Notes = source.Notes, PricingMode = source.PricingMode,
            ValidUntil = BusinessTime.Today(clock).AddDays(await commercialParameters.ValidityDaysAsync(cancellationToken)), CopiedFromQuoteId = source.Id,
            CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
        };
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        quote.Number = await numbers.NextAsync(DocType, cancellationToken);
        db.Quotes.Add(quote);
        await CopyLinesAsync(source, quote, null, keepPrices: true, cancellationToken);
        var milestones = await db.QuoteMilestones.AsNoTracking().Where(m => m.QuoteId == source.Id).OrderBy(m => m.Sequence).ToListAsync(cancellationToken);
        db.QuoteMilestones.AddRange(milestones.Select(m => new QuoteMilestone
        {
            QuoteId = quote.Id, Sequence = m.Sequence, Description = m.Description, Percent = m.Percent, Amount = m.Amount, Trigger = m.Trigger, TriggerEvent = m.TriggerEvent,
            DueDate = m.DueDate, DaysAfterEvent = m.DaysAfterEvent,
        }));
        await RecalculateAsync(quote, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(quote, cancellationToken);
    }

    /// <summary>"Copiar itens de…" (CA 14): acrescenta os itens escolhidos com as composições; preço sugerido pelo modo e % atuais.</summary>
    public async Task<Result<QuoteDetail>> CopyItemsAsync(Guid id, QuoteCopyItemsInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var editable = await EditableAsync(id, input.Version, cancellationToken);
        if (editable.IsFailure)
        {
            return editable.Error!;
        }

        var quote = editable.Value;
        var source = await db.Quotes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == input.SourceQuoteId, cancellationToken);
        if (source is null)
        {
            return Invalid("sourceQuoteId", "Orçamento de origem não encontrado.");
        }

        if (await CopyLinesAsync(source, quote, input.ItemIds, keepPrices: false, cancellationToken) is { } error)
        {
            return error;
        }

        await RecalculateAsync(quote, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(quote, cancellationToken);
    }

    /// <summary>Orçamentos para "Copiar de…": os mais recentes, com os itens, de qualquer cliente e situação.</summary>
    public async Task<IReadOnlyList<QuoteCopyOption>> CopyOptionsAsync(string? q, CancellationToken cancellationToken = default)
    {
        var page = await ListAsync(q, null, new PageRequest(1, 20), cancellationToken: cancellationToken);
        var ids = page.Items.Select(i => i.Id).ToList();
        var lines = await db.QuoteItems.AsNoTracking().Where(i => ids.Contains(i.QuoteId)).OrderBy(i => i.Sequence)
            .Select(i => new { i.QuoteId, i.Id, i.Description, i.Quantity }).ToListAsync(cancellationToken);
        return page.Items.Select(s => new QuoteCopyOption(s.Id, s.Number, s.Revision, s.PartyName, s.Status, s.TotalPrice,
            lines.Where(l => l.QuoteId == s.Id).Select(l => new QuoteCopyItemOption(l.Id, l.Description, l.Quantity)).ToList())).ToList();
    }

    private async Task<Error?> CopyLinesAsync(Quote source, Quote target, IReadOnlyList<Guid>? only, bool keepPrices, CancellationToken ct)
    {
        var sourceItems = await db.QuoteItems.AsNoTracking().Where(i => i.QuoteId == source.Id && (only == null || only.Contains(i.Id))).OrderBy(i => i.Sequence).ToListAsync(ct);
        if (only is not null && sourceItems.Count != only.Count)
        {
            return Invalid("itemIds", "Um dos itens não é do orçamento de origem.");
        }

        var ids = sourceItems.Select(i => i.Id).ToList();
        var costs = await db.QuoteItemCosts.AsNoTracking().Where(c => ids.Contains(c.QuoteItemId)).ToListAsync(ct);
        var next = (await db.QuoteItems.Where(i => i.QuoteId == target.Id).Select(i => (int?)i.Sequence).MaxAsync(ct) ?? 0) + 1;
        foreach (var s in sourceItems)
        {
            var item = new QuoteItem
            {
                QuoteId = target.Id, Sequence = next++, Kind = s.Kind, ItemId = s.ItemId, Description = s.Description, Quantity = s.Quantity, UnitCost = s.UnitCost, Pct = s.Pct,
                UnitPrice = s.UnitPrice, PriceOverridden = s.PriceOverridden,
            };
            db.QuoteItems.Add(item);
            db.QuoteItemCosts.AddRange(costs.Where(c => c.QuoteItemId == s.Id).Select(c => new QuoteItemCost
            {
                QuoteItemId = item.Id, CostType = c.CostType, Description = c.Description, ItemId = c.ItemId, WorkCenterId = c.WorkCenterId, PositionId = c.PositionId, PartyId = c.PartyId,
                Quantity = c.Quantity, UnitCost = c.UnitCost, Total = c.Total,
            }));
            if (!keepPrices && !item.PriceOverridden && await SuggestAsync(target, item, item.Pct, ct) is { } error)
            {
                return error;
            }
        }

        return null;
    }
}
