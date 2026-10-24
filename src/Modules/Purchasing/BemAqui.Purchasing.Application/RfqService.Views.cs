using BemAqui.BuildingBlocks.Paging;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application;

public sealed partial class RfqService
{
    public const string OpenOnes = "abertas";
    public const string All = "todas";

    /// <summary>
    /// Detalhe da cotação (CA 1 a 15): linhas com as SC de origem e a última compra, fornecedores com a resposta, quem lançou, os totais e
    /// o anexo, o comparativo (menor custo por linha e o menor total geral), o aviso "1 cotação", a prévia dos pedidos e o que o usuário pode
    /// fazer agora; 404 se não existe.
    /// </summary>
    public async Task<Result<RfqDetail>> DetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rfq = await db.Rfqs.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rfq is null)
        {
            return NotFound();
        }

        var lines = await db.RfqLines.AsNoTracking().Where(l => l.RfqId == id).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        var suppliers = await db.RfqSuppliers.AsNoTracking().Where(s => s.RfqId == id).OrderBy(s => s.Sequence).ToListAsync(cancellationToken);
        var quotes = await db.RfqQuotes.AsNoTracking().Where(q => q.RfqId == id).ToListAsync(cancellationToken);
        var links = await (from r in db.RfqLineRequests.AsNoTracking()
                           join l in db.PurchaseRequestLines.AsNoTracking() on r.RequestLineId equals l.Id
                           join s in db.PurchaseRequests.AsNoTracking() on l.RequestId equals s.Id
                           where r.RfqId == id
                           orderby s.Number, l.Sequence
                           select new { r.RfqLineId, Ref = new RfqRequestRef(s.Id, s.Number, l.Id, r.Quantity) }).ToListAsync(cancellationToken);
        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var itemInfo = (await items.FindAsync(itemIds, cancellationToken)).ToDictionary(i => i.Id);
        var comparison = Compare(lines, suppliers, quotes);
        var costs = comparison.Costs.ToDictionary(c => (c.LineId, c.SupplierId));
        var partyNames = await PartyNamesAsync(suppliers.Select(s => s.PartyId).ToList(), cancellationToken);
        var termNames = new Dictionary<Guid, string?>();
        foreach (var termId in suppliers.Select(s => s.PaymentTermId).OfType<Guid>().Distinct())
        {
            termNames[termId] = (await terms.FindAsync(termId, cancellationToken))?.Name;
        }

        var userNames = new Dictionary<Guid, string?>();
        foreach (var user in suppliers.Select(s => s.RespondedBy).Append(rfq.CreatedBy).Append(rfq.ClosedBy).Append(rfq.CancelledBy).OfType<Guid>().Distinct())
        {
            userNames[user] = await users.DisplayNameAsync(user, cancellationToken);
        }

        var lastPurchases = await LastPurchasesAsync(itemIds, cancellationToken);
        var generated = await GeneratedOrdersAsync(id, cancellationToken);
        var limit = await parameters.QuoteAttachmentAmountAsync(cancellationToken);
        var quotesCount = QuotesCount(suppliers, quotes);
        var supplierViews = new List<RfqSupplierView>();
        foreach (var s in suppliers)
        {
            var totals = comparison.Suppliers.FirstOrDefault(t => t.SupplierId == s.Id);
            var units = await supplierCodes.ForItemsAsync(s.PartyId, itemIds, cancellationToken);
            var order = generated.GetValueOrDefault(s.PartyId);
            supplierViews.Add(new RfqSupplierView(
                s.Id, s.PartyId, partyNames.GetValueOrDefault(s.PartyId) ?? "Fornecedor removido", s.Sequence, s.Responded, s.Declined, s.RespondedBy,
                s.RespondedBy is { } by ? userNames.GetValueOrDefault(by) : null, s.RespondedAt, s.Freight, s.IpiPct, s.PaymentTermId,
                s.PaymentTermId is { } tid ? termNames.GetValueOrDefault(tid) : null, s.ValidUntil, s.Notes, totals?.Subtotal ?? 0m, totals?.IpiAmount ?? 0m, totals?.Total ?? 0m,
                totals?.MaxLeadDays, totals?.CoversAll ?? false, comparison.CheapestOverall == s.Id, totals is not null && RfqRules.NeedsAttachment(totals.Total, limit),
                await AttachmentsCountAsync(s.Id, cancellationToken), order?.Id, order?.Number, s.OrderSubmittedAt, s.OrderSubmitError,
                quotes.Where(q => q.RfqSupplierId == s.Id).Select(q =>
                {
                    var cost = costs.GetValueOrDefault((q.RfqLineId, s.Id));
                    return new RfqQuoteView(q.RfqLineId, q.UnitPrice, q.SupplierUnitPrice, q.SupplierUnit, q.AvailableQuantity, q.LeadDays, cost?.LineTotal ?? 0m, cost?.LandedCost ?? 0m,
                        comparison.CheapestByLine.TryGetValue(q.RfqLineId, out var c) && c == s.Id);
                }).ToList(),
                units.Values.Where(u => !string.IsNullOrWhiteSpace(u.SupplierUnit) && u.ConversionFactor > 0m).Select(u => new RfqSupplierUnit(u.ItemId, u.SupplierUnit!, u.ConversionFactor)).ToList()));
        }

        var lineViews = lines.Select(l =>
        {
            var item = itemInfo.GetValueOrDefault(l.ItemId);
            return new RfqLineView(
                l.Id, l.Sequence, l.ItemId, item?.Code ?? string.Empty, item?.Name ?? "Item removido", item?.UnitCode ?? string.Empty, l.Quantity,
                links.Where(x => x.RfqLineId == l.Id).Select(x => x.Ref).ToList(), comparison.CheapestByLine.TryGetValue(l.Id, out var c) ? c : null, l.SelectedSupplierId,
                l.SuggestedSupplierId, l.SelectionReason, l.SelectionNote, lastPurchases.GetValueOrDefault(l.ItemId));
        }).ToList();

        var preview = rfq.Status == RfqStatus.Closed
            ? suppliers.Where(s => lines.Any(l => l.SelectedSupplierId == s.Id)).Select(s =>
            {
                var won = lines.Where(l => l.SelectedSupplierId == s.Id).ToList();
                var total = RfqRules.SupplierTotal(won.Select(l => (l.Quantity, quotes.First(q => q.RfqLineId == l.Id && q.RfqSupplierId == s.Id).UnitPrice)), s.Freight, s.IpiPct).Total;
                var order = generated.GetValueOrDefault(s.PartyId);
                return new RfqOrderPreview(s.Id, s.PartyId, partyNames.GetValueOrDefault(s.PartyId) ?? "Fornecedor removido", won.Count, total, order?.Id, order?.Number, order?.Status, order?.Total);
            }).ToList()
            : [];

        var canEdit = await permissions.HasPermissionAsync("purchasing", "edit", cancellationToken);
        var canCreate = await permissions.HasPermissionAsync("purchasing", "create", cancellationToken);
        var open = rfq.Status == RfqStatus.Open;
        return new RfqDetail(
            rfq.Id, rfq.Number, rfq.Status, rfq.Notes, rfq.CreatedBy, rfq.CreatedBy is { } cb ? userNames.GetValueOrDefault(cb) : null, rfq.CreatedAt, rfq.ClosedAt,
            rfq.ClosedBy is { } closer ? userNames.GetValueOrDefault(closer) : null, rfq.CancelReason, rfq.CancelledAt, rfq.CancelledBy is { } canceller ? userNames.GetValueOrDefault(canceller) : null,
            lineViews, supplierViews, quotesCount, comparison.CheapestOverall, RfqRules.SingleQuoteWarning(rfq.Status, quotesCount), limit, preview, canEdit && open,
            canEdit && open && quotes.Count > 0, canEdit && open, canCreate && preview.Any(p => p.PurchaseOrderId is null),
            (canCreate || canEdit) && preview.Any(p => p.PurchaseOrderStatus == PurchaseOrderStatus.Draft), rfq.Version);
    }

    /// <summary>Lista "Cotações": por padrão as abertas; a mais recente primeiro.</summary>
    public async Task<PagedList<RfqSummary>> ListAsync(RfqListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);
        var rfqs = db.Rfqs.AsNoTracking();
        if ((query.Show ?? OpenOnes) == OpenOnes && query.Status is null)
        {
            rfqs = rfqs.Where(r => r.Status == RfqStatus.Open);
        }

        if (query.Status is { } status)
        {
            rfqs = rfqs.Where(r => r.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Term))
        {
            var term = query.Term.Trim().ToUpperInvariant();
            rfqs = rfqs.Where(r => r.Number.Contains(term));
        }

        var result = await rfqs.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Number).ToPagedListAsync(page, cancellationToken);
        var ids = result.Items.Select(r => r.Id).ToList();
        var lines = await db.RfqLines.AsNoTracking().Where(l => ids.Contains(l.RfqId)).OrderBy(l => l.Sequence).Select(l => new { l.RfqId, l.ItemId }).ToListAsync(cancellationToken);
        var suppliers = await db.RfqSuppliers.AsNoTracking().Where(s => ids.Contains(s.RfqId)).Select(s => new { s.RfqId, s.Responded, s.Declined }).ToListAsync(cancellationToken);
        var itemInfo = (await items.FindAsync(lines.Select(l => l.ItemId).Distinct().ToList(), cancellationToken)).ToDictionary(i => i.Id);
        var rows = result.Items.Select(r =>
        {
            var mine = lines.Where(l => l.RfqId == r.Id).ToList();
            var first = mine.Count > 0 ? itemInfo.GetValueOrDefault(mine[0].ItemId) : null;
            var summary = first is null ? "—" : $"{first.Code} {first.Name}{(mine.Count > 1 ? $" e mais {mine.Count - 1}" : string.Empty)}";
            var theirs = suppliers.Where(s => s.RfqId == r.Id).ToList();
            return new RfqSummary(r.Id, r.Number, r.Status, summary, mine.Count, theirs.Count, theirs.Count(s => s.Responded || s.Declined), r.CreatedAt, r.ClosedAt);
        }).ToList();
        return new PagedList<RfqSummary>(rows, result.Page, result.PageSize, result.Total);
    }

    /// <summary>
    /// "Cotar" preenchido (CA 1, R-PRP-1): os itens das linhas de SC em aberto (quantidade somada) com a última compra e os fornecedores
    /// sugeridos: os últimos fornecedores desses itens (pedido aprovado ou além), ativos e com o papel Fornecedor, até 3.
    /// </summary>
    public async Task<Result<RfqDefaults>> DefaultsAsync(IReadOnlyCollection<Guid>? requestLineIds, CancellationToken cancellationToken = default)
    {
        var ids = requestLineIds ?? [];
        var requestLines = ids.Count == 0 ? [] : await (from l in db.PurchaseRequestLines.AsNoTracking()
                                                        join r in db.PurchaseRequests.AsNoTracking() on l.RequestId equals r.Id
                                                        where ids.Contains(l.Id) && l.Status == PurchaseRequestStatus.Open
                                                        orderby r.Number, l.Sequence
                                                        select new { Line = l, r.Number }).ToListAsync(cancellationToken);
        if (ids.Count > 0 && requestLines.Count == 0)
        {
            return Error.Conflict(PurchaseRequestService.LineNotOpenCode, "As linhas escolhidas já estão em cotação, viraram pedido ou foram canceladas.");
        }

        var itemIds = requestLines.Select(r => r.Line.ItemId).Distinct().ToList();
        var found = (await items.FindAsync(itemIds, cancellationToken)).Where(i => i.IsActive && !i.IsDeleted).ToList();
        IReadOnlyDictionary<Guid, IReadOnlyList<ItemUnitOption>> units = found.Count == 0 ? new Dictionary<Guid, IReadOnlyList<ItemUnitOption>>() : await itemUnits.ForItemsAsync(found.Select(i => i.Id).ToList(), cancellationToken);
        var last = await LastPurchasesAsync(itemIds, cancellationToken);
        var lines = found.OrderBy(i => itemIds.IndexOf(i.Id)).Select(i =>
        {
            var mine = requestLines.Where(r => r.Line.ItemId == i.Id).ToList();
            var option = new PurchaseItemOption(i.Id, i.Code, i.Name, i.Type, i.UnitId, i.UnitCode, units.GetValueOrDefault(i.Id) ?? [new ItemUnitOption(i.UnitId, i.UnitCode, true, 1m)]);
            return new RfqDefaultLine(option, mine.Sum(r => r.Line.Quantity), mine.Select(r => r.Line.Id).ToList(), mine.Select(r => r.Number).Distinct().ToList(), last.GetValueOrDefault(i.Id));
        }).ToList();
        return new RfqDefaults(lines, await SuggestedSuppliersAsync(itemIds, cancellationToken));
    }

    /// <summary>Os últimos fornecedores dos itens (R-PRP-1): pelo pedido aprovado mais recente de cada um, sem repetir, até 3.</summary>
    private async Task<IReadOnlyList<RfqSupplierOption>> SuggestedSuppliersAsync(List<Guid> itemIds, CancellationToken ct)
    {
        if (itemIds.Count == 0)
        {
            return [];
        }

        var counted = PurchaseOrderRules.CountsForReference.Where(s => s != PurchaseOrderStatus.PendingApproval).ToList();
        var recent = await (from l in db.PurchaseOrderLines.AsNoTracking()
                            join o in db.PurchaseOrders.AsNoTracking() on l.OrderId equals o.Id
                            where itemIds.Contains(l.ItemId) && counted.Contains(o.Status) && o.ApprovedAt != null
                            select new { o.PartyId, o.ApprovedAt }).ToListAsync(ct);
        var result = new List<RfqSupplierOption>();
        foreach (var partyId in recent.OrderByDescending(r => r.ApprovedAt).Select(r => r.PartyId).Distinct())
        {
            if (result.Count == RfqRules.MaxSuggestedSuppliers)
            {
                break;
            }

            var party = await parties.FindAsync(partyId, ct);
            if (party is { IsActive: true, IsDeleted: false } && party.Roles.Contains(PartyRoleKind.Supplier))
            {
                result.Add(new RfqSupplierOption(party.Id, party.TradeName ?? party.LegalName, party.Document, party.DefaultPaymentTermId, true));
            }
        }

        return result;
    }

    /// <summary>Última compra de cada item (CMP-04): preço, data e fornecedor do último pedido aprovado (ou além).</summary>
    private async Task<Dictionary<Guid, RfqLastPurchase>> LastPurchasesAsync(List<Guid> itemIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, RfqLastPurchase>();
        var contract = new PurchaseOrdersContract(db);
        foreach (var itemId in itemIds)
        {
            if (await contract.LastPurchaseAsync(itemId, ct) is { } last)
            {
                var party = await parties.FindAsync(last.PartyId, ct);
                result[itemId] = new RfqLastPurchase(last.UnitPrice, last.ApprovedAt, party is null ? "Fornecedor removido" : party.TradeName ?? party.LegalName, last.PurchaseOrderNumber);
            }
        }

        return result;
    }
}
