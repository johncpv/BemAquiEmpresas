using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.Finance.Contracts;
using BemAqui.Fiscal.Domain;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Purchasing.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Fiscal.Application.Incoming;

public sealed partial class IncomingInvoiceService
{
    /// <summary>Até quantos itens sem de-para recebem sugestão por abertura da nota (uma consulta cada).</summary>
    public const int MaxSuggestedItems = 60;

    /// <summary>Fiscal → Notas de entrada: da importada mais recente para a mais antiga, com o filtro de situação.</summary>
    public async Task<IncomingInvoiceList> ListAsync(IncomingInvoiceStatus? status, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        var query = db.IncomingInvoices.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(i => i.Status == s);
        }

        var rows = await query.OrderByDescending(i => i.ImportedAt)
            .Select(i => new IncomingInvoiceSummary(
                i.Id, i.Number, i.Series, i.AccessKey, i.IssuerName, i.IssuerDocument, i.IssuedAt, i.Total, i.Status, i.WithoutOrder, i.PurchaseOrderId != null,
                db.IncomingInvoiceItems.Count(x => x.InvoiceId == i.Id),
                db.IncomingInvoiceItems.Count(x => x.InvoiceId == i.Id && !(x.NoStockItem ? x.ExpenseAccountId != null : x.ItemId != null && x.ConversionFactor != null)),
                i.ImportedAt))
            .ToPagedListAsync(page, cancellationToken);
        return new IncomingInvoiceList(rows.Items, rows.Page, rows.PageSize, rows.Total, await permissions.HasPermissionAsync("fiscal", "import_incoming", cancellationToken));
    }

    /// <summary>A nota aberta (CA 3, 6 a 12): fornecedor, itens com o de-para e a sugestão, pedido (valores só com <c>purchasing.view</c>).</summary>
    public async Task<Result<IncomingInvoiceView>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await db.IncomingInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null)
        {
            return NotFound();
        }

        var lines = await db.IncomingInvoiceItems.AsNoTracking().Where(i => i.InvoiceId == id).OrderBy(i => i.Sequence).ToListAsync(cancellationToken);
        var can = new IncomingInvoicePermissions(
            await permissions.HasPermissionAsync("fiscal", "import_incoming", cancellationToken),
            await permissions.HasPermissionAsync("purchasing", StandardActions.Approve, cancellationToken),
            await permissions.HasPermissionAsync("purchasing", StandardActions.View, cancellationToken),
            await permissions.HasPermissionAsync("parties", StandardActions.Create, cancellationToken),
            await permissions.HasPermissionAsync("parties", StandardActions.Edit, cancellationToken),
            await permissions.HasPermissionAsync("inventory", StandardActions.Create, cancellationToken),
            await permissions.HasPermissionAsync("purchasing", "receive", cancellationToken));

        // Fornecedor (CA 6): o ligado; sem vínculo, o do CNPJ (o cadastro pode ter sido feito depois; a próxima ação liga).
        var party = invoice.PartyId is { } pid ? await parties.FindAsync(pid, cancellationToken) : await parties.FindByDocumentAsync(invoice.IssuerDocument, cancellationToken);
        var supplier = party is null || party.IsDeleted
            ? new IncomingSupplierView(IncomingSupplierState.NotFound, null, null, false)
            : new IncomingSupplierView(party.Roles.Contains(PartyRoleKind.Supplier) ? IncomingSupplierState.Found : IncomingSupplierState.NotSupplier, party.Id,
                party.TradeName is { Length: > 0 } t ? t : party.LegalName, party.IsActive);

        var itemIds = lines.Where(l => l.ItemId is not null).Select(l => l.ItemId!.Value).ToHashSet();
        var linked = invoice.PurchaseOrderId is { } orderId ? await orders.FindAsync(orderId, cancellationToken) : null;
        var options = invoice.IsEditable && supplier.PartyId is { } supplierId ? await orders.OpenForPartyAsync(supplierId, cancellationToken) : [];
        foreach (var l in linked?.Lines ?? [])
        {
            itemIds.Add(l.ItemId);
        }

        var catalogItems = (await items.FindAsync(itemIds, cancellationToken)).ToDictionary(i => i.Id);
        var accountIds = lines.Where(l => l.ExpenseAccountId is not null).Select(l => l.ExpenseAccountId!.Value).Distinct().ToList();
        var accountNames = accountIds.Count == 0 ? new Dictionary<Guid, string>() : (await accounts.FindAsync(accountIds, cancellationToken)).ToDictionary(a => a.Id, a => a.Code + " " + a.Name);

        var views = new List<IncomingInvoiceItemView>(lines.Count);
        var suggested = 0;
        foreach (var line in lines)
        {
            IReadOnlyList<ItemSuggestion> hints = [];
            if (invoice.IsEditable && line.ItemId is null && !line.NoStockItem && suggested < MaxSuggestedItems)
            {
                hints = await suggestions.SuggestAsync(line.Ncm, line.Description, 3, cancellationToken);
                suggested++;
            }

            var item = line.ItemId is { } iid ? catalogItems.GetValueOrDefault(iid) : null;
            var poLine = line.PoLineId is { } lid ? linked?.Lines.FirstOrDefault(l => l.LineId == lid) : null;
            var comparison = can.ViewOrder && poLine is not null && line.StockQuantity is { } q && line.StockUnitPrice is { } p
                ? IncomingInvoiceRules.Compare(poLine.OpenQuantity, q, poLine.UnitPrice, p)
                : null;
            views.Add(new IncomingInvoiceItemView(
                line.Id, line.Sequence, line.SupplierCode, line.Description, line.Ncm, line.Cfop, line.Unit, line.Quantity, line.UnitPrice, line.Amount, line.Taxes, line.LotNumber,
                line.OrderNumber, line.ItemId, item?.Code, item?.Name, item?.UnitCode, line.ConversionFactor, line.ItemId is not null && line.ConversionFactor is null,
                line.StockQuantity, line.StockUnitPrice, line.NoStockItem, line.ExpenseAccountId, line.ExpenseAccountId is { } aid ? accountNames.GetValueOrDefault(aid) : null,
                line.MappingSource, hints, line.PoLineId, poLine?.Sequence, comparison, line.IsResolved));
        }

        var order = OrderView(invoice, linked, options, itemIds, lines, catalogItems, can.ViewOrder);
        var blocked = invoice.IsEditable ? ViewBlockedReason(invoice, lines, supplier, can) : null;
        return new IncomingInvoiceView(
            invoice.Id, invoice.AccessKey, invoice.Number, invoice.Series, invoice.IssuedAt, invoice.Status,
            new IncomingIssuerView(invoice.IssuerDocument, invoice.IssuerName, invoice.IssuerTradeName, invoice.IssuerStateRegistration, invoice.IssuerAddress), supplier,
            invoice.Totals, invoice.Duplicates, invoice.Protocol, invoice.AuthorizedAt, invoice.SignerDocument, invoice.SefazStatusChecked, order,
            invoice.WithoutOrder, invoice.WithoutOrderReason, await NameAsync(invoice.WithoutOrderApprovedBy, cancellationToken), views, invoice.Divergences,
            lines.Count(l => !l.IsResolved), blocked, await NameAsync(invoice.ImportedBy, cancellationToken), invoice.ImportedAt,
            await NameAsync(invoice.CheckedBy, cancellationToken), invoice.CheckedAt, invoice.RejectedReason, await NameAsync(invoice.RejectedBy, cancellationToken), invoice.RejectedAt,
            invoice.XmlAttachmentId, can, invoice.GoodsReceiptId, invoice.InputTaxCreditApplied, invoice.InputTaxCreditAmount);
    }

    /// <summary>Itens do estoque para o seletor do de-para (ativos, fora Serviço), sem custo nem saldo.</summary>
    public async Task<IReadOnlyList<IncomingItemOption>> ItemOptionsAsync(string? q, CancellationToken cancellationToken = default) =>
        (await items.SearchAsync(null, q, 30, cancellationToken)).Where(i => i.Type != ItemType.Service).Take(20)
            .Select(i => new IncomingItemOption(i.Id, i.Code, i.Name, i.UnitCode, i.Ncm)).ToList();

    /// <summary>Contas de despesa analíticas e ativas para o item "Sem item de estoque" (CA 7).</summary>
    public async Task<IReadOnlyList<IncomingAccountOption>> AccountOptionsAsync(CancellationToken cancellationToken = default) =>
        (await accounts.PostableAsync(AccountType.Expense, cancellationToken)).Select(a => new IncomingAccountOption(a.Id, a.Code, a.Name)).ToList();

    private static IncomingOrderView OrderView(
        IncomingInvoice invoice, PurchaseOrderWithLines? linked, IReadOnlyList<PurchaseOrderWithLines> options, IReadOnlySet<Guid> itemIds, IReadOnlyList<IncomingInvoiceItem> lines,
        IReadOnlyDictionary<Guid, ItemSummary> catalogItems, bool values)
    {
        var mapped = lines.Where(l => l.ItemId is not null).Select(l => l.ItemId!.Value).ToHashSet();
        var cited = invoice.OrderReferences.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var choices = options.Select(o => (Order: o, Common: o.Lines.Select(l => l.ItemId).Distinct().Count(mapped.Contains), Cited: cited.Contains(o.Number))).ToList();

        // Sugestão (CA 9): o pedido citado na nota (xPed ou informação complementar); senão o com mais itens em comum.
        (PurchaseOrderWithLines Order, int Common, bool Cited)? suggestion = null;
        string? reason = null;
        if (linked is null && !invoice.WithoutOrder && choices.Count > 0)
        {
            if (choices.FirstOrDefault(c => c.Cited) is { Order: not null } byNumber)
            {
                suggestion = byNumber;
                reason = $"O pedido {byNumber.Order.Number} é citado na nota.";
            }
            else if (choices.OrderByDescending(c => c.Common).FirstOrDefault() is { Common: > 0 } byItems)
            {
                suggestion = byItems;
                reason = byItems.Common == 1 ? "O pedido tem 1 item em comum com a nota." : $"O pedido tem {byItems.Common} itens em comum com a nota.";
            }
        }

        var orderOptions = choices.Select(c => new IncomingOrderOption(c.Order.Id, c.Order.Number, c.Order.Status, c.Order.ExpectedDate, c.Common, suggestion?.Order.Id == c.Order.Id, c.Cited)).ToList();
        var lineOptions = linked?.Lines.Select(l => new IncomingOrderLineOption(
            l.LineId, l.Sequence, l.ItemId, catalogItems.GetValueOrDefault(l.ItemId)?.Code, catalogItems.GetValueOrDefault(l.ItemId)?.Name,
            values ? l.OpenQuantity : null, values ? l.UnitPrice : null)).ToList() ?? [];
        return new IncomingOrderView(
            linked?.Id, linked?.Number, linked?.Status, suggestion?.Order.Id, suggestion?.Order.Number, reason, values,
            values && linked is not null ? IncomingInvoiceRules.CompareTotal(linked.Freight, invoice.Totals.Freight) : null,
            values && linked is not null ? IncomingInvoiceRules.CompareTotal(linked.IpiAmount, invoice.Totals.Ipi) : null,
            orderOptions, lineOptions);
    }

    /// <summary>O motivo do "Conferir nota" desabilitado (CA 11): sem pedido e sem <c>purchasing.approve</c> vem primeiro.</summary>
    private static string? ViewBlockedReason(IncomingInvoice invoice, IReadOnlyList<IncomingInvoiceItem> lines, IncomingSupplierView supplier, IncomingInvoicePermissions can)
    {
        if (!can.Import)
        {
            return "Conferir a nota pede a permissão de importar nota de entrada.";
        }

        if (invoice.WithoutOrder && !can.ApproveWithoutOrder)
        {
            return IncomingInvoiceRules.WithoutOrderBlockedMessage;
        }

        return BlockedReason(supplier.State == IncomingSupplierState.Found, invoice, lines);
    }

    private async Task<string?> NameAsync(Guid? userId, CancellationToken ct) => userId is { } id ? await users.DisplayNameAsync(id, ct) : null;
}
