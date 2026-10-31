using System.Globalization;
using BemAqui.Finance.Contracts;
using BemAqui.Fiscal.Contracts;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application;

/// <summary>
/// Recebimento pela NF-e de entrada conferida (FIS-04b, ADR 0024 §1): é o recebimento da CMP-04 com os valores da nota. A nota é lida e
/// passa a <c>Received</c> pelo <see cref="IIncomingInvoiceReceipts"/>; os títulos nascem pelo <see cref="IPayables.CreateOpenInstallmentsAsync"/>
/// com as duplicatas da nota, na mesma transação da entrada no estoque. Preço: o do pedido; diferença só pelo "Corrigir preço" (CA 3).
/// </summary>
public sealed partial class GoodsReceiptService
{
    public const string InvoiceWithoutOrderCode = "incoming_invoice_without_order";
    public const string InvoiceLotsCode = "incoming_invoice_lots";
    public const string PriceCorrectionNotification = "purchasing.price_correction";
    public const string WithoutOrderMessage = "Ligue a nota a um pedido para receber.";
    public const string NotCheckedMessage = "Confira a nota antes de receber.";

    /// <summary>Uma linha do pedido com os itens da nota ligados a ela: quantidade (Σ, unidade de estoque), preço médio da nota, lote e tributos.</summary>
    private sealed record InvoiceLine(
        PurchaseOrderLine PoLine, decimal Quantity, decimal InvoicePrice, string? Lot, IReadOnlyList<string> Lots, IReadOnlyList<IncomingReceiptItem> Items, decimal Icms, decimal Pis,
        decimal Cofins);

    private sealed record InvoiceDraft(IncomingInvoiceForReceipt Invoice, PurchaseOrder Order, List<PurchaseOrderLine> Lines, IReadOnlyList<InvoiceLine> Stock,
        IReadOnlyList<IncomingReceiptItem> NoStock, decimal StockIpi);

    /// <summary>
    /// "Receber pela nota" preenchido (CA 1, 3, 5 e 7; R-PRP-2): com a nota conferida e ligada a um pedido, as linhas com a quantidade da
    /// nota, o lote sugerido, o local padrão, frete, seguro, outras despesas e IPI da nota e o motivo quando não pode receber (preço diferente,
    /// segregação, pedido em reaprovação). Recebida: o recebimento ligado. Sem pedido ou não conferida: o motivo.
    /// </summary>
    public async Task<Result<InvoiceReceiptDefaults>> InvoiceDefaultsAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var invoice = await incomingInvoices.FindForReceiptAsync(invoiceId, cancellationToken);
        if (invoice is null)
        {
            return InvoiceNotFound();
        }

        var active = await locations.ActiveAsync(cancellationToken);
        var fallback = await locations.DefaultAsync(cancellationToken);
        var options = active.OrderByDescending(l => l.IsDefault).ThenBy(l => l.Code, StringComparer.Ordinal).Select(l => new ReceiptLocationOption(l.Id, l.Code, l.Name, l.IsDefault)).ToList();
        var defaultLocation = fallback is { IsActive: true } ? fallback.Id : active.Count > 0 ? active[0].Id : (Guid?)null;
        var noStock = await NoStockLinesAsync(invoice, cancellationToken);
        InvoiceReceiptDefaults Empty(string? blocker, string? code) => new(
            invoice.Id, invoice.Number, invoice.Series, invoice.IssuerName, invoice.IssuedOn, invoice.Stage, invoice.GoodsReceiptId, invoice.Version, invoice.PurchaseOrderId, null, 0, null,
            Today, defaultLocation, options, [], noStock, invoice.Freight, invoice.Insurance, invoice.OtherExpenses, invoice.Ipi, 0m, 0m, invoice.Total, invoice.Duplicates,
            invoice.Credit.Label, invoice.Credit.Confirmed, false, false, blocker, code);

        if (invoice.Stage == IncomingInvoiceStage.Received)
        {
            return Empty($"A NF-e {invoice.Number} já foi recebida.", IncomingInvoiceReceiptCodes.Received);
        }

        if (invoice.Stage != IncomingInvoiceStage.Checked)
        {
            return Empty(NotCheckedMessage, IncomingInvoiceReceiptCodes.NotChecked);
        }

        var drafted = await DraftAsync(invoice, tracked: false, cancellationToken);
        if (drafted.IsFailure)
        {
            return drafted.Error!.Code == InvoiceWithoutOrderCode ? Empty(WithoutOrderMessage, InvoiceWithoutOrderCode) : drafted.Error!;
        }

        var d = drafted.Value;
        var order = d.Order;
        var info = (await items.FindAsync(d.Stock.Select(s => s.PoLine.ItemId).Distinct().ToList(), cancellationToken)).ToDictionary(i => i.Id);
        var tolerance = await parameters.ReceiptToleranceAsync(cancellationToken);
        var used = await UsedAsync(order.Id, cancellationToken);
        var subtotal = d.Stock.Sum(s => GoodsReceiptRules.LineAmount(s.Quantity, s.PoLine.UnitPrice));
        var completes = d.Lines.All(l => l.QuantityReceived + d.Stock.Where(s => s.PoLine.Id == l.Id).Sum(s => s.Quantity) >= l.Quantity);
        var lines = d.Stock.Select(s =>
        {
            var item = info.GetValueOrDefault(s.PoLine.ItemId);
            var receivedTotal = s.PoLine.QuantityReceived + s.PoLine.QuantityOverReceived;
            return new InvoiceReceiptDefaultLine(
                s.PoLine.Id, s.PoLine.Sequence, s.PoLine.ItemId, item?.Code ?? string.Empty, item?.Name ?? "Item removido", item?.UnitCode ?? string.Empty, item?.TracksLot ?? false,
                s.Quantity, Math.Max(0m, s.PoLine.Quantity - s.PoLine.QuantityReceived), GoodsReceiptRules.MaxQuantity(s.PoLine.Quantity, receivedTotal, tolerance), s.PoLine.UnitPrice,
                s.InvoicePrice, InvoiceReceiptRules.PriceDiffers(s.PoLine.UnitPrice, s.InvoicePrice), s.Lot, s.Items.Select(i => i.Sequence).ToList());
        }).ToList();
        var priceBlocked = lines.Any(l => l.PriceDiffers);
        var segregation = await orders.ReceiveSegregationAsync(order, cancellationToken);
        var blocker = priceBlocked ? InvoiceReceiptRules.PriceBlockedMessage : await orders.ReceiveBlockerAsync(order, d.Lines, cancellationToken);
        var project = order.ProjectId is { } pid ? await projects.FindAsync(pid, cancellationToken) : null;
        return new InvoiceReceiptDefaults(
            invoice.Id, invoice.Number, invoice.Series, invoice.IssuerName, invoice.IssuedOn, invoice.Stage, invoice.GoodsReceiptId, invoice.Version, order.Id, order.Number, order.Version,
            project?.Number, Today, defaultLocation, options, lines, noStock, invoice.Freight, invoice.Insurance, invoice.OtherExpenses, d.StockIpi,
            GoodsReceiptRules.ProportionalShare(order.Freight, order.Subtotal, subtotal, used.Freight, completes),
            GoodsReceiptRules.ProportionalShare(order.IpiAmount, order.Subtotal, subtotal, used.Ipi, completes), invoice.Total, invoice.Duplicates, invoice.Credit.Label,
            invoice.Credit.Confirmed, priceBlocked, segregation is { Involved: true, SoleOwner: true }, blocker, priceBlocked ? InvoiceReceiptRules.PriceDifferenceCode : null);
    }

    /// <summary>
    /// Impacto do "Receber pela nota" (CA 5 a 9) para o diálogo grande, sem gravar: as conferências do registro, por item o custo de
    /// entrada e o custo médio antes → depois, os títulos por duplicata (ou pela condição do pedido), os da parte sem item de estoque, a regra
    /// de crédito e o previsto do pedido antes → depois.
    /// </summary>
    public async Task<Result<InvoiceReceiptPreview>> InvoicePreviewAsync(Guid invoiceId, InvoiceReceiptInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var prepared = await PrepareInvoiceAsync(invoiceId, input, tracked: false, cancellationToken);
        if (prepared.IsFailure)
        {
            return prepared.Error!;
        }

        var (draft, p) = prepared.Value;
        var planned = await InvoicePlanAsync(draft, p.Amount, cancellationToken);
        if (planned.IsFailure)
        {
            return planned.Error!;
        }

        var (plan, fromDuplicates, count) = planned.Value;
        var names = await AccountNamesAsync(plan, cancellationToken);
        var receipt = await BuildPreviewAsync(p, plan.Receipt.Select(i => new GoodsReceiptInstallmentPreview(i.Number, i.DueDate, i.Amount)).ToList(), cancellationToken);
        var noStock = plan.NoStock.SelectMany(part => part.Installments.Select(i => new InvoiceNoStockInstallmentPreview(
            names.GetValueOrDefault(part.AccountId) ?? "Conta removida", Position(i.Number, count), count, i.DueDate, i.Amount))).ToList();
        return new InvoiceReceiptPreview(receipt, noStock, draft.Invoice.Credit.Label, fromDuplicates, plan.Mismatch, draft.Invoice.Total);
    }

    /// <summary>
    /// "Receber pela nota" (CA 6, <c>purchasing.receive</c>, com <c>Idempotency-Key</c>): o recebimento da CMP-04 com a origem
    /// <c>Invoice</c> e a nota ligada; na mesma transação a entrada no estoque, o recebido do pedido, os títulos pelas duplicatas (e os da parte
    /// sem item de estoque), o replanejamento do previsto e a nota <c>Received</c>. A mesma nota não é recebida duas vezes (409 com o link).
    /// </summary>
    public async Task<Result<GoodsReceiptDetail>> RegisterFromInvoiceAsync(Guid invoiceId, InvoiceReceiptInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("purchasing", "receive", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Quem recebe pela nota é quem tem a permissão Registrar recebimento em Compras.");
        }

        var prepared = await PrepareInvoiceAsync(invoiceId, input, tracked: true, cancellationToken);
        if (prepared.IsFailure)
        {
            return prepared.Error!;
        }

        var (draft, p) = prepared.Value;
        if (draft.Invoice.Version != input.InvoiceVersion)
        {
            return Error.Conflict("concurrency_conflict", "A nota mudou desde que você abriu. Recarregue.");
        }

        if (p.Order.Version != input.OrderVersion)
        {
            return Stale();
        }

        var planned = await InvoicePlanAsync(draft, p.Amount, cancellationToken);
        if (planned.IsFailure)
        {
            return planned.Error!;
        }

        var names = await AccountNamesAsync(planned.Value.Plan, cancellationToken);
        return await CommitAsync(p, input.OrderVersion, Clean(input.Notes), new InvoiceCommit(draft.Invoice, planned.Value.Plan, names, planned.Value.Count), cancellationToken);
    }

    /// <summary>
    /// "Pedir correção de preço" (CA 3): avisa quem tem <c>purchasing.approve</c> com o link para o "Corrigir preço" do pedido (CMP-04 CA 14),
    /// com o preço da nota sugerido. Quem recebe nunca corrige o preço (ADR 0024 §3).
    /// </summary>
    public async Task<Result<PriceCorrectionRequestResult>> RequestPriceCorrectionAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        if (!await permissions.HasPermissionAsync("purchasing", "receive", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Quem pede a correção é quem recebe pela nota (Registrar recebimento em Compras).");
        }

        var invoice = await incomingInvoices.FindForReceiptAsync(invoiceId, cancellationToken);
        if (invoice is null)
        {
            return InvoiceNotFound();
        }

        if (invoice.Stage != IncomingInvoiceStage.Checked)
        {
            return Error.Conflict(IncomingInvoiceReceiptCodes.NotChecked, "Só a nota conferida, ainda não recebida, pede a correção do preço.");
        }

        var drafted = await DraftAsync(invoice, tracked: false, cancellationToken);
        if (drafted.IsFailure)
        {
            return drafted.Error!;
        }

        var d = drafted.Value;
        var different = d.Stock.Where(s => InvoiceReceiptRules.PriceDiffers(s.PoLine.UnitPrice, s.InvoicePrice)).ToList();
        if (different.Count == 0)
        {
            return PurchaseOrderService.Invalid("lines", "Os preços da nota batem com os do pedido: não há o que corrigir.");
        }

        var info = (await items.FindAsync(different.Select(s => s.PoLine.ItemId).Distinct().ToList(), cancellationToken)).ToDictionary(i => i.Id);
        var detail = string.Join("; ", different.Select(s =>
            $"{info.GetValueOrDefault(s.PoLine.ItemId)?.Code ?? "item"}: pedido {PurchaseOrderRules.Money(s.PoLine.UnitPrice)}, nota {PurchaseOrderRules.Money(s.InvoicePrice)}"));
        var first = different[0];
        var link = $"/app/compras/pedidos/{d.Order.Id}?aba=itens&corrigir={first.PoLine.Id}&preco={first.InvoicePrice.ToString(CultureInfo.InvariantCulture)}";
        var message = $"NF-e {invoice.Number} de {invoice.IssuerName}: o preço da nota é diferente do pedido {d.Order.Number} ({detail}). Corrija o preço para receber pela nota.";
        var approvers = (await users.UsersWithPermissionAsync("purchasing.approve", cancellationToken)).Where(u => u != currentUser.UserId).Distinct().ToList();
        foreach (var approver in approvers)
        {
            notifications.Notify(approver, PriceCorrectionNotification, message.Length > 480 ? message[..479] + "…" : message, link);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new PriceCorrectionRequestResult(approvers.Count, different.Select(s => s.PoLine.Id).ToList());
    }

    /// <summary>
    /// A nota conferida e ligada ao pedido, com as linhas do pedido e os itens da nota agrupados por linha (vários itens na mesma linha somam a
    /// quantidade; lotes diferentes na mesma linha: 422). O IPI da parte de estoque é o da nota menos o dos itens sem item de estoque.
    /// </summary>
    private async Task<Result<InvoiceDraft>> DraftAsync(IncomingInvoiceForReceipt invoice, bool tracked, CancellationToken ct)
    {
        if (invoice.WithoutOrder || invoice.PurchaseOrderId is not { } orderId)
        {
            return Error.Conflict(InvoiceWithoutOrderCode, WithoutOrderMessage);
        }

        var order = tracked
            ? await db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct)
            : await db.PurchaseOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null)
        {
            return Error.Conflict(InvoiceWithoutOrderCode, WithoutOrderMessage);
        }

        var lines = tracked
            ? await db.PurchaseOrderLines.Where(l => l.OrderId == orderId).OrderBy(l => l.Sequence).ToListAsync(ct)
            : await db.PurchaseOrderLines.AsNoTracking().Where(l => l.OrderId == orderId).OrderBy(l => l.Sequence).ToListAsync(ct);
        var stockItems = invoice.Items.Where(i => !i.NoStockItem).ToList();
        if (stockItems.FirstOrDefault(i => i.PoLineId is null || i.StockQuantity is not > 0m || lines.All(l => l.Id != i.PoLineId)) is { } loose)
        {
            return PurchaseOrderService.Invalid("lines", $"O item {loose.Sequence} da nota não está ligado a uma linha deste pedido. Confira a nota de novo.");
        }

        var grouped = new List<InvoiceLine>();
        foreach (var group in stockItems.GroupBy(i => i.PoLineId!.Value))
        {
            var poLine = lines.First(l => l.Id == group.Key);
            var quantity = group.Sum(i => i.StockQuantity!.Value);
            var value = group.Sum(i => i.StockQuantity!.Value * (i.StockUnitPrice ?? 0m));
            var lots = group.Select(i => i.LotNumber?.Trim()).OfType<string>().Where(l => l.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            grouped.Add(new InvoiceLine(poLine, Math.Round(quantity, 4, MidpointRounding.AwayFromZero),
                quantity <= 0m ? 0m : Math.Round(value / quantity, 6, MidpointRounding.AwayFromZero), lots.FirstOrDefault(), lots, group.ToList(), group.Sum(i => i.Icms),
                group.Sum(i => i.Pis), group.Sum(i => i.Cofins)));
        }

        var noStock = invoice.Items.Where(i => i.NoStockItem).ToList();
        var stockIpi = Math.Max(0m, invoice.Ipi - noStock.Sum(i => i.Ipi));
        return new InvoiceDraft(invoice, order, lines, grouped.OrderBy(g => g.PoLine.Sequence).ToList(), noStock, stockIpi);
    }

    /// <summary>
    /// As conferências do recebimento pela nota: a nota existe, está conferida e não recebida (409 com o link), ligada a pedido, os preços batem
    /// com o pedido (CA 3) e um lote por linha; depois as mesmas do recebimento da CMP-04 (CA 2 e 4) com os valores da nota.
    /// </summary>
    private async Task<Result<(InvoiceDraft Draft, Prepared Prepared)>> PrepareInvoiceAsync(Guid invoiceId, InvoiceReceiptInput input, bool tracked, CancellationToken ct)
    {
        var invoice = await incomingInvoices.FindForReceiptAsync(invoiceId, ct);
        if (invoice is null)
        {
            return InvoiceNotFound();
        }

        if (invoice.Stage == IncomingInvoiceStage.Received)
        {
            return Error.Conflict(IncomingInvoiceReceiptCodes.Received, $"A NF-e {invoice.Number} já foi recebida.") with
            {
                Data = new Dictionary<string, object?>
                {
                    ["goodsReceiptId"] = invoice.GoodsReceiptId,
                    ["link"] = invoice.GoodsReceiptId is { } r ? Link(r) : IncomingInvoiceReceiptCodes.ScreenLink + invoice.Id,
                },
            };
        }

        if (invoice.Stage != IncomingInvoiceStage.Checked)
        {
            return Error.Conflict(IncomingInvoiceReceiptCodes.NotChecked, NotCheckedMessage);
        }

        var drafted = await DraftAsync(invoice, tracked, ct);
        if (drafted.IsFailure)
        {
            return drafted.Error!;
        }

        var d = drafted.Value;
        if (d.Stock.Count == 0)
        {
            return PurchaseOrderService.Invalid("lines", "A nota não tem item de estoque para receber.");
        }

        if (d.Stock.Any(s => InvoiceReceiptRules.PriceDiffers(s.PoLine.UnitPrice, s.InvoicePrice)))
        {
            return Error.Validation(InvoiceReceiptRules.PriceDifferenceCode, InvoiceReceiptRules.PriceBlockedMessage,
                new Dictionary<string, string[]> { ["lines"] = [InvoiceReceiptRules.PriceBlockedMessage] });
        }

        if (d.Stock.FirstOrDefault(s => s.Lots.Count > 1) is { } mixed)
        {
            return Error.Validation(InvoiceLotsCode, "Itens da nota com lotes diferentes estão na mesma linha do pedido. Receba esta linha pelo pedido, um lote por vez.",
                new Dictionary<string, string[]> { ["lines"] = [$"Linha {mixed.PoLine.Sequence} do pedido com os lotes {string.Join(", ", mixed.Lots)}."] });
        }

        var chosen = (input.Lines ?? []).GroupBy(l => l.PoLineId).ToDictionary(g => g.Key, g => g.First());
        var lines = d.Stock.Select(s =>
        {
            var pick = chosen.GetValueOrDefault(s.PoLine.Id);
            return new GoodsReceiptLineInput(s.PoLine.Id, s.Quantity, false, Clean(pick?.LotNumber) ?? s.Lot, pick?.LotExpiry, pick?.LocationId);
        }).ToList();
        var costs = new InvoiceCosts(d.Invoice.Freight, d.StockIpi, d.Invoice.Insurance, d.Invoice.OtherExpenses,
            d.Stock.ToDictionary(s => s.PoLine.Id, s => (s.Icms, s.Pis, s.Cofins)), d.Invoice.Credit.Taxes);
        var prepared = await PrepareAsync(d.Order.Id, new GoodsReceiptInput(input.ReceivedOn, null, null, input.Notes, lines, input.OrderVersion), tracked, ct, costs);
        return prepared.IsFailure ? prepared.Error! : (d, prepared.Value);
    }

    /// <summary>
    /// Parcelas da nota (CA 6 a 9): as duplicatas (todas com vencimento) ou, sem elas, a condição do pedido a partir da emissão da nota (CA 7);
    /// o recebimento e a parte sem item de estoque proporcionais a cada parcela.
    /// </summary>
    private async Task<Result<(InvoiceInstallmentPlan Plan, bool FromDuplicates, int Count)>> InvoicePlanAsync(InvoiceDraft d, decimal receiptAmount, CancellationToken ct)
    {
        var noStock = d.NoStock.Where(i => i.ExpenseAccountId is not null).GroupBy(i => i.ExpenseAccountId!.Value)
            .Select(g => (AccountId: g.Key, Amount: g.Sum(i => InvoiceReceiptRules.NoStockAmount(i.Amount, i.Discount, i.Ipi)))).Where(g => g.Amount > 0m).ToList();
        var duplicates = d.Invoice.Duplicates;
        var fromDuplicates = duplicates.Count > 0 && duplicates.All(x => x.DueDate is not null && x.Amount > 0m);
        IReadOnlyList<(DateOnly DueDate, decimal Amount)> basis;
        if (fromDuplicates)
        {
            basis = duplicates.Select(x => (x.DueDate!.Value, x.Amount)).ToList();
        }
        else
        {
            var dues = await terms.SplitAsync(d.Order.PaymentTermId, receiptAmount + noStock.Sum(n => n.Amount), d.Invoice.IssuedOn, ct);
            if (dues is null || dues.Count == 0)
            {
                return PurchaseOrderService.Invalid("paymentTermId", "A nota não trouxe duplicatas e a condição de pagamento do pedido não existe mais. Fale com quem compra.");
            }

            basis = dues.Select(x => (x.DueDate, x.Amount)).ToList();
        }

        return (InvoiceReceiptRules.Plan(receiptAmount, noStock, basis, fromDuplicates), fromDuplicates, basis.Count);
    }

    /// <summary>
    /// Títulos da nota (CA 6, 8 e 9): os do recebimento (origem <c>GoodsReceipt</c>, conta e natureza do pedido) e os da parte sem item de
    /// estoque (origem <c>IncomingInvoice</c>, id da nota, na conta escolhida), pelo mesmo <see cref="IPayables.CreateOpenInstallmentsAsync"/>.
    /// </summary>
    private async Task<Result> InvoicePayablesAsync(PurchaseOrder order, GoodsReceipt receipt, InvoiceCommit c, CancellationToken ct)
    {
        Guid accountId;
        if (order.AccountId is { } stored)
        {
            accountId = stored;
        }
        else
        {
            var account = await orders.AccountIdAsync(ct);
            if (account.IsFailure)
            {
                return account.Error!;
            }

            accountId = account.Value;
        }

        var defaults = await parameters.PayableDefaultsAsync(ct);
        var number = c.Invoice.Number;
        var plan = new OpenPayables(order.PartyId, order.ProjectId, accountId, defaults.CostType, c.Plan.Receipt.Select(i => new OpenInstallment(
            (short)i.Number, i.Amount, i.DueDate, InvoiceReceiptRules.InstallmentDescription(number, receipt.Number, i.Number, c.Count, c.Plan.Mismatch))).ToList());
        var created = await payables.CreateOpenInstallmentsAsync(DocumentOrigin.GoodsReceipt, receipt.Id, plan, ct);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        foreach (var (expense, installments) in c.Plan.NoStock.Where(n => n.Installments.Count > 0))
        {
            var name = c.AccountNames.GetValueOrDefault(expense) ?? "despesa";
            var part = new OpenPayables(order.PartyId, order.ProjectId, expense, CostType.Other, installments.Select(i => new OpenInstallment(
                (short)i.Number, i.Amount, i.DueDate, InvoiceReceiptRules.NoStockDescription(number, name, Position(i.Number, c.Count), c.Count, c.Plan.Mismatch))).ToList());
            var made = await payables.CreateOpenInstallmentsAsync(DocumentOrigin.IncomingInvoice, c.Invoice.Id, part, ct);
            if (made.IsFailure)
            {
                return made.Error!;
            }
        }

        return Result.Success();
    }

    private async Task<IReadOnlyList<InvoiceNoStockLine>> NoStockLinesAsync(IncomingInvoiceForReceipt invoice, CancellationToken ct)
    {
        var rows = invoice.Items.Where(i => i.NoStockItem).ToList();
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Where(r => r.ExpenseAccountId is not null).Select(r => r.ExpenseAccountId!.Value).Distinct().ToList();
        var names = ids.Count == 0 ? new Dictionary<Guid, string>() : (await accounts.FindAsync(ids, ct)).ToDictionary(a => a.Id, a => a.Code + " " + a.Name);
        return rows.Select(r => new InvoiceNoStockLine(r.Sequence, r.Description, r.ExpenseAccountId, r.ExpenseAccountId is { } a ? names.GetValueOrDefault(a) : null,
            InvoiceReceiptRules.NoStockAmount(r.Amount, r.Discount, r.Ipi))).ToList();
    }

    private async Task<IReadOnlyDictionary<Guid, string>> AccountNamesAsync(InvoiceInstallmentPlan plan, CancellationToken ct)
    {
        var ids = plan.NoStock.Select(n => n.AccountId).Distinct().ToList();
        return ids.Count == 0 ? new Dictionary<Guid, string>() : (await accounts.FindAsync(ids, ct)).ToDictionary(a => a.Id, a => a.Code + " " + a.Name);
    }

    /// <summary>Posição da parcela dentro da conta (as partes sem estoque numeram as parcelas depois das da conta anterior).</summary>
    private static int Position(int number, int count) => count <= 0 ? number : ((number - 1) % count) + 1;

    private static Error InvoiceNotFound() => Error.NotFound("incoming_invoice_not_found", "Nota de entrada não encontrada.");
}
