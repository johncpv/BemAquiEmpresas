using BemAqui.Engineering.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>
/// Orçamentos usam terceiros, itens e centros (COM-01a CA 22) e roteiros (PRD-01 CA 22, ENG-01 CA 10): em uso, o cadastro só é
/// inativado.
/// </summary>
public sealed class CommercialUsage(ICommercialDb db) : IPartyUsageProvider, IItemUsageProvider, IWorkCenterUsageProvider, IRoutingTemplateUsageProvider
{
    public string Noun => "itens de orçamento ou projeto";

    public async Task<IReadOnlyDictionary<Guid, int>> CountAsync(IReadOnlyCollection<Guid> templateIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(templateIds);
        var quoteItems = await db.QuoteItems.AsNoTracking().Where(i => i.RoutingTemplateId != null && templateIds.Contains(i.RoutingTemplateId.Value))
            .GroupBy(i => i.RoutingTemplateId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var projectItems = await db.ProjectItems.AsNoTracking().Where(i => i.RoutingTemplateId != null && templateIds.Contains(i.RoutingTemplateId.Value))
            .GroupBy(i => i.RoutingTemplateId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        return quoteItems.Concat(projectItems).GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Sum(x => x.Count));
    }

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
