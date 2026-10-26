using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public sealed partial class PaymentOrderService
{
    /// <summary>Grava a resolução do envio (CA 6, 10) e libera o nível 1.</summary>
    private async Task<Result> ResolveLevelsAsync(PaymentOrder order, IReadOnlyCollection<Payable> payables, CancellationToken ct)
    {
        var resolution = await resolver.ResolveAsync(order.Total, payables, order.RequestedBy, order.Id, ct);
        if (resolution.IsFailure)
        {
            return resolution.Error!;
        }

        db.PaymentOrderLevels.RemoveRange(await db.PaymentOrderLevels.Where(l => l.PaymentOrderId == order.Id).ToListAsync(ct));
        foreach (var level in resolution.Value.Levels)
        {
            db.PaymentOrderLevels.Add(new PaymentOrderLevel
            {
                PaymentOrderId = order.Id, Level = level.Level, ApprovalRuleId = level.Rule.Id, ReferenceAmount = resolution.Value.ReferenceAmount, SplitWarning = resolution.Value.SplitWarning,
            });
        }

        await NotifyLevelAsync(order, resolution.Value.Levels[0].Level, resolution.Value.Levels[0].Eligible, ct);
        return Result.Success();
    }

    /// <summary>Sino e e-mail (CA 16) aos aprovadores do nível: o e-mail leva só o número, o total e o link.</summary>
    private async Task NotifyLevelAsync(PaymentOrder order, int level, IReadOnlyList<Guid> eligible, CancellationToken ct)
    {
        events.Publish(new PaymentOrderLevelReleased(order.Id, level));
        foreach (var user in eligible)
        {
            notifications.Notify(user, NotificationType, $"OP {order.Number} de {ReceivableService.Money(order.Total)} aguarda sua aprovação" + (level > 1 ? $" (nível {level})" : string.Empty), Link(order) + "?acao=aprovar");
            if (await users.EmailOfAsync(user, ct) is { } to)
            {
                email.Enqueue("payment-order-pending", to, new Dictionary<string, object?>
                {
                    ["number"] = order.Number, ["total"] = ReceivableService.Money(order.Total), ["link"] = links.Absolute(Link(order)),
                }, "PaymentOrder", order.Id);
            }
        }
    }

    private Task<PaymentOrderLevel?> CurrentLevelAsync(Guid orderId, CancellationToken ct) =>
        db.PaymentOrderLevels.Where(l => l.PaymentOrderId == orderId && l.Status == LevelStatus.Pending).OrderBy(l => l.Level).FirstOrDefaultAsync(ct);

    /// <summary>Níveis da OP para a aba Decisões (CA 19): regra, aprovador, valor de referência, aviso e situação.</summary>
    private async Task<List<ApprovalLevelView>> LevelViewsAsync(Guid orderId, CancellationToken ct)
    {
        var levels = await db.PaymentOrderLevels.AsNoTracking().Where(l => l.PaymentOrderId == orderId).OrderBy(l => l.Level).ToListAsync(ct);
        var ruleIds = levels.Select(l => l.ApprovalRuleId).ToList();
        var rules = await db.ApprovalRules.IgnoreQueryFilters().AsNoTracking().Where(r => ruleIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var list = new List<ApprovalLevelView>();
        foreach (var l in levels)
        {
            var name = rules.TryGetValue(l.ApprovalRuleId, out var rule) ? await ruleNames.ApproverNameAsync(rule, ct) : "—";
            list.Add(new ApprovalLevelView(l.Level, l.ApprovalRuleId, name, l.ReferenceAmount, l.SplitWarning, l.Status.ToString()));
        }

        return list;
    }

    /// <summary>
    /// OPs pendentes cujo nível atual está na alçada do usuário (CA 15, "Aguardando você"; também a Minha pauta, DSH-04). Mesma regra do
    /// <see cref="ApprovalResolver.EligibleAsync"/>, em lote: níveis e aprovações numa consulta cada, a regra resolvida uma vez por
    /// nível, regra e solicitante (DSH-04 CA 12, sem N+1 por OP).
    /// </summary>
    internal async Task<List<Guid>> AwaitingMeAsync(Guid me, CancellationToken ct)
    {
        var pending = await db.PaymentOrders.AsNoTracking().Where(o => o.Status == PaymentOrderStatus.Pending)
            .Select(o => new { o.Id, o.RequestedBy, o.SubmittedAt }).ToListAsync(ct);
        if (pending.Count == 0)
        {
            return [];
        }

        var ids = pending.Select(o => o.Id).ToList();
        var levels = (await db.PaymentOrderLevels.AsNoTracking().Where(l => ids.Contains(l.PaymentOrderId) && l.Status == LevelStatus.Pending).ToListAsync(ct))
            .GroupBy(l => l.PaymentOrderId).ToDictionary(g => g.Key, g => g.OrderBy(l => l.Level).First());
        var approvals = (await db.PaymentOrderApprovals.AsNoTracking().Where(a => ids.Contains(a.PaymentOrderId) && a.Decision == PaymentOrderDecision.Approved && a.DecidedBy == me)
            .Select(a => new { a.PaymentOrderId, a.Level, a.DecidedAt }).ToListAsync(ct)).ToLookup(a => a.PaymentOrderId);
        var byRule = new Dictionary<(int Level, Guid Rule, Guid? Requester), IReadOnlyList<Guid>>();
        var result = new List<Guid>();
        foreach (var o in pending)
        {
            if (!levels.TryGetValue(o.Id, out var level))
            {
                if (o.RequestedBy != me)
                {
                    result.Add(o.Id);
                }

                continue;
            }

            var key = (level.Level, level.ApprovalRuleId, o.RequestedBy);
            if (!byRule.TryGetValue(key, out var candidates))
            {
                byRule[key] = candidates = await resolver.EligibleByRuleAsync(ApprovalDocumentTypes.PaymentOrder, level.Level, level.ApprovalRuleId, o.RequestedBy, ct);
            }

            // Quem aprovou outro nível desde o envio não decide este (CA 13).
            if (candidates.Contains(me) && !approvals[o.Id].Any(a => a.Level != level.Level && a.DecidedAt >= o.SubmittedAt))
            {
                result.Add(o.Id);
            }
        }

        return result;
    }
}
