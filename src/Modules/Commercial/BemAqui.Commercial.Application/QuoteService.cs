using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Finance.Contracts;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.HR.Contracts;
using BemAqui.Identity.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>
/// Orçamentos (COM-01a): cabeçalho, itens, composição de custo e preço. Sem efeito financeiro nem de estoque (PRD §5.4).
/// Custo e margem só saem pelas rotas de custo, com <c>quotes.view_cost</c> (D7).
/// </summary>
public sealed partial class QuoteService(
    ICommercialDb db, IParties parties, IItems items, IHourlyCost hourlyCost, IPositions positions, IWorkCenters workCenters, ITenantParameters tenantParameters,
    ICommercialParameters commercialParameters, INumberSequence numbers, IPermissionChecker permissions, ICurrentUser currentUser, IPaymentTerms paymentTerms, IEventPublisher events,
    IUserDirectory users, TimeProvider clock)
{
    public const string DocType = "ORC";

    public async Task<QuotePage> ListAsync(string? q, string? situation, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        var quotes = db.Quotes.AsNoTracking().Where(x => x.IsCurrent);
        quotes = situation switch
        {
            "rascunho" => quotes.Where(x => x.Status == QuoteStatus.Draft),
            "enviado" => quotes.Where(x => x.Status == QuoteStatus.Sent),
            "perdido" => quotes.Where(x => x.Status == QuoteStatus.Rejected),
            "expirado" => quotes.Where(x => x.Status == QuoteStatus.Expired),
            _ => quotes,
        };
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var matches = (await parties.SearchAsync(term, PartyRoleKind.Customer, 50, cancellationToken)).Select(p => p.Id).ToList();
            quotes = quotes.Where(x => x.Number.Contains(term) || matches.Contains(x.PartyId));
        }

        var total = await quotes.CountAsync(cancellationToken);
        var rows = await quotes.OrderByDescending(x => x.CreatedAt).Skip(page.Skip).Take(page.PageSize)
            .Select(x => new { Quote = x, Main = db.QuoteItems.Where(i => i.QuoteId == x.Id).OrderBy(i => i.Sequence).Select(i => i.Description).FirstOrDefault(), Revisions = db.Quotes.Count(o => o.Number == x.Number) })
            .ToListAsync(cancellationToken);
        var names = await NamesAsync(rows.Select(r => r.Quote.PartyId), cancellationToken);
        return new QuotePage(
            rows.Select(r => new QuoteSummary(r.Quote.Id, r.Quote.Number, r.Quote.Revision, r.Quote.Status, r.Quote.PartyId, names.GetValueOrDefault(r.Quote.PartyId, string.Empty), r.Main,
                r.Quote.TotalPrice, r.Quote.SentAt, r.Quote.CreatedAt, r.Quote.ValidUntil, r.Quote.LostReason, r.Revisions)).ToList(),
            page.Page, page.PageSize, total);
    }

    public async Task<Result<QuoteDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var quote = await db.Quotes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return quote is null ? NotFound() : await DetailAsync(quote, cancellationToken);
    }

    private async Task<QuoteDetail> DetailAsync(Quote quote, CancellationToken ct)
    {
        var lines = await db.QuoteItems.AsNoTracking().Where(i => i.QuoteId == quote.Id).OrderBy(i => i.Sequence).ToListAsync(ct);
        var itemNames = (await items.FindAsync(lines.Where(l => l.ItemId != null).Select(l => l.ItemId!.Value).Distinct().ToList(), ct)).ToDictionary(i => i.Id, i => i.Name);
        var party = await parties.FindAsync(quote.PartyId, ct);
        return new QuoteDetail(
            quote.Id, quote.Number, quote.Revision, quote.Status, quote.PartyId, party is null ? string.Empty : party.TradeName ?? party.LegalName, quote.ContactId, quote.SiteAddressId,
            quote.DeliveryDays, quote.Notes, quote.PricingMode,
            lines.Select(l => new QuoteItemView(l.Id, l.Sequence, l.Kind, l.ItemId, l.ItemId is { } i ? itemNames.GetValueOrDefault(i) : null, l.Description, l.Quantity, l.UnitPrice,
                l.PriceOverridden, QuotePricing.Money(l.Quantity * l.UnitPrice))).ToList(),
            quote.TotalPrice, quote.SentAt, quote.CreatedAt, quote.ValidUntil, quote.LostReason, quote.LostDetail,
            quote.IsCurrent, quote.ParentQuoteId,
            quote.CopiedFromQuoteId is { } from ? await db.Quotes.IgnoreQueryFilters().Where(x => x.Id == from).Select(x => x.Number).FirstOrDefaultAsync(ct) : null, quote.Version);
    }

    /// <summary>Refaz custo, preço e margem do orçamento com as fórmulas do CA 15 (o servidor grava, o front só mostra).</summary>
    private async Task RecalculateAsync(Quote quote, CancellationToken ct)
    {
        // Carrega os itens no contexto: os novos e os removidos ainda não gravados entram certo na conta.
        await db.QuoteItems.Where(i => i.QuoteId == quote.Id).LoadAsync(ct);
        var all = db.QuoteItems.Local.Where(i => i.QuoteId == quote.Id && db.Entry(i).State != EntityState.Deleted).ToList();
        (quote.TotalCost, quote.TotalPrice, quote.MarginPct) = QuotePricing.Totals(all.Select(i => (i.Quantity, i.UnitCost, i.UnitPrice)));
        await RecalculateMilestonesAsync(quote, ct);
    }

    /// <summary>Rascunho editável, na versão que o usuário abriu (CA 17 e 21).</summary>
    private async Task<Result<Quote>> EditableAsync(Guid id, uint version, CancellationToken ct)
    {
        var quote = await db.Quotes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (quote is null)
        {
            return NotFound();
        }

        if (!quote.IsCurrent)
        {
            return NotCurrent();
        }

        if (quote.Status == QuoteStatus.Rejected)
        {
            return Error.Conflict("quote_lost", "Orçamento perdido não é alterado.");
        }

        if (quote.Status != QuoteStatus.Draft)
        {
            return Error.Conflict("quote_sent", "Orçamento enviado não é alterado. Crie uma nova revisão.");
        }

        if (quote.Version != version)
        {
            return Error.Conflict("concurrency_conflict", "O orçamento mudou desde que você abriu. Recarregue.");
        }

        db.Entry(quote).Property(x => x.Version).OriginalValue = version;
        return quote;
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken ct) =>
        (await parties.FindManyAsync(ids.Distinct().ToList(), ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);

    private ValueTask<bool> CanViewCostAsync(CancellationToken ct) => permissions.HasPermissionAsync("quotes", "view_cost", ct);

    private static T? First<T>(IReadOnlyList<T> list) where T : class => list.Count > 0 ? list[0] : null;

    internal static Error NotFound() => Error.NotFound("quote_not_found", "Orçamento não encontrado.");

    internal static Error Invalid(string field, string message) =>
        Error.Validation("validation", message, new Dictionary<string, string[]> { [field] = [message] });

    private static Error NoCostAccess() => Error.Forbidden(Error.Codes.Forbidden, "Custo e margem do orçamento são restritos. Fale com o administrador.");
}
