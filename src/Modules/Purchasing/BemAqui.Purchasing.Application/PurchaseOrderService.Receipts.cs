using BemAqui.BuildingBlocks.Authorization;
using BemAqui.Finance.Contracts;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Purchasing.Application;

/// <summary>Segregação do recebimento (CMP-04 CA 2, 14): quem está envolvido no pedido, se a regra do Proprietário único libera e o motivo.</summary>
public sealed record ReceiveSegregation(bool Involved, bool SoleOwner, string? Message);

public sealed partial class PurchaseOrderService
{
    public const string SegregationCode = "segregation_of_duties";
    public const string NotOpenForReceiptCode = "purchase_order_not_open_for_receipt";
    public const string ReapprovalBlocker = "Pedido em reaprovação";

    /// <summary>
    /// Quem pediu, decidiu algum nível da aprovação ou corrigiu o preço do pedido não registra o recebimento dele (CA 2, 14). Com
    /// <c>SoleOwnerRule</c> (nenhum outro usuário ativo com <c>purchasing.receive</c> fora dos envolvidos), passa com a marca.
    /// </summary>
    internal async Task<ReceiveSegregation> ReceiveSegregationAsync(PurchaseOrder order, CancellationToken ct)
    {
        if (currentUser.UserId is not { } me)
        {
            return new ReceiveSegregation(false, false, null);
        }

        var deciders = await db.PurchaseOrderApprovals.AsNoTracking()
            .Where(a => a.OrderId == order.Id && (a.Decision == PurchaseDecision.Approved || a.Decision == PurchaseDecision.Rejected)).Select(a => a.DecidedBy).ToListAsync(ct);
        var correctors = await db.PurchaseOrderPriceCorrections.AsNoTracking().Where(c => c.OrderId == order.Id).Select(c => c.CorrectedBy).ToListAsync(ct);
        if (!GoodsReceiptRules.IsInvolved(me, order.RequestedBy, deciders, correctors))
        {
            return new ReceiveSegregation(false, false, null);
        }

        var involved = deciders.Concat(correctors).Append(order.RequestedBy).OfType<Guid>().Distinct().ToList();
        if (await SoleOwnerRule.NoOtherEligibleAsync(users, "purchasing.receive", me, involved, ct))
        {
            return new ReceiveSegregation(true, true, null);
        }

        return new ReceiveSegregation(true, false, correctors.Contains(me) && order.RequestedBy != me && !deciders.Contains(me)
            ? "Quem corrigiu o preço deste pedido não registra o recebimento dele."
            : "Quem pediu ou aprovou este pedido não registra o recebimento dele.");
    }

    /// <summary>Por que "Registrar recebimento" está desabilitado agora (CA 2, 14 e 16); null quando pode.</summary>
    internal async Task<string?> ReceiveBlockerAsync(PurchaseOrder order, IReadOnlyCollection<PurchaseOrderLine> lines, CancellationToken ct)
    {
        if (order.Status == PurchaseOrderStatus.PendingApproval && order.ApprovedTotal is not null)
        {
            return ReapprovalBlocker;
        }

        if (!PurchaseOrderRules.OpenForReceipt.Contains(order.Status))
        {
            return order.Status == PurchaseOrderStatus.Received ? "O pedido já foi todo recebido." : "O pedido ainda não foi aprovado.";
        }

        if (lines.All(l => l.QuantityReceived >= l.Quantity))
        {
            return "O pedido já foi todo recebido.";
        }

        return (await ReceiveSegregationAsync(order, ct)).Message;
    }

    /// <summary>Σ dos recebimentos registrados do pedido (os estornados ficam fora): o que já virou título do recebimento.</summary>
    internal Task<decimal> ReceivedAmountAsync(Guid orderId, CancellationToken ct) =>
        db.GoodsReceipts.Where(r => r.PurchaseOrderId == orderId && r.Status == GoodsReceiptStatus.Completed).SumAsync(r => r.Amount, ct);

    /// <summary>
    /// Replaneja os títulos previstos do pedido (origem <c>Purchase</c>, ADR 0024 §3, CA 9) para o saldo a receber: total − Σ recebimentos
    /// registrados, pela condição a partir da previsão de entrega; pedido recebido, cancelado ou sem saldo: os previstos são cancelados.
    /// Previsto numa OP ou pago: 409 e nada muda (quem chama desfaz a transação).
    /// </summary>
    internal async Task<Result> ReplanAsync(PurchaseOrder order, string reason, CancellationToken ct)
    {
        var balance = GoodsReceiptRules.PlannedBalance(order.Total, await ReceivedAmountAsync(order.Id, ct));
        if (order.Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.Cancelled || balance <= 0m)
        {
            return await payables.CancelAllFromOriginAsync(DocumentOrigin.Purchase, order.Id, reason, ct);
        }

        var dues = await terms.SplitAsync(order.PaymentTermId, balance, order.ExpectedDate, ct);
        if (dues is null || dues.Count == 0)
        {
            return Invalid("paymentTermId", "A condição de pagamento do pedido não existe mais. Edite o pedido e escolha outra.");
        }

        Guid accountId;
        if (order.AccountId is { } stored)
        {
            accountId = stored;
        }
        else
        {
            var account = await AccountIdAsync(ct);
            if (account.IsFailure)
            {
                return account.Error!;
            }

            accountId = account.Value;
        }

        var defaults = await parameters.PayableDefaultsAsync(ct);
        var plan = new PlannedPayables(order.PartyId, order.ProjectId, accountId, defaults.CostType,
            dues.Select(d => new PlannedInstallment((short)d.Number, d.Amount, d.DueDate, PurchaseOrderRules.InstallmentDescription(order.Number, d.Number, dues.Count))).ToList());
        var existing = await payables.ListByOriginAsync(DocumentOrigin.Purchase, order.Id, ct);
        var result = existing.Any(p => p.Status != PayableStatus.Cancelled)
            ? await payables.ReplacePlannedAsync(DocumentOrigin.Purchase, order.Id, plan, ct)
            : await payables.CreatePlannedInstallmentsAsync(DocumentOrigin.Purchase, order.Id, plan, ct);
        return result.IsFailure ? result.Error! : Result.Success();
    }

    /// <summary>
    /// "Encerrar saldo do pedido" (CA 13, <c>purchasing.edit</c>): o pedido parcialmente recebido cujo saldo não virá fica <c>Received</c>
    /// com a marca "Encerrado com saldo de {x}", os previstos restantes são cancelados e as linhas de SC ligadas voltam a <c>Open</c> pela
    /// quantidade não recebida; tudo na mesma transação. <c>PurchaseOrderClosed</c> vai ao outbox só como aviso.
    /// </summary>
    public async Task<Result<PurchaseOrderDetail>> CloseBalanceAsync(Guid id, PurchaseOrderCloseInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var order = await db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status != PurchaseOrderStatus.PartiallyReceived)
        {
            return Error.Conflict("purchase_order_not_partially_received", "Só o pedido parcialmente recebido tem saldo para encerrar.");
        }

        var reason = Clean(input.Reason);
        if (reason is null || reason.Length > GoodsReceiptRules.MaxReason)
        {
            return Invalid("reason", $"Informe o motivo, com até {GoodsReceiptRules.MaxReason} caracteres.");
        }

        if (order.Version != input.Version)
        {
            return Stale();
        }

        var lines = await db.PurchaseOrderLines.Where(l => l.OrderId == id).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var balance = GoodsReceiptRules.PlannedBalance(order.Total, await ReceivedAmountAsync(order.Id, cancellationToken));
        var cancelled = await payables.CancelAllFromOriginAsync(DocumentOrigin.Purchase, order.Id, $"saldo do pedido {order.Number} encerrado ({reason})", cancellationToken);
        if (cancelled.IsFailure)
        {
            return cancelled.Error!;
        }

        db.Entry(order).Property(o => o.Version).OriginalValue = input.Version;
        order.Status = PurchaseOrderStatus.Received;
        order.ClosedBalanceAmount = balance;
        order.CloseReason = reason;
        order.ClosedAt = now;
        order.ClosedBy = currentUser.UserId;
        order.UpdatedAt = now;
        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        var touched = new HashSet<Guid>();
        foreach (var line in lines.Where(l => l.Quantity > l.QuantityReceived && l.RequestLineIds.Length > 0))
        {
            await ReopenRequestLinesAsync(line, line.Quantity - line.QuantityReceived, order.Number, now, touched, cancellationToken);
        }

        // A linha de SC mudou ao mesmo tempo (edição, cotação): 409, e nada grava.
        if (await SaveAsync(cancellationToken) is { } requestConflict)
        {
            return requestConflict;
        }

        await PurchaseRequestUpdates.RefreshStatusAsync(db, touched, now, cancellationToken, currentUser.UserId);
        events.Publish(new PurchaseOrderClosed(order.Id, balance));
        if (await SaveAsync(cancellationToken) is { } late)
        {
            return late;
        }

        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>
    /// Devolve à SC a quantidade não recebida da linha do pedido (CA 13), da última linha de SC para a primeira: a linha não coberta volta a
    /// <c>Open</c>; a coberta em parte fica <c>Ordered</c> com o recebido e o resto nasce numa linha aberta; havendo já uma linha aberta da
    /// mesma falta (OS e item) ou do mesmo mínimo, a quantidade vai para ela (os índices únicos das linhas abertas não deixam duas).
    /// </summary>
    private async Task ReopenRequestLinesAsync(PurchaseOrderLine poLine, decimal notReceived, string orderNumber, DateTimeOffset now, HashSet<Guid> touched, CancellationToken ct)
    {
        var ids = poLine.RequestLineIds.ToList();
        var requestLines = await db.PurchaseRequestLines.Where(l => ids.Contains(l.Id) && l.Status == PurchaseRequestStatus.Ordered).OrderByDescending(l => l.Sequence).ToListAsync(ct);
        var left = notReceived;
        foreach (var line in requestLines)
        {
            if (left <= 0m)
            {
                break;
            }

            var back = Math.Min(left, line.Quantity);
            left -= back;
            touched.Add(line.RequestId);
            var twin = await OpenTwinAsync(line, ct);
            if (twin is not null)
            {
                twin.Quantity += back;
                twin.UpdatedAt = now;
                touched.Add(twin.RequestId);
                if (back >= line.Quantity)
                {
                    PurchaseRequestUpdates.Cancel(line, $"Saldo do pedido {orderNumber} encerrado: a quantidade voltou para a linha aberta do mesmo item.", now);
                    line.OrderedQuantity = 0m;
                }
                else
                {
                    Shrink(line, back, now);
                }

                continue;
            }

            if (back >= line.Quantity)
            {
                Unlink(line, now);
                continue;
            }

            Shrink(line, back, now);
            var sequence = await db.PurchaseRequestLines.Where(l => l.RequestId == line.RequestId).MaxAsync(l => (int?)l.Sequence, ct) ?? 0;
            sequence = Math.Max(sequence, db.PurchaseRequestLines.Local.Where(l => l.RequestId == line.RequestId).Select(l => l.Sequence).DefaultIfEmpty(0).Max());
            db.PurchaseRequestLines.Add(new PurchaseRequestLine
            {
                RequestId = line.RequestId, Origin = line.Origin, Sequence = sequence + 1, ItemId = line.ItemId, Quantity = back, NeededBy = line.NeededBy,
                ProductionOrderId = line.ProductionOrderId, BomLineId = line.BomLineId, ProjectId = line.ProjectId, ManuallyEdited = line.ManuallyEdited, CreatedAt = now,
                UpdatedAt = now,
            });
        }
    }

    /// <summary>A linha aberta que ocupa o índice único da falta (OS e item) ou do mínimo (item), se houver.</summary>
    private async Task<PurchaseRequestLine?> OpenTwinAsync(PurchaseRequestLine line, CancellationToken ct)
    {
        if (line.ManuallyEdited || line.Origin == PurchaseRequestOrigin.Manual)
        {
            return null;
        }

        bool Matches(PurchaseRequestLine l) => l.Id != line.Id && l.Status == PurchaseRequestStatus.Open && !l.ManuallyEdited && l.Origin == line.Origin && l.ItemId == line.ItemId
            && (line.Origin == PurchaseRequestOrigin.MinStock || l.ProductionOrderId == line.ProductionOrderId);
        if (db.PurchaseRequestLines.Local.FirstOrDefault(Matches) is { } local)
        {
            return local;
        }

        return line.Origin == PurchaseRequestOrigin.MinStock
            ? await db.PurchaseRequestLines.FirstOrDefaultAsync(l => l.Id != line.Id && l.Status == PurchaseRequestStatus.Open && !l.ManuallyEdited && l.Origin == line.Origin && l.ItemId == line.ItemId, ct)
            : await db.PurchaseRequestLines.FirstOrDefaultAsync(
                l => l.Id != line.Id && l.Status == PurchaseRequestStatus.Open && !l.ManuallyEdited && l.Origin == line.Origin && l.ItemId == line.ItemId && l.ProductionOrderId == line.ProductionOrderId, ct);
    }

    /// <summary>A linha de SC coberta em parte fica com o que foi recebido (continua <c>Ordered</c>).</summary>
    private static void Shrink(PurchaseRequestLine line, decimal back, DateTimeOffset now)
    {
        line.Quantity -= back;
        line.OrderedQuantity = line.Quantity;
        line.EnteredQuantity = null;
        line.EnteredUnitId = null;
        line.UpdatedAt = now;
    }

    /// <summary>
    /// "Corrigir preço" (CA 14, <c>purchasing.approve</c>, motivo obrigatório): o preço novo vale só para o saldo a receber da linha (as
    /// entradas feitas não mudam). Quem registrou recebimento do pedido não corrige o preço dele (403). Dentro do teto (aprovado +
    /// tolerância), os previstos são replanejados na mesma transação; acima, o pedido volta à alçada (CMP-03a CA 15) e o recebimento fica
    /// desabilitado com "Pedido em reaprovação" até a nova aprovação.
    /// </summary>
    public async Task<Result<PurchaseOrderDetail>> CorrectPriceAsync(Guid id, Guid lineId, PurchaseOrderPriceInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("purchasing", "approve", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Quem corrige o preço do pedido é quem tem a permissão Aprovar em Compras.");
        }

        var order = await db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        var lines = order is null ? [] : await db.PurchaseOrderLines.Where(l => l.OrderId == id).OrderBy(l => l.Sequence).ToListAsync(cancellationToken);
        var line = lines.FirstOrDefault(l => l.Id == lineId);
        if (order is null || line is null)
        {
            return Error.NotFound("purchase_order_line_not_found", "Linha não encontrada neste pedido.");
        }

        if (!PurchaseOrderRules.OpenForReceipt.Contains(order.Status))
        {
            return Error.Conflict(NotOpenForReceiptCode, "Só o pedido aprovado com saldo a receber tem o preço corrigido.");
        }

        if (line.QuantityReceived >= line.Quantity)
        {
            return Invalid("unitPrice", "A linha já foi toda recebida: o preço não muda mais.");
        }

        var reason = Clean(input.Reason);
        if (reason is null || reason.Length > GoodsReceiptRules.MaxReason)
        {
            return Invalid("reason", $"Informe o motivo da correção, com até {GoodsReceiptRules.MaxReason} caracteres.");
        }

        if (input.UnitPrice <= 0m || input.UnitPrice > PurchaseOrderRules.MaxUnitPrice || decimal.Round(input.UnitPrice, 6) != input.UnitPrice)
        {
            return Invalid("unitPrice", "Informe um preço maior que zero, com até 6 casas.");
        }

        if (input.UnitPrice == line.UnitPrice)
        {
            return Invalid("unitPrice", "O preço novo é igual ao do pedido.");
        }

        if (order.Version != input.Version)
        {
            return Stale();
        }

        var me = currentUser.UserId;
        if (me is { } user && await db.GoodsReceipts.AnyAsync(r => r.PurchaseOrderId == id && r.ReceivedBy == user, cancellationToken))
        {
            return Error.Forbidden(SegregationCode, "Quem registrou recebimento deste pedido não corrige o preço dele.");
        }

        var previous = line.UnitPrice;
        line.UnitPrice = input.UnitPrice;
        line.LineTotal = line.AmountReceived + PurchaseOrderRules.LineTotal(line.Quantity - line.QuantityReceived, input.UnitPrice);
        Recalculate(order, lines);
        var tolerance = await parameters.ReceiptToleranceAsync(cancellationToken);
        var reapproval = order.Total > GoodsReceiptRules.Ceiling(order.ApprovedTotal ?? order.Total, tolerance);
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Entry(order).Property(o => o.Version).OriginalValue = input.Version;
        db.PurchaseOrderPriceCorrections.Add(new PurchaseOrderPriceCorrection
        {
            OrderId = order.Id, PoLineId = line.Id, PreviousPrice = previous, NewPrice = input.UnitPrice, Reason = reason, Reapproval = reapproval, CorrectedBy = me, CorrectedAt = now,
        });
        order.UpdatedAt = now;
        if (reapproval)
        {
            var opened = await OpenAttemptAsync(order, now, cancellationToken);
            if (opened.IsFailure)
            {
                return opened.Error!;
            }
        }
        else
        {
            var replanned = await ReplanAsync(order, $"preço do pedido {order.Number} corrigido", cancellationToken);
            if (replanned.IsFailure)
            {
                return replanned.Error!;
            }
        }

        events.Publish(new PurchaseOrderPriceCorrected(order.Id, line.Id, previous, input.UnitPrice, reapproval));
        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(id, cancellationToken);
    }

    /// <summary>Totais do pedido a partir dos totais das linhas (a linha corrigida já soma o recebido + o saldo pelo preço novo).</summary>
    private static void Recalculate(PurchaseOrder order, IReadOnlyCollection<PurchaseOrderLine> lines)
    {
        order.Subtotal = lines.Sum(l => l.LineTotal);
        order.IpiAmount = Math.Round(order.Subtotal * order.IpiPct / 100m, 2, MidpointRounding.AwayFromZero);
        order.Total = order.Subtotal + order.Freight + order.IpiAmount;
    }

    /// <summary>
    /// Reprovação de uma reaprovação com recebimento (CA 14): os preços corrigidos depois da última aprovação voltam ao que eram, o pedido
    /// volta a <c>PartiallyReceived</c> (nunca a rascunho: já há entrada no estoque e título).
    /// </summary>
    private async Task RevertCorrectionsAsync(PurchaseOrder order, CancellationToken ct)
    {
        var since = order.ApprovedAt ?? DateTimeOffset.MinValue;
        var corrections = await db.PurchaseOrderPriceCorrections.AsNoTracking().Where(c => c.OrderId == order.Id && c.CorrectedAt > since).OrderBy(c => c.CorrectedAt).ToListAsync(ct);
        var lines = await db.PurchaseOrderLines.Where(l => l.OrderId == order.Id).ToListAsync(ct);
        foreach (var first in corrections.GroupBy(c => c.PoLineId).Select(g => g.First()))
        {
            if (lines.FirstOrDefault(l => l.Id == first.PoLineId) is { } line)
            {
                line.UnitPrice = first.PreviousPrice;
                line.LineTotal = line.AmountReceived + PurchaseOrderRules.LineTotal(Math.Max(0m, line.Quantity - line.QuantityReceived), first.PreviousPrice);
            }
        }

        Recalculate(order, lines);
        order.Status = PurchaseOrderStatus.PartiallyReceived;
    }
}
