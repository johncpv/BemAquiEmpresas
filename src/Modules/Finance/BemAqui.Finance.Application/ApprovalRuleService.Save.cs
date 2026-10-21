using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public sealed partial class ApprovalRuleService
{
    private async Task<Result<ApprovalRuleView>> SaveAsync(Guid? id, ApprovalRuleInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await OwnerOnlyAsync(ct) is { } denied)
        {
            return denied;
        }

        if (input.Level is < 1 or > 3)
        {
            return ReceivableService.Invalid("level", "O nível vai de 1 a 3.");
        }

        if (input.MinAmount < 0 || input.MaxAmount is { } max && max < input.MinAmount)
        {
            return ReceivableService.Invalid("maxAmount", "O teto não pode ser menor que o mínimo.");
        }

        if (input.AccountId is { } accountId)
        {
            if (input.MaxAmount is null)
            {
                return ReceivableService.Invalid("maxAmount", "Regra por conta precisa de teto.");
            }

            var found = await chart.FindAsync([accountId], ct);
            if (found.Count == 0 || found[0].Type != AccountType.Expense || !found[0].AcceptsEntries || !found[0].IsActive)
            {
                return ReceivableService.Invalid("accountId", "Escolha uma conta de despesa analítica e ativa.");
            }
        }

        if ((input.ApproverGroupId is null) == (input.ApproverUserId is null))
        {
            return ReceivableService.Invalid("approverGroupId", "Escolha um grupo ou um usuário aprovador.");
        }

        var approvers = await users.UsersWithPermissionAsync("finance.approve", ct);
        var members = input.ApproverGroupId is { } group ? await users.GroupMembersAsync(group, ct) : [input.ApproverUserId!.Value];
        if (members.Count == 0 || members.Any(m => !approvers.Contains(m)))
        {
            return ReceivableService.Invalid(input.ApproverGroupId is null ? "approverUserId" : "approverGroupId", "O aprovador escolhido não tem a permissão Aprovar em Caixa e pagamentos.");
        }

        var rule = id is { } ruleId ? await db.ApprovalRules.FirstOrDefaultAsync(r => r.Id == ruleId, ct) : new ApprovalRule { CreatedAt = clock.GetUtcNow() };
        if (rule is null)
        {
            return NotFound();
        }

        var candidate = new ApprovalRule { MinAmount = input.MinAmount, MaxAmount = input.MaxAmount, AccountId = input.AccountId, Level = input.Level, ApproverGroupId = input.ApproverGroupId, ApproverUserId = input.ApproverUserId };
        var others = await db.ApprovalRules.AsNoTracking().Where(r => r.IsActive && r.Id != rule.Id && r.DocumentType == ApprovalRule.PaymentOrder).ToListAsync(ct);
        if (others.FirstOrDefault(o => o.Level == candidate.Level && o.AccountId == candidate.AccountId && o.Overlaps(candidate)) is { } clash)
        {
            return ReceivableService.Invalid("minAmount", $"A faixa se sobrepõe à regra {await DescribeAsync(clash, ct)}.");
        }

        if (others.Any(o => o.Level != candidate.Level && o.Overlaps(candidate) && SameApprover(o, candidate)))
        {
            return ReceivableService.Invalid("approverGroupId", "O mesmo aprovador não pode decidir dois níveis da mesma faixa.");
        }

        rule.MinAmount = candidate.MinAmount;
        rule.MaxAmount = candidate.MaxAmount;
        rule.AccountId = candidate.AccountId;
        rule.Level = candidate.Level;
        rule.ApproverGroupId = candidate.ApproverGroupId;
        rule.ApproverUserId = candidate.ApproverUserId;
        if (id is null)
        {
            db.ApprovalRules.Add(rule);
        }

        await NotifyOwnersAsync(ct);
        await db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).First(r => r.Id == rule.Id);
    }

    private static bool SameApprover(ApprovalRule a, ApprovalRule b) =>
        (a.ApproverGroupId is not null && a.ApproverGroupId == b.ApproverGroupId) || (a.ApproverUserId is not null && a.ApproverUserId == b.ApproverUserId);

    private async Task<string> DescribeAsync(ApprovalRule rule, CancellationToken ct) =>
        $"nível {rule.Level}, {ReceivableService.Money(rule.MinAmount)} {(rule.MaxAmount is { } m ? "a " + ReceivableService.Money(m) : "sem teto")}, {await ApproverNameAsync(rule, ct)}";
}
