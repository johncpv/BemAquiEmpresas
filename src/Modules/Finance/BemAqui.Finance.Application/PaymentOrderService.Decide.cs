using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public sealed partial class PaymentOrderService
{
    private static Error Decided() => Error.Conflict("payment_order_decided", "A ordem já foi decidida.");

    /// <summary>Aprovar (CA 7): a OP e os títulos passam a Approved; quem paga é avisado. Quem solicitou só nas exceções da D6.</summary>
    public Task<Result<PaymentOrderDetail>> ApproveAsync(Guid id, DecisionInput input, CancellationToken cancellationToken = default) =>
        DecideAsync(id, input, PaymentOrderDecision.Approved, cancellationToken);

    /// <summary>Reprovar (CA 8): comentário obrigatório; os títulos voltam a Open e o solicitante é avisado.</summary>
    public Task<Result<PaymentOrderDetail>> RejectAsync(Guid id, DecisionInput input, CancellationToken cancellationToken = default) =>
        DecideAsync(id, input, PaymentOrderDecision.Rejected, cancellationToken);

    private async Task<Result<PaymentOrderDetail>> DecideAsync(Guid id, DecisionInput input, PaymentOrderDecision decision, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("finance", "approve", ct))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Quem aprova a ordem é quem tem a alçada de aprovação.");
        }

        var comment = string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim();
        if (decision == PaymentOrderDecision.Rejected && comment is null)
        {
            return ReceivableService.Invalid("comment", "Escreva o motivo da reprovação.");
        }

        if (comment is { Length: > 500 })
        {
            return ReceivableService.Invalid("comment", "Escreva o comentário com até 500 caracteres.");
        }

        var order = await db.PaymentOrders.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status != PaymentOrderStatus.Pending || order.Version != input.Version)
        {
            return Decided();
        }

        var (allowed, self) = await DecisionRuleAsync(order, ct);
        if (!allowed)
        {
            return Error.Forbidden("own_payment_order", "Quem solicitou a ordem não pode aprová-la.");
        }

        // Alçada multinível (FIN-03b CA 12 a 15): decide o nível pendente mais baixo, só quem está na regra dele.
        var level = await CurrentLevelAsync(order.Id, ct);
        if (level is not null && await LevelErrorAsync(order, level, ct) is { } outside)
        {
            return outside;
        }

        db.Entry(order).Property(o => o.Version).OriginalValue = input.Version;
        var approved = decision == PaymentOrderDecision.Approved;
        db.PaymentOrderApprovals.Add(new PaymentOrderApproval
        {
            PaymentOrderId = order.Id, Level = level?.Level ?? 1, Kind = PaymentOrderDecisionKind.Decision, Decision = decision, Comment = comment, DecidedBy = currentUser.UserId,
            DecidedAt = clock.GetUtcNow(), SelfApproved = approved && self,
        });
        if (level is not null)
        {
            level.Status = approved ? LevelStatus.Approved : LevelStatus.Rejected;
            var next = approved ? await db.PaymentOrderLevels.Where(l => l.PaymentOrderId == order.Id && l.Status == LevelStatus.Pending && l.Level > level.Level).OrderBy(l => l.Level).FirstOrDefaultAsync(ct) : null;
            if (next is not null)
            {
                order.SelfApproved = order.SelfApproved || self;
                db.Entry(order).Property(o => o.SubmittedAt).IsModified = true;
                await NotifyLevelAsync(order, next.Level, await resolver.EligibleAsync(next, order.RequestedBy, ct), ct);
                return await SaveDecisionAsync(order, ct);
            }
        }

        order.Status = approved ? PaymentOrderStatus.Approved : PaymentOrderStatus.Rejected;
        order.SelfApproved = approved && (self || order.SelfApproved);
        foreach (var payable in await PayablesOfAsync(order, ct))
        {
            if (approved)
            {
                payable.Status = PayableStatus.Approved;
            }
            else
            {
                Release(payable);
            }
        }

        if (approved)
        {
            events.Publish(new PaymentOrderApproved(order.Id));
            foreach (var user in await users.UsersWithPermissionAsync("finance.pay", ct))
            {
                notifications.Notify(user, NotificationType, $"OP {order.Number} aprovada. Pode pagar.", Link(order) + "?acao=pagar");
            }
        }
        else if (order.RequestedBy is { } requester)
        {
            notifications.Notify(requester, NotificationType, $"OP {order.Number} reprovada: {Cut(comment!, 120)}", Link(order));
        }

        return await SaveDecisionAsync(order, ct);
    }

    /// <summary>
    /// Cancelar (CA 20): a pendente pelo solicitante ou por quem aprova; a aprovada só por quem aprova; a paga, nunca
    /// (estorne o pagamento). Os títulos voltam a Open.
    /// </summary>
    public async Task<Result<PaymentOrderDetail>> CancelAsync(Guid id, ReasonInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 200)
        {
            return ReceivableService.Invalid("reason", "Informe o motivo, com até 200 caracteres.");
        }

        var order = await db.PaymentOrders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status == PaymentOrderStatus.Paid)
        {
            return Error.Conflict("payment_order_paid", "Estorne o pagamento.");
        }

        if (order.Status is not (PaymentOrderStatus.Pending or PaymentOrderStatus.Approved) || order.Version != input.Version)
        {
            return Decided();
        }

        var approver = await permissions.HasPermissionAsync("finance", "approve", cancellationToken);
        var requester = order.RequestedBy is { } r && r == currentUser.UserId;
        if (!(approver || (requester && order.Status == PaymentOrderStatus.Pending)))
        {
            return Error.Forbidden(Error.Codes.Forbidden, order.Status == PaymentOrderStatus.Approved
                ? "Ordem aprovada só é cancelada por quem aprova."
                : "Só quem solicitou ou quem aprova cancela a ordem.");
        }

        db.Entry(order).Property(o => o.Version).OriginalValue = input.Version;
        order.Status = PaymentOrderStatus.Cancelled;
        order.CancelReason = input.Reason.Trim();
        order.CancelledAt = clock.GetUtcNow();
        order.CancelledBy = currentUser.UserId;
        foreach (var payable in await PayablesOfAsync(order, cancellationToken))
        {
            Release(payable);
        }

        return await SaveDecisionAsync(order, cancellationToken);
    }

    /// <summary>Fora da alçada (CA 15) ou já decidiu outro nível (CA 13): 403 com o motivo.</summary>
    private async Task<Error?> LevelErrorAsync(PaymentOrder order, PaymentOrderLevel level, CancellationToken ct)
    {
        var me = currentUser.UserId;
        var other = await db.PaymentOrderApprovals.AsNoTracking()
            .Where(a => a.PaymentOrderId == order.Id && a.DecidedBy == me && a.Decision == PaymentOrderDecision.Approved && a.Level != level.Level && a.Kind == PaymentOrderDecisionKind.Decision
                && a.DecidedAt >= order.SubmittedAt)
            .Select(a => (int?)a.Level).FirstOrDefaultAsync(ct);
        if (other is { } decided)
        {
            return Error.Forbidden("already_decided_level", $"Você já aprovou o nível {decided} desta ordem.");
        }

        return me is { } user && (await resolver.EligibleAsync(level, order.RequestedBy, ct)).Contains(user)
            ? null
            : Error.Forbidden("outside_approval_rule", "Esta ordem não está na sua alçada.");
    }

    /// <summary>Regra do solicitante (CA 9 e 10) com o Proprietário de agora e o parâmetro.</summary>
    private async Task<(bool Allowed, bool Self)> DecisionRuleAsync(PaymentOrder order, CancellationToken ct)
    {
        var isRequester = order.RequestedBy is { } r && r == currentUser.UserId;
        if (!isRequester)
        {
            return (true, false);
        }

        var owners = await users.OwnerIdsAsync(ct);
        var limits = await parameters.LimitsAsync(ct);
        return PaymentOrderRules.CanDecide(true, owners.Contains(order.RequestedBy!.Value), owners.Count, limits.OwnerCanApproveOwn);
    }

    private async Task<List<Payable>> PayablesOfAsync(PaymentOrder order, CancellationToken ct)
    {
        var ids = await db.PaymentOrderItems.Where(i => i.PaymentOrderId == order.Id).Select(i => i.PayableId).ToListAsync(ct);
        return await db.Payables.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
    }

    /// <summary>Título sai da OP: Open, ou PartiallyPaid se já tinha pagamento anterior.</summary>
    private static void Release(Payable payable) =>
        payable.Status = payable.PaidAmount > 0 ? PayableStatus.PartiallyPaid : PayableStatus.Open;

    /// <summary>Grava a transição; duas decisões ao mesmo tempo: a segunda recebe 409 (CA 11 e 17).</summary>
    private async Task<Result<PaymentOrderDetail>> SaveDecisionAsync(PaymentOrder order, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Decided();
        }

        return await DetailAsync(order, ct);
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
