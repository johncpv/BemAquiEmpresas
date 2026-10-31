using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application;

/// <summary>
/// Recebimento manual contra o pedido (CMP-04): registrar e estornar. Na <b>mesma transação</b> do recebimento: a entrada no estoque pelo
/// motor (<see cref="IStockMovements"/>, origem <c>GoodsReceipt</c>, com lote, local e o custo de entrada rateado, ADR 0023 §1), o recebido
/// das linhas e a situação do pedido, os títulos em aberto do próprio recebimento e o replanejamento dos previstos do pedido para o saldo
/// (<see cref="IPayables"/>, ADR 0024 §3). Falha em qualquer parte desfaz tudo. O <see cref="GoodsReceived"/> vai ao outbox só como aviso.
/// </summary>
public sealed partial class GoodsReceiptService(
    IPurchasingDb db, PurchaseOrderService orders, IItems items, ILocations locations, IStockMovements stock, IStockBalances balances, IItemCosts costs,
    IStockReservations reservations, BemAqui.Production.Contracts.IProductionOrders productionOrders, IItemSupplierCodes supplierCodes, BemAqui.Parties.Contracts.IParties parties,
    BemAqui.Commercial.Contracts.IProjects projects, IPaymentTerms terms, IPayables payables, INumberSequence numbers, PurchasingParametersService parameters,
    IPermissionChecker permissions, ICurrentUser currentUser, IUserDirectory users, INotificationService notifications, IEventPublisher events, TimeProvider clock,
    BemAqui.Fiscal.Contracts.IIncomingInvoices incomingInvoices, IChartOfAccounts accounts)
{
    public const string NotificationType = "purchasing.goods_receipt";
    public const string ReversedCode = "goods_receipt_reversed";
    public const string ReapprovalCode = "purchase_order_in_reapproval";
    public const string ClosedCode = "purchase_order_closed";

    internal static string Link(Guid id) => "/app/compras/recebimentos/" + id;

    private DateOnly Today => BusinessTime.Today(clock);

    private sealed record PreparedLine(
        PurchaseOrderLine PoLine, ItemSummary Item, decimal Quantity, string? SupplierUnit, decimal? SupplierQuantity, string? LotNumber, DateOnly? LotExpiry, Guid LocationId,
        decimal LineAmount, decimal FreightShare, decimal IpiShare, decimal UnitCost, decimal InsuranceShare = 0m, decimal OtherShare = 0m, decimal TaxCreditShare = 0m);

    private sealed record Prepared(
        PurchaseOrder Order, List<PurchaseOrderLine> Lines, DateOnly ReceivedOn, decimal Subtotal, decimal Freight, decimal IpiAmount, decimal Amount, decimal FreightMax,
        decimal IpiMax, decimal Ceiling, decimal ReceivedBefore, bool Completes, List<PreparedLine> Items, bool SoleOwner, decimal Insurance = 0m, decimal OtherExpenses = 0m,
        decimal TaxCredit = 0m);

    /// <summary>
    /// Valores da nota para o recebimento pela nota (FIS-04b CA 1, 4 e 5): frete, IPI (da parte de estoque), seguro e outras despesas da
    /// nota, os tributos dos itens por linha do pedido e os tributos recuperáveis do parâmetro de crédito.
    /// </summary>
    private sealed record InvoiceCosts(
        decimal Freight, decimal Ipi, decimal Insurance, decimal OtherExpenses, IReadOnlyDictionary<Guid, (decimal Icms, decimal Pis, decimal Cofins)> Taxes,
        IReadOnlyCollection<string> Recoverable);

    /// <summary>
    /// "Registrar recebimento" preenchido (CA 1, R-PRP-1): as linhas com o saldo a receber, o local padrão, a data de hoje, o frete e o IPI
    /// proporcionais ao saldo inteiro e, quando o usuário não pode registrar (pediu ou aprovou, pedido em reaprovação), o motivo.
    /// </summary>
    public async Task<Result<GoodsReceiptDefaults>> DefaultsAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await db.PurchaseOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null)
        {
            return PurchaseOrderService.NotFound();
        }

        var lines = await db.PurchaseOrderLines.AsNoTracking().Where(l => l.OrderId == orderId).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        var info = (await items.FindAsync(lines.Select(l => l.ItemId).Distinct().ToList(), cancellationToken)).ToDictionary(i => i.Id);
        var codes = await supplierCodes.ForItemsAsync(order.PartyId, lines.Select(l => l.ItemId).Distinct().ToList(), cancellationToken);
        var tolerance = await parameters.ReceiptToleranceAsync(cancellationToken);
        var active = await locations.ActiveAsync(cancellationToken);
        var fallback = await locations.DefaultAsync(cancellationToken);
        var used = await UsedAsync(orderId, cancellationToken);
        var party = await parties.FindAsync(order.PartyId, cancellationToken);
        var project = order.ProjectId is { } pid ? await projects.FindAsync(pid, cancellationToken) : null;
        var blocker = await orders.ReceiveBlockerAsync(order, lines, cancellationToken);
        var segregation = await orders.ReceiveSegregationAsync(order, cancellationToken);
        var rows = lines.Select(l =>
        {
            var item = info.GetValueOrDefault(l.ItemId);
            var code = codes.GetValueOrDefault(l.ItemId);
            var receivedTotal = l.QuantityReceived + l.QuantityOverReceived;
            return new GoodsReceiptDefaultLine(
                l.Id, l.Sequence, l.ItemId, item?.Code ?? string.Empty, item?.Name ?? "Item removido", item?.UnitCode ?? string.Empty, item?.TracksLot ?? false, l.Quantity, receivedTotal,
                Math.Max(0m, l.Quantity - l.QuantityReceived), GoodsReceiptRules.MaxQuantity(l.Quantity, receivedTotal, tolerance), l.UnitPrice,
                code is { SupplierUnit: { Length: > 0 } unit } ? unit : null, code is { SupplierUnit: { Length: > 0 } } ? code.ConversionFactor : null);
        }).ToList();
        return new GoodsReceiptDefaults(
            order.Id, order.Number, order.Status, order.PartyId, party is null ? "Fornecedor removido" : party.TradeName ?? party.LegalName, order.ProjectId, project?.Number, Today,
            fallback is { IsActive: true } ? fallback.Id : active.Count > 0 ? active[0].Id : null, active.Select(l => new ReceiptLocationOption(l.Id, l.Code, l.Name, l.IsDefault)).ToList(), tolerance,
            order.Subtotal, order.Freight, order.IpiAmount, used.Freight, used.Ipi, order.ApprovedTotal ?? order.Total, used.Amount, rows, segregation is { Involved: true, SoleOwner: true },
            blocker, order.Version);
    }

    /// <summary>
    /// Impacto do recebimento (CA 5) para o diálogo de confirmação: as mesmas conferências do registro (CA 1 a 4), sem gravar nada; por
    /// item o custo de entrada e o custo médio antes → depois (com <c>inventory.view</c>), os títulos que nascem e o previsto antes → depois.
    /// </summary>
    public async Task<Result<GoodsReceiptPreview>> PreviewAsync(Guid orderId, GoodsReceiptInput input, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(orderId, input, tracked: false, cancellationToken);
        if (prepared.IsFailure)
        {
            return prepared.Error!;
        }

        var p = prepared.Value;
        var dues = await terms.SplitAsync(p.Order.PaymentTermId, p.Amount, p.ReceivedOn, cancellationToken) ?? [];
        return await BuildPreviewAsync(p, dues.Select(d => new GoodsReceiptInstallmentPreview(d.Number, d.DueDate, d.Amount)).ToList(), cancellationToken);
    }

    /// <summary>O impacto comum ao recebimento manual e ao pela nota: custo médio antes → depois, as parcelas dadas e o previsto do pedido.</summary>
    private async Task<GoodsReceiptPreview> BuildPreviewAsync(Prepared p, IReadOnlyList<GoodsReceiptInstallmentPreview> installments, CancellationToken cancellationToken)
    {
        var seeCost = await permissions.HasPermissionAsync("inventory", "view", cancellationToken);
        var itemIds = p.Items.Select(i => i.Item.Id).Distinct().ToList();
        var averages = seeCost ? await costs.AverageCostsAsync(itemIds, cancellationToken) : new Dictionary<Guid, decimal>();
        var onHand = seeCost
            ? (await balances.GetAsync(itemIds, null, cancellationToken)).GroupBy(b => b.ItemId).ToDictionary(g => g.Key, g => g.Sum(b => b.OnHand))
            : new Dictionary<Guid, decimal>();
        var lines = p.Items.Select(i =>
        {
            decimal? before = seeCost ? averages.GetValueOrDefault(i.Item.Id) : null;
            decimal? after = null;
            if (before is { } avg)
            {
                var stockBefore = Math.Max(0m, onHand.GetValueOrDefault(i.Item.Id));
                after = stockBefore + i.Quantity <= 0m ? avg : Math.Round((stockBefore * avg + i.Quantity * i.UnitCost) / (stockBefore + i.Quantity), 6, MidpointRounding.AwayFromZero);
            }

            return new GoodsReceiptPreviewLine(i.PoLine.Id, i.Item.Code, i.Item.Name, i.Item.UnitCode, i.Quantity, i.LineAmount, i.FreightShare, i.IpiShare, i.UnitCost, before, after,
                i.InsuranceShare, i.OtherShare, i.TaxCreditShare);
        }).ToList();
        var plannedBefore = GoodsReceiptRules.PlannedBalance(p.Order.Total, p.ReceivedBefore);
        var plannedAfter = p.Completes ? 0m : GoodsReceiptRules.PlannedBalance(p.Order.Total, p.ReceivedBefore + p.Amount);
        return new GoodsReceiptPreview(p.ReceivedOn, p.Subtotal, p.Freight, p.IpiAmount, p.Amount, p.FreightMax, p.IpiMax, p.Ceiling, p.ReceivedBefore + p.Amount, lines,
            installments, plannedBefore, plannedAfter, p.Completes, p.SoleOwner, p.Insurance, p.OtherExpenses, p.TaxCredit);
    }

    /// <summary>
    /// "Registrar recebimento" (CA 5 a 9): na mesma transação nasce o recebimento <c>Completed</c> (<c>REC-AAAA-0001</c>), cada linha entra no
    /// estoque pelo motor com lote, local e o custo de entrada (o motor completa as faltas das reservas, CA 7), o recebido das linhas e a
    /// situação do pedido mudam, nascem os títulos em aberto do recebimento por parcela (CA 8) e os previstos do pedido são replanejados
    /// para o saldo (CA 9). Dois recebimentos do mesmo saldo ao mesmo tempo: a versão do pedido segura (409 <c>concurrency_conflict</c>).
    /// </summary>
    public async Task<Result<GoodsReceiptDetail>> RegisterAsync(Guid orderId, GoodsReceiptInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("purchasing", "receive", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Quem registra o recebimento é quem tem a permissão Registrar recebimento em Compras.");
        }

        var prepared = await PrepareAsync(orderId, input, tracked: true, cancellationToken);
        if (prepared.IsFailure)
        {
            return prepared.Error!;
        }

        if (prepared.Value.Order.Version != input.Version)
        {
            return Stale();
        }

        return await CommitAsync(prepared.Value, input.Version, Clean(input.Notes), null, cancellationToken);
    }

    /// <summary>A nota do recebimento pela nota (FIS-04b) na transação: o que vira título e a regra de crédito usada.</summary>
    private sealed record InvoiceCommit(BemAqui.Fiscal.Contracts.IncomingInvoiceForReceipt Invoice, InvoiceInstallmentPlan Plan, IReadOnlyDictionary<Guid, string> AccountNames, int Count);

    /// <summary>
    /// A transação do recebimento (CMP-04 CA 5 a 9; FIS-04b CA 6): recebimento, recebido do pedido, entradas no estoque, títulos (as parcelas
    /// do pedido ou as duplicatas da nota), replanejamento do previsto e, pela nota, a transição dela para <c>Received</c>. Qualquer falha
    /// devolve o erro sem commit: tudo é desfeito.
    /// </summary>
    private async Task<Result<GoodsReceiptDetail>> CommitAsync(Prepared p, uint version, string? notes, InvoiceCommit? invoice, CancellationToken cancellationToken)
    {
        var order = p.Order;
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lots = new Dictionary<Guid, Guid?>();
        foreach (var line in p.Items)
        {
            lots[line.PoLine.Id] = line.LotNumber is { } lot ? await stock.EnsureLotAsync(line.Item.Id, lot, order.PartyId, line.LotExpiry, cancellationToken) : null;
        }

        var statusBefore = order.Status;
        var receipt = new GoodsReceipt
        {
            Number = await numbers.NextAsync(GoodsReceiptRules.DocType, cancellationToken), PurchaseOrderId = order.Id, PartyId = order.PartyId, ProjectId = order.ProjectId,
            ReceivedOn = p.ReceivedOn, Subtotal = p.Subtotal, Freight = p.Freight, IpiAmount = p.IpiAmount, Amount = p.Amount, OrderStatusBefore = statusBefore,
            Notes = notes, ReceivedBy = currentUser.UserId, SoleOwner = p.SoleOwner, CreatedAt = now, Insurance = p.Insurance, OtherExpenses = p.OtherExpenses, TaxCredit = p.TaxCredit,
            Origin = invoice is null ? GoodsReceiptOrigin.Manual : GoodsReceiptOrigin.Invoice, IncomingInvoiceId = invoice?.Invoice.Id,
        };
        db.GoodsReceipts.Add(receipt);
        var receiptLines = new List<(GoodsReceiptLine Line, PreparedLine Source)>();
        var sequence = 0;
        foreach (var line in p.Items)
        {
            var row = new GoodsReceiptLine
            {
                ReceiptId = receipt.Id, Sequence = ++sequence, PoLineId = line.PoLine.Id, ItemId = line.Item.Id, Quantity = line.Quantity, SupplierUnit = line.SupplierUnit,
                SupplierQuantity = line.SupplierQuantity, LotNumber = line.LotNumber, LotId = lots[line.PoLine.Id], LotExpiry = line.LotExpiry, LocationId = line.LocationId,
                UnitPrice = line.PoLine.UnitPrice, LineAmount = line.LineAmount, FreightShare = line.FreightShare, IpiShare = line.IpiShare, UnitCost = line.UnitCost,
                InsuranceShare = line.InsuranceShare, OtherShare = line.OtherShare, TaxCreditShare = line.TaxCreditShare,
            };
            db.GoodsReceiptLines.Add(row);
            receiptLines.Add((row, line));
            var (received, over) = GoodsReceiptRules.Split(line.PoLine.Quantity, line.PoLine.QuantityReceived + line.PoLine.QuantityOverReceived + line.Quantity);
            line.PoLine.QuantityReceived = received;
            line.PoLine.QuantityOverReceived = over;
            line.PoLine.AmountReceived += line.LineAmount;
        }

        // Grava o pedido antes do motor: dois recebimentos do mesmo saldo param no xmin e só um segue (CA 17).
        db.Entry(order).Property(o => o.Version).OriginalValue = version;
        order.Status = GoodsReceiptRules.StatusAfter(p.Lines.Select(l => (l.Quantity, l.QuantityReceived)).ToList(), statusBefore);
        order.UpdatedAt = now;
        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        // FIS-04b CA 13: a nota passa a Received logo depois do pedido, antes do estoque e dos títulos (a versão da nota e o índice único
        // do recebimento da nota seguram o segundo "Receber pela nota" ao mesmo tempo).
        if (invoice is not null)
        {
            var marked = await incomingInvoices.MarkReceivedAsync(invoice.Invoice.Id, invoice.Invoice.Version, receipt.Id, invoice.Invoice.Credit.Label, p.TaxCredit, cancellationToken);
            if (marked.IsFailure)
            {
                return marked.Error!;
            }
        }

        var at = p.ReceivedOn == BusinessTime.Today(clock) ? (DateTimeOffset?)null : BusinessTime.StartOfDayUtc(p.ReceivedOn).AddHours(12);
        var postings = receiptLines.Select(r => new StockPosting(
            StockMovementType.Receipt, r.Line.ItemId, r.Line.LocationId, r.Line.LotId, r.Line.Quantity, r.Line.UnitCost, StockSources.GoodsReceipt, receipt.Id, Leg: r.Line.Sequence,
            ProjectId: order.ProjectId, At: at, SourceNumber: receipt.Number)).ToList();
        var posted = await stock.PostManyAsync(postings, cancellationToken);
        if (posted.IsFailure)
        {
            return posted.Error!;
        }

        for (var i = 0; i < receiptLines.Count; i++)
        {
            receiptLines[i].Line.MovementId = posted.Value[i].MovementId;
            receiptLines[i].Line.MovementNumber = posted.Value[i].Number;
        }

        var titles = invoice is null ? await OpenPayablesAsync(order, receipt, cancellationToken) : await InvoicePayablesAsync(order, receipt, invoice, cancellationToken);
        if (titles.IsFailure)
        {
            return titles.Error!;
        }

        var replanned = await orders.ReplanAsync(order, $"pedido {order.Number} recebido ({receipt.Number})", cancellationToken);
        if (replanned.IsFailure)
        {
            return replanned.Error!;
        }

        events.Publish(new GoodsReceived(receipt.Id, order.Id, order.PartyId, order.ProjectId, receipt.ReceivedOn, receipt.Amount,
            receiptLines.Select(r => new GoodsReceivedLine(r.Line.PoLineId, r.Line.Quantity, r.Line.LineAmount)).ToList(), invoice?.Invoice.Id));
        if (p.SoleOwner)
        {
            foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
            {
                notifications.Notify(owner, NotificationType, $"Recebimento {receipt.Number} do pedido {order.Number} registrado por quem pediu ou aprovou ({GoodsReceiptRules.SoleOwnerLabel}).",
                    Link(receipt.Id));
            }
        }

        if (await SaveAsync(cancellationToken) is { } late)
        {
            return late;
        }

        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(receipt.Id, cancellationToken);
    }

    /// <summary>
    /// Títulos em aberto do recebimento (CA 8): um por parcela da condição do pedido, vencimento = data do recebimento + dias da parcela,
    /// valor = itens + frete e IPI rateados (a última parcela absorve a diferença), com o fornecedor, o projeto, a conta e a natureza do pedido.
    /// </summary>
    private async Task<Result> OpenPayablesAsync(PurchaseOrder order, GoodsReceipt receipt, CancellationToken ct)
    {
        var dues = await terms.SplitAsync(order.PaymentTermId, receipt.Amount, receipt.ReceivedOn, ct);
        if (dues is null || dues.Count == 0)
        {
            return PurchaseOrderService.Invalid("paymentTermId", "A condição de pagamento do pedido não existe mais. Fale com quem compra.");
        }

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
        var plan = new OpenPayables(order.PartyId, order.ProjectId, accountId, defaults.CostType,
            dues.Select(d => new OpenInstallment((short)d.Number, d.Amount, d.DueDate, GoodsReceiptRules.InstallmentDescription(receipt.Number, order.Number, d.Number, dues.Count))).ToList());
        var created = await payables.CreateOpenInstallmentsAsync(DocumentOrigin.GoodsReceipt, receipt.Id, plan, ct);
        return created.IsFailure ? created.Error! : Result.Success();
    }

    /// <summary>
    /// "Estornar recebimento" (CA 12, <c>purchasing.approve</c>, motivo obrigatório): na mesma transação, os títulos do recebimento são
    /// cancelados <b>antes</b> do estoque (409 se algum está em OP ou pago, com "Ver OP"); nascem os movimentos inversos pelo motor (saldo
    /// insuficiente: 409 <c>insufficient_stock</c> com o item); o recebido desce, o pedido volta a parcialmente recebido (ou à situação de
    /// antes) e o saldo volta ao previsto do pedido. <c>GoodsReceiptReversed</c> vai ao outbox só como aviso.
    /// </summary>
    public async Task<Result<GoodsReceiptDetail>> ReverseAsync(Guid id, GoodsReceiptReverseInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("purchasing", "approve", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Quem estorna o recebimento é quem tem a permissão Aprovar em Compras.");
        }

        var receipt = await db.GoodsReceipts.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (receipt is null)
        {
            return NotFound();
        }

        if (receipt.Status != GoodsReceiptStatus.Completed)
        {
            return Error.Conflict(ReversedCode, $"O recebimento {receipt.Number} já foi estornado.");
        }

        var reason = Clean(input.Reason);
        if (reason is null || reason.Length > GoodsReceiptRules.MaxReason)
        {
            return PurchaseOrderService.Invalid("reason", $"Informe o motivo do estorno, com até {GoodsReceiptRules.MaxReason} caracteres.");
        }

        if (receipt.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "O recebimento mudou desde que você abriu. Recarregue.");
        }

        var order = await db.PurchaseOrders.FirstAsync(o => o.Id == receipt.PurchaseOrderId, cancellationToken);
        if (order.ClosedBalanceAmount is not null)
        {
            return Error.Conflict(ClosedCode, $"O saldo do pedido {order.Number} foi encerrado: o recebimento não é mais estornado.");
        }

        if (order.Status == PurchaseOrderStatus.PendingApproval)
        {
            return Error.Conflict(ReapprovalCode, $"O pedido {order.Number} está em reaprovação: estorne depois da decisão.");
        }

        var lines = await db.GoodsReceiptLines.Where(l => l.ReceiptId == id).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        var poLines = await db.PurchaseOrderLines.Where(l => l.OrderId == order.Id).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var cancelled = await payables.CancelAllFromOriginAsync(DocumentOrigin.GoodsReceipt, receipt.Id, $"recebimento {receipt.Number} estornado ({reason})", cancellationToken);
        if (cancelled.IsFailure)
        {
            return cancelled.Error!;
        }

        // FIS-04b CA 11: pela nota, os títulos da parte "Sem item de estoque" (origem IncomingInvoice) também saem, antes do estoque.
        if (receipt.IncomingInvoiceId is { } invoiceId)
        {
            var noStock = await payables.CancelAllFromOriginAsync(DocumentOrigin.IncomingInvoice, invoiceId, $"recebimento {receipt.Number} estornado ({reason})", cancellationToken);
            if (noStock.IsFailure)
            {
                return noStock.Error!;
            }
        }

        db.Entry(receipt).Property(r => r.Version).OriginalValue = input.Version;
        receipt.Status = GoodsReceiptStatus.Reversed;
        receipt.ReversedAt = now;
        receipt.ReversedBy = currentUser.UserId;
        receipt.ReversalReason = reason;
        foreach (var line in lines)
        {
            var poLine = poLines.First(l => l.Id == line.PoLineId);
            var (received, over) = GoodsReceiptRules.Split(poLine.Quantity, Math.Max(0m, poLine.QuantityReceived + poLine.QuantityOverReceived - line.Quantity));
            poLine.QuantityReceived = received;
            poLine.QuantityOverReceived = over;
            poLine.AmountReceived = Math.Max(0m, poLine.AmountReceived - line.LineAmount);
        }

        order.Status = GoodsReceiptRules.StatusAfter(poLines.Select(l => (l.Quantity, l.QuantityReceived)).ToList(), receipt.OrderStatusBefore);
        order.UpdatedAt = now;
        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        foreach (var line in lines.Where(l => l.MovementId is not null))
        {
            var reversed = await stock.ReverseAsync(new StockReversal(line.MovementId!.Value, StockSources.GoodsReceipt, receipt.Id, $"Estorno do recebimento {receipt.Number}: {reason}"),
                cancellationToken);
            if (reversed.IsFailure)
            {
                return reversed.Error!;
            }

            line.ReversalMovementId = reversed.Value.MovementId;
        }

        var replanned = await orders.ReplanAsync(order, $"recebimento {receipt.Number} estornado", cancellationToken);
        if (replanned.IsFailure)
        {
            return replanned.Error!;
        }

        if (receipt.IncomingInvoiceId is { } back)
        {
            var returned = await incomingInvoices.ReturnToCheckedAsync(back, receipt.Id, cancellationToken);
            if (returned.IsFailure)
            {
                return returned.Error!;
            }
        }

        events.Publish(new GoodsReceiptReversed(receipt.Id, order.Id, receipt.Amount));
        if (receipt.ReceivedBy is { } who && who != currentUser.UserId)
        {
            notifications.Notify(who, NotificationType, $"Recebimento {receipt.Number} estornado: {Cut(reason, 120)}", Link(receipt.Id));
        }

        if (await SaveAsync(cancellationToken) is { } late)
        {
            return late;
        }

        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>
    /// Conferências do recebimento (CA 1 a 4 e 16), as mesmas na prévia e no registro: pedido aberto para receber, segregação, data, linhas
    /// do próprio pedido (outra: 422), quantidade até o saldo + a tolerância, lote do item que controla lote, local ativo, frete e IPI só para
    /// baixo e o teto do aprovado.
    /// </summary>
    private async Task<Result<Prepared>> PrepareAsync(Guid orderId, GoodsReceiptInput input, bool tracked, CancellationToken ct, InvoiceCosts? costs = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        var order = tracked
            ? await db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct)
            : await db.PurchaseOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null)
        {
            return PurchaseOrderService.NotFound();
        }

        if (order.Status == PurchaseOrderStatus.PendingApproval && order.ApprovedTotal is not null)
        {
            return Error.Conflict(ReapprovalCode, $"{PurchaseOrderService.ReapprovalBlocker}: o recebimento volta depois da nova aprovação.");
        }

        if (!PurchaseOrderRules.OpenForReceipt.Contains(order.Status))
        {
            return Error.Conflict(PurchaseOrderService.NotOpenForReceiptCode, $"O pedido {order.Number} não está aprovado com saldo a receber.");
        }

        var segregation = await orders.ReceiveSegregationAsync(order, ct);
        if (segregation is { Involved: true, SoleOwner: false })
        {
            return Error.Forbidden(PurchaseOrderService.SegregationCode, segregation.Message ?? "Quem pediu ou aprovou este pedido não registra o recebimento dele.");
        }

        var today = Today;
        var receivedOn = input.ReceivedOn ?? today;
        if (receivedOn > today)
        {
            return PurchaseOrderService.Invalid("receivedOn", "A data de recebimento não pode ser depois de hoje.");
        }

        if (receivedOn < DateOnly.FromDateTime(BusinessTime.ToBusiness(order.CreatedAt).DateTime))
        {
            return PurchaseOrderService.Invalid("receivedOn", "A data de recebimento não pode ser antes do pedido.");
        }

        if (Clean(input.Notes) is { Length: > GoodsReceiptRules.MaxNotes })
        {
            return PurchaseOrderService.Invalid("notes", $"Escreva a observação com até {GoodsReceiptRules.MaxNotes} caracteres.");
        }

        var inputs = input.Lines ?? [];
        if (inputs.Select(l => l.PoLineId).Distinct().Count() != inputs.Count)
        {
            return PurchaseOrderService.Invalid("lines", "Cada linha do pedido aparece uma vez só no recebimento.");
        }

        var lines = tracked
            ? await db.PurchaseOrderLines.Where(l => l.OrderId == orderId).OrderBy(l => l.Sequence).ToListAsync(ct)
            : await db.PurchaseOrderLines.AsNoTracking().Where(l => l.OrderId == orderId).OrderBy(l => l.Sequence).ToListAsync(ct);
        if (inputs.FirstOrDefault(i => lines.All(l => l.Id != i.PoLineId)) is not null)
        {
            return PurchaseOrderService.Invalid("lines", "Há uma linha que não é deste pedido. Abra o recebimento pelo pedido.");
        }

        var received = inputs.Where(i => i.Quantity != 0m).ToList();
        if (received.Count == 0)
        {
            return PurchaseOrderService.Invalid("lines", "Informe a quantidade recebida de pelo menos um item.");
        }

        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var info = (await items.FindAsync(itemIds, ct)).ToDictionary(i => i.Id);
        var codes = await supplierCodes.ForItemsAsync(order.PartyId, itemIds, ct);
        var active = (await locations.ActiveAsync(ct)).Select(l => l.Id).ToHashSet();
        var defaultLocation = await locations.DefaultAsync(ct);
        var tolerance = await parameters.ReceiptToleranceAsync(ct);
        var drafts = new List<(PurchaseOrderLine PoLine, ItemSummary Item, decimal Quantity, string? SupplierUnit, decimal? SupplierQuantity, string? Lot, DateOnly? Expiry, Guid Location, decimal Amount)>();
        foreach (var entry in received)
        {
            var index = inputs.ToList().IndexOf(entry);
            var field = $"lines[{index}]";
            var poLine = lines.First(l => l.Id == entry.PoLineId);
            if (!info.TryGetValue(poLine.ItemId, out var item))
            {
                return PurchaseOrderService.Invalid(field + ".poLineId", "O item desta linha não existe mais.");
            }

            if (entry.Quantity < 0m || decimal.Round(entry.Quantity, 4) != entry.Quantity || entry.Quantity > PurchaseOrderRules.MaxQuantity)
            {
                return PurchaseOrderService.Invalid(field + ".quantity", "Informe a quantidade recebida, maior que zero, com até 4 casas.");
            }

            var quantity = entry.Quantity;
            string? supplierUnit = null;
            decimal? supplierQuantity = null;
            if (entry.InSupplierUnit)
            {
                if (codes.GetValueOrDefault(item.Id) is not { SupplierUnit: { Length: > 0 } unit } code)
                {
                    return PurchaseOrderService.Invalid(field + ".quantity", "Este item não tem a unidade do fornecedor cadastrada. Informe na unidade de estoque.");
                }

                supplierUnit = unit;
                supplierQuantity = entry.Quantity;
                quantity = GoodsReceiptRules.FromSupplierUnit(entry.Quantity, code.ConversionFactor);
            }

            var receivedTotal = poLine.QuantityReceived + poLine.QuantityOverReceived;
            if (quantity <= 0m || quantity > GoodsReceiptRules.MaxQuantity(poLine.Quantity, receivedTotal, tolerance))
            {
                return PurchaseOrderService.Invalid(field + ".quantity",
                    $"Recebido acima do pedido: saldo {PurchaseFormat.Quantity(Math.Max(0m, poLine.Quantity - poLine.QuantityReceived))}.");
            }

            var lot = Clean(entry.LotNumber);
            if (item.TracksLot && lot is null)
            {
                return PurchaseOrderService.Invalid(field + ".lotNumber", "Este item controla lote: informe o lote.");
            }

            if (lot is { Length: > GoodsReceiptRules.MaxLotNumber })
            {
                return PurchaseOrderService.Invalid(field + ".lotNumber", $"O lote tem até {GoodsReceiptRules.MaxLotNumber} caracteres.");
            }

            if (entry.LotExpiry is not null && lot is null)
            {
                return PurchaseOrderService.Invalid(field + ".lotExpiry", "Informe o lote para guardar a validade.");
            }

            var location = entry.LocationId ?? defaultLocation?.Id;
            if (location is not { } loc || !active.Contains(loc))
            {
                return PurchaseOrderService.Invalid(field + ".locationId", "Escolha um local de estoque ativo.");
            }

            drafts.Add((poLine, item, quantity, supplierUnit, supplierQuantity, lot, entry.LotExpiry, loc, GoodsReceiptRules.LineAmount(quantity, poLine.UnitPrice)));
        }

        var subtotal = drafts.Sum(d => d.Amount);
        var completes = lines.All(l => l.QuantityReceived + drafts.Where(d => d.PoLine.Id == l.Id).Sum(d => d.Quantity) >= l.Quantity);
        var used = await UsedAsync(orderId, ct);
        var freightMax = GoodsReceiptRules.ProportionalShare(order.Freight, order.Subtotal, subtotal, used.Freight, completes);
        var ipiMax = GoodsReceiptRules.ProportionalShare(order.IpiAmount, order.Subtotal, subtotal, used.Ipi, completes);
        var freight = costs?.Freight ?? input.Freight ?? freightMax;
        var ipi = costs?.Ipi ?? input.IpiAmount ?? ipiMax;
        if (freight < 0m || freight > freightMax || decimal.Round(freight, 2) != freight)
        {
            return PurchaseOrderService.Invalid("freight", costs is null
                ? "Frete e IPI só podem baixar no recebimento."
                : $"O frete da nota ({PurchaseOrderRules.Money(freight)}) passa do proporcional do pedido ({PurchaseOrderRules.Money(freightMax)}). Peça a correção do pedido a quem aprova compras.");
        }

        if (ipi < 0m || ipi > ipiMax || decimal.Round(ipi, 2) != ipi)
        {
            return PurchaseOrderService.Invalid("ipiAmount", costs is null
                ? "Frete e IPI só podem baixar no recebimento."
                : $"O IPI da nota ({PurchaseOrderRules.Money(ipi)}) passa do proporcional do pedido ({PurchaseOrderRules.Money(ipiMax)}). Peça a correção do pedido a quem aprova compras.");
        }

        var amounts = drafts.Select(d => d.Amount).ToList();
        var freightShares = CostAllocator.Allocate(freight, amounts);
        var ipiShares = CostAllocator.Allocate(ipi, amounts);
        var insurance = costs?.Insurance ?? 0m;
        var other = costs?.OtherExpenses ?? 0m;
        var insuranceShares = CostAllocator.Allocate(insurance, amounts);
        var otherShares = CostAllocator.Allocate(other, amounts);
        var prepared = drafts.Select((d, i) =>
        {
            if (costs is null)
            {
                return new PreparedLine(d.PoLine, d.Item, d.Quantity, d.SupplierUnit, d.SupplierQuantity, d.Lot, d.Expiry, d.Location, d.Amount, freightShares[i], ipiShares[i],
                    GoodsReceiptRules.EntryUnitCost(d.Amount, freightShares[i], ipiShares[i], d.Quantity));
            }

            var taxes = costs.Taxes.GetValueOrDefault(d.PoLine.Id);
            var credit = InvoiceReceiptRules.TaxCredit(costs.Recoverable, taxes.Icms, taxes.Pis, taxes.Cofins, ipiShares[i]);
            return new PreparedLine(d.PoLine, d.Item, d.Quantity, d.SupplierUnit, d.SupplierQuantity, d.Lot, d.Expiry, d.Location, d.Amount, freightShares[i], ipiShares[i],
                InvoiceReceiptRules.EntryUnitCost(d.Amount, freightShares[i], ipiShares[i], insuranceShares[i], otherShares[i], credit, d.Quantity), insuranceShares[i], otherShares[i], credit);
        }).ToList();
        if (prepared.FirstOrDefault(l => l.UnitCost <= 0m) is { } free)
        {
            return PurchaseOrderService.Invalid("lines", $"O custo de entrada de {free.Item.Name} ficou zero: confira a quantidade.");
        }

        var amount = Math.Round(GoodsReceiptRules.Amount(amounts, freight, ipi) + insurance + other, 2, MidpointRounding.AwayFromZero);
        var ceiling = GoodsReceiptRules.Ceiling(order.ApprovedTotal ?? order.Total, tolerance);
        if (used.Amount + amount > ceiling)
        {
            return PurchaseOrderService.Invalid("lines", "Acima do valor aprovado do pedido: peça a correção do preço ou a reaprovação.");
        }

        return new Prepared(order, lines, receivedOn, subtotal, freight, ipi, amount, freightMax, ipiMax, ceiling, used.Amount, completes, prepared,
            segregation is { Involved: true, SoleOwner: true }, insurance, other, prepared.Sum(l => l.TaxCreditShare));
    }

    /// <summary>Frete, IPI e valor dos recebimentos registrados do pedido (os estornados ficam fora).</summary>
    private async Task<(decimal Freight, decimal Ipi, decimal Amount)> UsedAsync(Guid orderId, CancellationToken ct)
    {
        var rows = await db.GoodsReceipts.AsNoTracking().Where(r => r.PurchaseOrderId == orderId && r.Status == GoodsReceiptStatus.Completed)
            .Select(r => new { r.Freight, r.IpiAmount, r.Amount }).ToListAsync(ct);
        return (rows.Sum(r => r.Freight), rows.Sum(r => r.IpiAmount), rows.Sum(r => r.Amount));
    }

    private async Task<Error?> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Stale();
        }
        catch (DbUpdateException ex) when (BemAqui.BuildingBlocks.Persistence.DbErrors.IsUniqueViolation(ex, IncomingInvoiceIndex))
        {
            // FIS-04b CA 13: a mesma nota recebida ao mesmo tempo por outro usuário.
            return Error.Conflict(BemAqui.Fiscal.Contracts.IncomingInvoiceReceiptCodes.Received, "Esta nota acabou de ser recebida por outro usuário. Recarregue.");
        }
    }

    /// <summary>Índice único do recebimento registrado por nota (FIS-04b CA 13).</summary>
    public const string IncomingInvoiceIndex = "ux_goods_receipts_incoming_invoice";

    private static Error Stale() => Error.Conflict("concurrency_conflict", "O pedido mudou desde que você abriu (outro recebimento ou alteração). Recarregue.");

    internal static Error NotFound() => Error.NotFound("goods_receipt_not_found", "Recebimento não encontrado.");

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
