using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>Orçamentos usam terceiros, itens e centros (COM-01a CA 22): em uso, o cadastro só é inativado.</summary>
public sealed class CommercialUsage(ICommercialDb db) : IPartyUsageProvider, IItemUsageProvider, IWorkCenterUsageProvider
{
    public async Task<string?> UsageAsync(Guid partyId, CancellationToken cancellationToken = default) =>
        await db.Contracts.AnyAsync(c => c.PartyId == partyId, cancellationToken)
            ? "contratos"
            : await db.Quotes.IgnoreQueryFilters().AnyAsync(q => q.PartyId == partyId, cancellationToken)
              || await db.QuoteItemCosts.AnyAsync(c => c.PartyId == partyId, cancellationToken)
                ? "orçamentos"
                : null;

    public Task<bool> HasMovementAsync(Guid itemId, CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<bool> HasBalanceAsync(Guid itemId, CancellationToken cancellationToken = default) => Task.FromResult(false);

    async Task<string?> IItemUsageProvider.UsageAsync(Guid itemId, CancellationToken cancellationToken) =>
        await db.QuoteItems.AnyAsync(i => i.ItemId == itemId, cancellationToken) || await db.QuoteItemCosts.AnyAsync(c => c.ItemId == itemId, cancellationToken)
            ? "orçamentos"
            : null;

    async Task<string?> IWorkCenterUsageProvider.UsageAsync(Guid workCenterId, CancellationToken cancellationToken) =>
        await db.QuoteItemCosts.AnyAsync(c => c.WorkCenterId == workCenterId, cancellationToken) ? "orçamentos" : null;
}
