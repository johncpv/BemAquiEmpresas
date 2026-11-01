using BemAqui.BuildingBlocks.Paging;
using BemAqui.Finance.Contracts;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application;

public sealed partial class GoodsReceiptService
{
    /// <summary>Detalhe do recebimento (CA 12, 15, 18): linhas com lote, local, custo de entrada e o movimento; as OS que receberam material (CA 7).</summary>
    public async Task<Result<GoodsReceiptDetail>> DetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var receipt = await db.GoodsReceipts.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (receipt is null)
        {
            return NotFound();
        }

        var order = await db.PurchaseOrders.AsNoTracking().Where(o => o.Id == receipt.PurchaseOrderId).Select(o => new { o.Number, o.ClosedBalanceAmount, o.Status }).FirstAsync(cancellationToken);
        var lines = await db.GoodsReceiptLines.AsNoTracking().Where(l => l.ReceiptId == id).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        var info = (await items.FindAsync(lines.Select(l => l.ItemId).Distinct().ToList(), cancellationToken)).ToDictionary(i => i.Id);
        var places = (await locations.FindAsync(lines.Select(l => l.LocationId).Distinct().ToList(), cancellationToken)).ToDictionary(l => l.Id);
        var party = await parties.FindAsync(receipt.PartyId, cancellationToken);
        var project = receipt.ProjectId is { } pid ? await projects.FindAsync(pid, cancellationToken) : null;
        var served = await ServedAsync(lines, cancellationToken);
        var canApprove = await permissions.HasPermissionAsync("purchasing", "approve", cancellationToken);
        return new GoodsReceiptDetail(
            receipt.Id, receipt.Number, receipt.Status, receipt.PurchaseOrderId, order.Number, receipt.PartyId, party is null ? "Fornecedor removido" : party.TradeName ?? party.LegalName,
            receipt.ProjectId, project?.Number, receipt.ReceivedOn, receipt.Subtotal, receipt.Freight, receipt.IpiAmount, receipt.Amount, receipt.Notes, receipt.ReceivedBy,
            receipt.ReceivedBy is { } by ? await users.DisplayNameAsync(by, cancellationToken) : null, receipt.SoleOwner, receipt.CreatedAt, receipt.ReversedAt,
            receipt.ReversedBy is { } rb ? await users.DisplayNameAsync(rb, cancellationToken) : null, receipt.ReversalReason,
            lines.Select(l => View(l, info.GetValueOrDefault(l.ItemId), places.GetValueOrDefault(l.LocationId)?.Name)).ToList(), served,
            canApprove && receipt.Status == GoodsReceiptStatus.Completed && order.ClosedBalanceAmount is null && order.Status != PurchaseOrderStatus.PendingApproval, receipt.Version,
            receipt.Origin, receipt.IncomingInvoiceId, receipt.Insurance, receipt.OtherExpenses, receipt.TaxCredit);
    }

    /// <summary>O recebimento pelo pedido da URL (IDOR, CA 16): recebimento de outro pedido é 404.</summary>
    public async Task<Result<GoodsReceiptDetail>> ForOrderAsync(Guid orderId, Guid receiptId, CancellationToken cancellationToken = default) =>
        await db.GoodsReceipts.AnyAsync(r => r.Id == receiptId && r.PurchaseOrderId == orderId, cancellationToken) ? await DetailAsync(receiptId, cancellationToken) : NotFound();

    /// <summary>Uma linha pelo recebimento da URL (IDOR, CA 16): linha de outro recebimento é 404.</summary>
    public async Task<Result<GoodsReceiptLineView>> LineAsync(Guid receiptId, Guid lineId, CancellationToken cancellationToken = default)
    {
        var line = await db.GoodsReceiptLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lineId && l.ReceiptId == receiptId, cancellationToken);
        if (line is null)
        {
            return Error.NotFound("goods_receipt_line_not_found", "Linha não encontrada neste recebimento.");
        }

        var found = await items.FindAsync([line.ItemId], cancellationToken);
        var item = found.Count > 0 ? found[0] : null;
        var places = await locations.FindAsync([line.LocationId], cancellationToken);
        var place = places.Count > 0 ? places[0] : null;
        return View(line, item, place?.Name);
    }

    /// <summary>Aba "Recebidos" (CA 15): número, data, pedido, fornecedor, itens, valor e situação; o mais recente primeiro.</summary>
    public async Task<PagedList<GoodsReceiptSummary>> ListAsync(GoodsReceiptListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);
        var receipts = db.GoodsReceipts.AsNoTracking();
        if (query.Status is { } status)
        {
            receipts = receipts.Where(r => r.Status == status);
        }

        if (query.PartyId is { } party)
        {
            receipts = receipts.Where(r => r.PartyId == party);
        }

        if (query.PurchaseOrderId is { } order)
        {
            receipts = receipts.Where(r => r.PurchaseOrderId == order);
        }

        if (!string.IsNullOrWhiteSpace(query.Term))
        {
            var term = query.Term.Trim().ToUpperInvariant();
            var matching = db.PurchaseOrders.AsNoTracking().Where(o => o.Number.Contains(term)).Select(o => o.Id);
            receipts = receipts.Where(r => r.Number.Contains(term) || matching.Contains(r.PurchaseOrderId));
        }

        var result = await receipts.OrderByDescending(r => r.ReceivedOn).ThenByDescending(r => r.Number).ToPagedListAsync(page, cancellationToken);
        return new PagedList<GoodsReceiptSummary>(await SummariesAsync(result.Items, cancellationToken), result.Page, result.PageSize, result.Total);
    }

    /// <summary>Aba Recebimentos do pedido (CA 15): os recebimentos dele, registrados e estornados.</summary>
    public async Task<Result<IReadOnlyList<GoodsReceiptSummary>>> ByOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        if (!await db.PurchaseOrders.AnyAsync(o => o.Id == orderId, cancellationToken))
        {
            return PurchaseOrderService.NotFound();
        }

        var rows = await db.GoodsReceipts.AsNoTracking().Where(r => r.PurchaseOrderId == orderId).OrderByDescending(r => r.ReceivedOn).ThenByDescending(r => r.Number).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<GoodsReceiptSummary>>.Success(await SummariesAsync(rows, cancellationToken));
    }

    /// <summary>Aba Financeiro do recebimento (com <c>finance.view</c>, CA 11 e 18): os títulos dele por parcela, com a OP quando há.</summary>
    public async Task<Result<IReadOnlyList<GoodsReceiptPayableView>>> PayablesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await permissions.HasPermissionAsync("finance", "view", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Os títulos do recebimento ficam no Financeiro. Fale com o administrador.");
        }

        if (!await db.GoodsReceipts.AnyAsync(r => r.Id == id, cancellationToken))
        {
            return NotFound();
        }

        return (await payables.ListByOriginAsync(DocumentOrigin.GoodsReceipt, id, cancellationToken))
            .Select(p => new GoodsReceiptPayableView(p.Id, p.Number, p.Installment, p.Status, p.Amount, p.DueDate, p.PaymentOrderId)).ToList();
    }

    /// <summary>Locais ativos para o recebimento, sem exigir as permissões do Estoque (o padrão primeiro).</summary>
    public async Task<IReadOnlyList<ReceiptLocationOption>> LocationOptionsAsync(CancellationToken cancellationToken = default) =>
        (await locations.ActiveAsync(cancellationToken)).OrderByDescending(l => l.IsDefault).ThenBy(l => l.Code, StringComparer.Ordinal)
        .Select(l => new ReceiptLocationOption(l.Id, l.Code, l.Name, l.IsDefault)).ToList();

    /// <summary>"Última compra" do item (CA 15): preço e fornecedor do último pedido aprovado; 404 sem compra.</summary>
    public async Task<Result<LastPurchaseView>> LastPurchaseAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        if (await new PurchaseOrdersContract(db).LastPurchaseAsync(itemId, cancellationToken) is not { } last)
        {
            return Error.NotFound("last_purchase_not_found", "Este item ainda não foi comprado.");
        }

        var party = await parties.FindAsync(last.PartyId, cancellationToken);
        return new LastPurchaseView(last.ItemId, last.PurchaseOrderId, last.PurchaseOrderNumber, last.PartyId, party is null ? "Fornecedor removido" : party.TradeName ?? party.LegalName,
            last.UnitPrice, last.ApprovedAt);
    }

    private async Task<IReadOnlyList<ServedOrderRef>> ServedAsync(IReadOnlyCollection<GoodsReceiptLine> lines, CancellationToken ct)
    {
        var numbersPosted = lines.Select(l => l.MovementNumber).OfType<string>().Distinct().ToList();
        var ids = numbersPosted.Count == 0 ? [] : await reservations.OrdersCompletedByEntriesAsync(numbersPosted, ct);
        if (ids.Count == 0)
        {
            return [];
        }

        return (await productionOrders.FindManyAsync(ids, ct)).OrderBy(o => o.Number, StringComparer.Ordinal).Select(o => new ServedOrderRef(o.Id, o.Number)).ToList();
    }

    private async Task<IReadOnlyList<GoodsReceiptSummary>> SummariesAsync(IReadOnlyCollection<GoodsReceipt> receipts, CancellationToken ct)
    {
        var ids = receipts.Select(r => r.Id).ToList();
        var orderIds = receipts.Select(r => r.PurchaseOrderId).Distinct().ToList();
        var orderNumbers = await db.PurchaseOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Number, ct);
        var lines = await db.GoodsReceiptLines.AsNoTracking().Where(l => ids.Contains(l.ReceiptId)).OrderBy(l => l.Sequence).Select(l => new { l.ReceiptId, l.ItemId }).ToListAsync(ct);
        var info = (await items.FindAsync(lines.Select(l => l.ItemId).Distinct().ToList(), ct)).ToDictionary(i => i.Id);
        var partyIds = receipts.Select(r => r.PartyId).Distinct().ToList();
        var partyInfo = partyIds.Count == 0 ? [] : (await parties.FindManyAsync(partyIds, ct)).ToDictionary(p => p.Id);
        return receipts.Select(r =>
        {
            var mine = lines.Where(l => l.ReceiptId == r.Id).ToList();
            var first = mine.Count > 0 ? info.GetValueOrDefault(mine[0].ItemId) : null;
            var summary = first is null ? "—" : $"{first.Code} {first.Name}{(mine.Count > 1 ? $" e mais {mine.Count - 1}" : string.Empty)}";
            var p = partyInfo.GetValueOrDefault(r.PartyId);
            return new GoodsReceiptSummary(r.Id, r.Number, r.Status, r.ReceivedOn, r.PurchaseOrderId, orderNumbers.GetValueOrDefault(r.PurchaseOrderId) ?? "—", r.PartyId,
                p is null ? "Fornecedor removido" : p.TradeName ?? p.LegalName, summary, r.Amount);
        }).ToList();
    }

    private static GoodsReceiptLineView View(GoodsReceiptLine l, BemAqui.Inventory.Contracts.ItemSummary? item, string? location) => new(
        l.Id, l.Sequence, l.PoLineId, l.ItemId, item?.Code ?? string.Empty, item?.Name ?? "Item removido", item?.UnitCode ?? string.Empty, l.Quantity, l.SupplierUnit, l.SupplierQuantity,
        l.LotNumber, l.LotExpiry, l.LocationId, location ?? "Local removido", l.UnitPrice, l.LineAmount, l.FreightShare, l.IpiShare, l.UnitCost, l.MovementId, l.MovementNumber,
        l.ReversalMovementId, l.InsuranceShare, l.OtherShare, l.TaxCreditShare);
}
