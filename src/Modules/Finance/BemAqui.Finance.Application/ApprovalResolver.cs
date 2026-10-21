using BemAqui.BuildingBlocks.Identity;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public sealed record ResolvedLevel(int Level, ApprovalRule Rule, IReadOnlyList<Guid> Eligible);

public sealed record ApprovalResolution(decimal ReferenceAmount, string? SplitWarning, IReadOnlyList<ResolvedLevel> Levels);

/// <summary>
/// Resolve a alçada da OP no envio (FIN-03b CA 6 a 9): valor de referência contra fracionamento, uma regra por nível e os
/// aprovadores elegíveis de cada nível (com <c>finance.approve</c>, sem o solicitante, salvo a exceção da D6).
/// </summary>
public sealed class ApprovalResolver(IFinanceDb db, IUserDirectory users, IFinanceParameters parameters, TimeProvider clock)
{
    public async Task<Result<ApprovalResolution>> ResolveAsync(decimal total, IReadOnlyCollection<Payable> payables, Guid? requester, Guid? excludeOrderId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payables);
        var since = clock.GetUtcNow().AddDays(-30);
        var suppliers = payables.Select(p => p.PartyId).Distinct().ToList();
        var recent = await db.PaymentOrderItems.AsNoTracking()
            .Join(db.PaymentOrders, i => i.PaymentOrderId, o => o.Id, (i, o) => new { i.PayableId, i.ApprovedTotal, o.Id, o.Status, o.SubmittedAt })
            .Join(db.Payables, x => x.PayableId, p => p.Id, (x, p) => new { x.Id, x.Status, x.SubmittedAt, x.ApprovedTotal, p.PartyId })
            .Where(x => suppliers.Contains(x.PartyId) && x.SubmittedAt >= since && x.Status != PaymentOrderStatus.Rejected && x.Status != PaymentOrderStatus.Cancelled && x.Id != excludeOrderId)
            .ToListAsync(ct);
        var bySupplier = suppliers.Select(s => (Party: s, Sum: recent.Where(r => r.PartyId == s).Sum(r => r.ApprovedTotal) + total, Orders: recent.Where(r => r.PartyId == s).Select(r => r.Id).Distinct().Count() + 1)).ToList();
        var top = bySupplier.OrderByDescending(s => s.Sum).First();
        var largest = payables.Max(p => p.Amount);
        var reference = ApprovalRules.Reference(total, largest, top.Sum);
        var account = payables.Select(p => p.AccountId).Distinct().Count() == 1 ? payables.First().AccountId : (Guid?)null;
        var rules = await db.ApprovalRules.AsNoTracking().Where(r => r.IsActive && r.DocumentType == ApprovalRule.PaymentOrder).ToListAsync(ct);
        var resolved = ApprovalRules.Resolve(rules, reference, account);
        var plain = ApprovalRules.Resolve(rules, Math.Max(total, largest), account);
        string? warning = null;
        if (top.Orders > 1 && !resolved.Select(r => r.Id).SequenceEqual(plain.Select(r => r.Id)))
        {
            warning = $"Possível fracionamento: {top.Orders} ordens para o mesmo fornecedor em 30 dias, {ReceivableService.Money(top.Sum)}.";
        }

        if (resolved.Count == 0 || resolved[0].Level != 1)
        {
            return Error.Validation("approval_rule_missing", "Nenhuma regra de alçada cobre esta ordem. Ajuste as alçadas.");
        }

        var approvers = await users.UsersWithPermissionAsync("finance.approve", ct);
        var owners = await users.OwnerIdsAsync(ct);
        var ownApproval = requester is { } r && owners.Contains(r) && (owners.Count == 1 || (await parameters.LimitsAsync(ct)).OwnerCanApproveOwn);
        var levels = new List<ResolvedLevel>();
        foreach (var rule in resolved)
        {
            var members = rule.ApproverGroupId is { } g ? await users.GroupMembersAsync(g, ct) : rule.ApproverUserId is { } u ? [u] : [];
            var eligible = members.Where(approvers.Contains).Where(m => m != requester || (ownApproval && rule.Level == 1)).ToList();
            if (eligible.Count == 0)
            {
                return Error.Validation("approval_level_without_approver", $"O nível {rule.Level} não tem aprovador elegível. Ajuste as alçadas.");
            }

            if (levels.LastOrDefault() is { Eligible: [var only] } previous && eligible.All(e => e == only))
            {
                return Error.Validation("approval_level_without_approver", $"O nível {rule.Level} não tem aprovador diferente do nível {previous.Level}.");
            }

            levels.Add(new ResolvedLevel(rule.Level, rule, eligible));
        }

        return new ApprovalResolution(reference, warning, levels);
    }

    /// <summary>Quem pode decidir o nível agora (CA 13, 15): está na regra, tem a permissão e não decidiu outro nível.</summary>
    public async Task<IReadOnlyList<Guid>> EligibleAsync(PaymentOrderLevel level, Guid? requester, CancellationToken ct)
    {
        var rule = await db.ApprovalRules.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(r => r.Id == level.ApprovalRuleId, ct);
        if (rule is null)
        {
            return [];
        }

        var approvers = await users.UsersWithPermissionAsync("finance.approve", ct);
        var members = rule.ApproverGroupId is { } g ? await users.GroupMembersAsync(g, ct) : rule.ApproverUserId is { } u ? [u] : [];
        var owners = await users.OwnerIdsAsync(ct);
        var ownApproval = requester is { } r && owners.Contains(r) && (owners.Count == 1 || (await parameters.LimitsAsync(ct)).OwnerCanApproveOwn);
        var submitted = await db.PaymentOrders.AsNoTracking().Where(o => o.Id == level.PaymentOrderId).Select(o => o.SubmittedAt).FirstAsync(ct);
        var decided = await db.PaymentOrderApprovals.AsNoTracking()
            .Where(a => a.PaymentOrderId == level.PaymentOrderId && a.Decision == PaymentOrderDecision.Approved && a.Level != level.Level && a.DecidedAt >= submitted)
            .Select(a => a.DecidedBy).ToListAsync(ct);
        return members.Where(approvers.Contains).Where(m => (m != requester || (ownApproval && level.Level == 1)) && !decided.Contains(m)).ToList();
    }
}
