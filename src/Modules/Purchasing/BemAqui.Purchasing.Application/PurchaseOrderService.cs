using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Finance.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application;

/// <summary>
/// Pedidos de compra (CMP-03a): criar à mão, a partir das linhas de SC ou pela cotação (API da CMP-02), editar, enviar para aprovação
/// pela alçada do Finance (<see cref="IApprovalRouting"/>), aprovar com os títulos previstos na mesma transação (<see cref="IPayables"/>,
/// ADR 0024 §1 e §2), reprovar e cancelar. Fornecedor, projeto, condição e itens são validados pelos Contracts dos donos; o servidor
/// recalcula os totais; a linha é sempre lida pelo pedido da URL (IDOR).
/// </summary>
public sealed partial class PurchaseOrderService(
    IPurchasingDb db, IItems items, IItemUnits itemUnits, IUnitConversion conversion, IItemCosts costs, IParties parties, IProjects projects, IPaymentTerms terms,
    IChartOfAccounts chart, IPayables payables, IApprovalRouting routing, INumberSequence numbers, PurchasingParametersService parameters, IPermissionChecker permissions,
    ICurrentUser currentUser, IUserDirectory users, INotificationService notifications, IEventPublisher events, IEmailSender email, TenantLinks links, TimeProvider clock)
{
    public const string NotificationType = "purchasing.purchase_order";
    public const string EmailTemplate = "purchase-order-pending";

    /// <summary>Dono dos anexos do pedido (adendo à ADR 0019 §7): leitura <c>purchasing.view</c>, escrita <c>purchasing.edit</c>.</summary>
    public const string AttachmentOwner = "PurchaseOrder";

    public const string ReceivedCode = "purchase_order_received";
    public const string LockedCode = "purchase_order_locked";
    public const string DecidedCode = "purchase_order_decided";
    public const string NotDraftCode = "purchase_order_not_draft";

    private DateOnly Today => BusinessTime.Today(clock);

    internal static string Link(Guid id) => "/app/compras/pedidos/" + id;

    /// <summary>
    /// "Novo pedido" (CA 1) e a criação pela cotação (CA 2): nasce <c>Draft</c> com o número <c>PC</c> na mesma transação; as linhas de SC
    /// atendidas passam a <c>Ordered</c> (duas criações com a mesma linha: a segunda recebe 409).
    /// </summary>
    public async Task<Result<PurchaseOrderDetail>> CreateAsync(PurchaseOrderInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.RfqId is not null && input.QuotesCount is not > 0)
        {
            return Invalid("quotesCount", "Informe quantas respostas a cotação recebeu.");
        }

        if (input.LeadDays is < PurchaseRequestRules.MinLeadDays or > PurchaseRequestRules.MaxLeadDays)
        {
            return Invalid("leadDays", "O prazo vai de 0 a 365 dias.");
        }

        var expected = input.ExpectedDate ?? PurchaseOrderRules.DefaultExpectedDate(Today, input.LeadDays ?? await parameters.LeadDaysAsync(cancellationToken));
        var prepared = await PrepareAsync(new Draft(input.PartyId, input.ProjectId, input.PaymentTermId, expected, input.Freight, input.IpiPct, input.Notes, input.Lines), null, [], cancellationToken);
        if (prepared.IsFailure)
        {
            return prepared.Error!;
        }

        var p = prepared.Value;
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var order = new PurchaseOrder
        {
            Number = await numbers.NextAsync(PurchaseOrderRules.DocType, cancellationToken), RfqId = input.RfqId, QuotesCount = input.RfqId is null ? null : input.QuotesCount,
            RequestedBy = currentUser.UserId, CreatedAt = now, UpdatedAt = now,
        };
        Apply(order, p, now);
        db.PurchaseOrders.Add(order);
        var sequence = 0;
        foreach (var line in p.Lines)
        {
            db.PurchaseOrderLines.Add(NewLine(order.Id, ++sequence, line));
        }

        var touched = LinkRequests(p.Lines.SelectMany(l => l.RequestLines), now);
        await PurchaseRequestUpdates.RefreshStatusAsync(db, touched, now, cancellationToken, currentUser.UserId);
        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(order.Id, cancellationToken);
    }

    /// <summary>
    /// Editar (CA 3 e 15): o servidor recalcula tudo. No rascunho, só grava. Em aprovação, aprovado ou enviado (sem recebimento), a
    /// mudança de valor, fornecedor, condição, data ou itens volta o pedido a <c>PendingApproval</c> com nova resolução da alçada (as
    /// decisões anteriores ficam no histórico); os previstos de antes continuam até a reaprovação. Título previsto numa OP recusa (409).
    /// </summary>
    public async Task<Result<PurchaseOrderDetail>> UpdateAsync(Guid id, PurchaseOrderUpdateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var order = await db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        var lines = await db.PurchaseOrderLines.Where(l => l.OrderId == id).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        if (LockError(order, lines) is { } locked)
        {
            return locked;
        }

        if (order.Version != input.Version)
        {
            return Stale();
        }

        var linked = lines.SelectMany(l => l.RequestLineIds).ToHashSet();
        var draft = new Draft(input.PartyId, input.ProjectId, input.PaymentTermId, input.ExpectedDate, input.Freight, input.IpiPct, input.Notes, input.Lines);
        var prepared = await PrepareAsync(draft, order, linked, cancellationToken);
        if (prepared.IsFailure)
        {
            return prepared.Error!;
        }

        var p = prepared.Value;
        var material = IsMaterialChange(order, lines, p);
        if (!material && p.Notes == order.Notes)
        {
            return await DetailAsync(id, cancellationToken);
        }

        var reapprove = material && PurchaseOrderRules.NeedsReapproval(order.Status);
        if (reapprove && await InPaymentOrderAsync(order, cancellationToken) is { } inOrder)
        {
            return inOrder;
        }

        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Entry(order).Property(o => o.Version).OriginalValue = input.Version;
        var wasSent = order.Status == PurchaseOrderStatus.Sent;
        Apply(order, p, now);
        var touched = ReplaceLines(order, lines, p, now);
        if (reapprove)
        {
            await CloseAttemptAsync(order, PurchaseDecision.Changed, "Pedido alterado: volta para a aprovação.", now, cancellationToken);
            order.ChangedAfterSent = order.ChangedAfterSent || wasSent;
            var opened = await OpenAttemptAsync(order, now, cancellationToken);
            if (opened.IsFailure)
            {
                return opened.Error!;
            }

            events.Publish(new PurchaseOrderChanged(order.Id, order.Attempt, wasSent));
        }

        await PurchaseRequestUpdates.RefreshStatusAsync(db, touched, now, cancellationToken, currentUser.UserId);
        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>Editar uma linha (quantidade e preço) pelo pedido da URL (IDOR, CA 21): a mesma regra da edição do pedido.</summary>
    public async Task<Result<PurchaseOrderDetail>> UpdateLineAsync(Guid id, Guid lineId, PurchaseOrderLineUpdate input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var order = await db.PurchaseOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        var lines = order is null ? [] : await db.PurchaseOrderLines.AsNoTracking().Where(l => l.OrderId == id).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        if (order is null || lines.All(l => l.Id != lineId))
        {
            return Error.NotFound("purchase_order_line_not_found", "Linha não encontrada neste pedido.");
        }

        var inputs = lines.Select(l => l.Id == lineId
            ? new PurchaseOrderLineInput(l.ItemId, input.Quantity, null, input.UnitPrice, l.RequestLineIds)
            : new PurchaseOrderLineInput(l.ItemId, l.Quantity, null, l.UnitPrice, l.RequestLineIds)).ToList();
        return await UpdateAsync(id, new PurchaseOrderUpdateInput(order.PartyId, order.ProjectId, order.PaymentTermId, order.ExpectedDate, order.Freight, order.IpiPct, order.Notes, inputs, input.Version),
            cancellationToken);
    }

    /// <summary>
    /// "Cancelar pedido" (CA 16): sem recebimento, na mesma transação o pedido fica <c>Cancelled</c>, os títulos previstos são cancelados
    /// pela origem (409 se algum está em OP ou pago) e as linhas de SC voltam a <c>Open</c>. Com recebimento: 409.
    /// </summary>
    public async Task<Result<PurchaseOrderDetail>> CancelAsync(Guid id, PurchaseOrderCancelInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var order = await db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        var lines = await db.PurchaseOrderLines.Where(l => l.OrderId == id).ToListAsync(cancellationToken);
        if (LockError(order, lines) is { } locked)
        {
            return locked;
        }

        var reason = Clean(input.Reason);
        if (reason is null || reason.Length > PurchaseOrderRules.MaxReason)
        {
            return Invalid("reason", $"Informe o motivo, com até {PurchaseOrderRules.MaxReason} caracteres.");
        }

        if (order.Version != input.Version)
        {
            return Stale();
        }

        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var cancelled = await payables.CancelAllFromOriginAsync(DocumentOrigin.Purchase, order.Id, $"pedido {order.Number} cancelado ({reason})", cancellationToken);
        if (cancelled.IsFailure)
        {
            return cancelled.Error!;
        }

        db.Entry(order).Property(o => o.Version).OriginalValue = input.Version;
        await CloseAttemptAsync(order, PurchaseDecision.Cancelled, reason, now, cancellationToken);
        order.Status = PurchaseOrderStatus.Cancelled;
        order.CancelReason = reason;
        order.CancelledAt = now;
        order.CancelledBy = currentUser.UserId;
        order.UpdatedAt = now;
        var touched = await UnlinkRequestsAsync(lines.SelectMany(l => l.RequestLineIds).ToList(), now, cancellationToken);
        await PurchaseRequestUpdates.RefreshStatusAsync(db, touched, now, cancellationToken, currentUser.UserId);
        events.Publish(new PurchaseOrderCancelled(order.Id));
        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>Pedido cancelado, recebido ou com recebimento não muda (CA 15, 16).</summary>
    private static Error? LockError(PurchaseOrder order, IReadOnlyCollection<PurchaseOrderLine> lines) =>
        order.Status == PurchaseOrderStatus.Cancelled
            ? Error.Conflict(LockedCode, $"O pedido {order.Number} está cancelado.")
            : !PurchaseOrderRules.IsChangeable(order.Status, lines.Sum(l => l.QuantityReceived))
                ? Error.Conflict(ReceivedCode, "Já houve recebimento: encerre o saldo do pedido.")
                : null;

    /// <summary>Algum previsto do pedido numa OP (CA 15): 409 <c>payable_in_payment_order</c> com o link para a OP.</summary>
    private async Task<Error?> InPaymentOrderAsync(PurchaseOrder order, CancellationToken ct)
    {
        var current = await payables.ListByOriginAsync(DocumentOrigin.Purchase, order.Id, ct);
        if (current.FirstOrDefault(p => p.Status is PayableStatus.AwaitingApproval or PayableStatus.Approved) is { } inOrder)
        {
            return Error.Conflict("payable_in_payment_order", $"O título previsto {inOrder.Number} está numa ordem de pagamento. Retire-o da ordem antes de alterar o pedido.")
                with { Data = new Dictionary<string, object?> { ["paymentOrderId"] = inOrder.PaymentOrderId, ["payableId"] = inOrder.Id } };
        }

        return current.FirstOrDefault(p => p.Status is PayableStatus.PartiallyPaid or PayableStatus.Paid) is { } paid
            ? Error.Conflict("payable_paid", $"O título {paid.Number} já teve pagamento. O pedido não muda mais.")
            : null;
    }

    private static void Apply(PurchaseOrder order, Prepared p, DateTimeOffset now)
    {
        order.PartyId = p.PartyId;
        order.ProjectId = p.ProjectId;
        order.PaymentTermId = p.TermId;
        order.ExpectedDate = p.ExpectedDate;
        order.Freight = p.Freight;
        order.IpiPct = p.IpiPct;
        order.Subtotal = p.Totals.Subtotal;
        order.IpiAmount = p.Totals.IpiAmount;
        order.Total = p.Totals.Total;
        order.Notes = p.Notes;
        order.UpdatedAt = now;
    }

    private static PurchaseOrderLine NewLine(Guid orderId, int sequence, PreparedLine line) => new()
    {
        OrderId = orderId, Sequence = sequence, ItemId = line.Item.Id, Quantity = line.Quantity, EnteredQuantity = line.EnteredQuantity, EnteredUnitId = line.EnteredUnitId,
        UnitPrice = line.UnitPrice, LineTotal = PurchaseOrderRules.LineTotal(line.Quantity, line.UnitPrice), CostType = line.CostType,
        RequestLineIds = line.RequestLines.Select(r => r.Id).ToArray(),
    };

    /// <summary>Troca as linhas pela edição: a do mesmo item é atualizada, a nova entra e a que saiu é removida (sem recebimento); os vínculos com a SC acompanham.</summary>
    private List<Guid> ReplaceLines(PurchaseOrder order, List<PurchaseOrderLine> current, Prepared p, DateTimeOffset now)
    {
        var before = current.SelectMany(l => l.RequestLineIds).ToHashSet();
        var after = p.Lines.SelectMany(l => l.RequestLines.Select(r => r.Id)).ToHashSet();
        var sequence = 0;
        foreach (var line in p.Lines)
        {
            sequence++;
            if (current.FirstOrDefault(l => l.ItemId == line.Item.Id) is { } same)
            {
                same.Sequence = sequence;
                same.Quantity = line.Quantity;
                same.EnteredQuantity = line.EnteredQuantity;
                same.EnteredUnitId = line.EnteredUnitId;
                same.UnitPrice = line.UnitPrice;
                same.LineTotal = PurchaseOrderRules.LineTotal(line.Quantity, line.UnitPrice);
                same.CostType = line.CostType;
                same.RequestLineIds = line.RequestLines.Select(r => r.Id).ToArray();
            }
            else
            {
                db.PurchaseOrderLines.Add(NewLine(order.Id, sequence, line));
            }
        }

        var kept = p.Lines.Select(l => l.Item.Id).ToHashSet();
        foreach (var gone in current.Where(l => !kept.Contains(l.ItemId)))
        {
            db.PurchaseOrderLines.Remove(gone);
        }

        // As sequências são únicas por pedido: as linhas mantidas são renumeradas acima e as novas vêm depois.
        var touched = LinkRequests(p.Lines.SelectMany(l => l.RequestLines).Where(r => !before.Contains(r.Id)), now);
        var released = db.PurchaseRequestLines.Local.Where(r => before.Contains(r.Id) && !after.Contains(r.Id)).ToList();
        foreach (var line in released)
        {
            Unlink(line, now);
            touched.Add(line.RequestId);
        }

        return touched;
    }

    /// <summary>Linhas de SC atendidas passam a <c>Ordered</c> (CA 1).</summary>
    private static List<Guid> LinkRequests(IEnumerable<PurchaseRequestLine> lines, DateTimeOffset now)
    {
        var touched = new List<Guid>();
        foreach (var line in lines)
        {
            line.Status = PurchaseRequestStatus.Ordered;
            line.OrderedQuantity = line.Quantity;
            line.UpdatedAt = now;
            touched.Add(line.RequestId);
        }

        return touched.Distinct().ToList();
    }

    /// <summary>Cancelamento (CA 16): as linhas de SC do pedido voltam a <c>Open</c>.</summary>
    private async Task<List<Guid>> UnlinkRequestsAsync(List<Guid> ids, DateTimeOffset now, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var lines = await db.PurchaseRequestLines.Where(l => ids.Contains(l.Id) && l.Status == PurchaseRequestStatus.Ordered).ToListAsync(ct);
        foreach (var line in lines)
        {
            Unlink(line, now);
        }

        return lines.Select(l => l.RequestId).Distinct().ToList();
    }

    private static void Unlink(PurchaseRequestLine line, DateTimeOffset now)
    {
        line.Status = PurchaseRequestStatus.Open;
        line.OrderedQuantity = 0m;
        line.UpdatedAt = now;
    }

    private static bool IsMaterialChange(PurchaseOrder order, List<PurchaseOrderLine> lines, Prepared p) =>
        order.PartyId != p.PartyId || order.ProjectId != p.ProjectId || order.PaymentTermId != p.TermId || order.ExpectedDate != p.ExpectedDate || order.Freight != p.Freight
        || order.IpiPct != p.IpiPct || lines.Count != p.Lines.Count
        || lines.Zip(p.Lines).Any(x => x.First.ItemId != x.Second.Item.Id || x.First.Quantity != x.Second.Quantity || x.First.UnitPrice != x.Second.UnitPrice
            || !x.First.RequestLineIds.Order().SequenceEqual(x.Second.RequestLines.Select(r => r.Id).Order()));

    /// <summary>Grava; dois comandos ao mesmo tempo no pedido ou na mesma linha de SC: o segundo recebe 409 (CA 22).</summary>
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
    }

    internal static Error NotFound() => Error.NotFound("purchase_order_not_found", "Pedido de compra não encontrado.");

    private static Error Stale() => Error.Conflict("concurrency_conflict", "O pedido mudou desde que você abriu. Recarregue.");

    internal static Error Invalid(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
