using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Regras de alçada (FIN-03b CA 1 a 5): só membros do Proprietário editam; o aprovador precisa ter <c>finance.approve</c>;
/// faixas do mesmo nível e conta não se sobrepõem; o mesmo aprovador não decide dois níveis da mesma faixa.
/// </summary>
public sealed partial class ApprovalRuleService(IFinanceDb db, IChartOfAccounts chart, IUserDirectory users, ICurrentUser currentUser, INotificationService notifications, TimeProvider clock)
{
    public async Task<IReadOnlyList<ApprovalRuleView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rules = await db.ApprovalRules.AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.Level).ThenBy(r => r.AccountId != null).ThenBy(r => r.MinAmount).ToListAsync(cancellationToken);
        var accounts = (await chart.FindAsync(rules.Where(r => r.AccountId != null).Select(r => r.AccountId!.Value).Distinct().ToList(), cancellationToken)).ToDictionary(a => a.Id, a => $"{a.Code} {a.Name}");
        var list = new List<ApprovalRuleView>();
        foreach (var r in rules)
        {
            list.Add(new ApprovalRuleView(r.Id, r.Level, r.MinAmount, r.MaxAmount, r.AccountId, r.AccountId is { } a ? accounts.GetValueOrDefault(a) : null, r.ApproverGroupId, r.ApproverUserId,
                await ApproverNameAsync(r, cancellationToken), r.SeedKey == ApprovalRule.DefaultSeedKey));
        }

        return list;
    }

    public async Task<Result<ApprovalRuleView>> CreateAsync(ApprovalRuleInput input, CancellationToken cancellationToken = default) =>
        await SaveAsync(null, input, cancellationToken);

    public async Task<Result<ApprovalRuleView>> UpdateAsync(Guid id, ApprovalRuleInput input, CancellationToken cancellationToken = default) =>
        await SaveAsync(id, input, cancellationToken);

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (await OwnerOnlyAsync(cancellationToken) is { } denied)
        {
            return denied;
        }

        var rule = await db.ApprovalRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rule is null)
        {
            return NotFound();
        }

        rule.MarkDeleted(clock.GetUtcNow());
        rule.IsActive = false;
        await NotifyOwnersAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Grupos e usuários que podem aprovar (têm <c>finance.approve</c>), para o seletor da regra.</summary>
    public async Task<IReadOnlyList<ApproverOption>> ApproversAsync(CancellationToken cancellationToken = default)
    {
        var approvers = await users.UsersWithPermissionAsync("finance.approve", cancellationToken);
        var options = new List<ApproverOption>();
        foreach (var u in approvers)
        {
            options.Add(new ApproverOption(u, await users.DisplayNameAsync(u, cancellationToken) ?? "Usuário", false));
        }

        if (await users.OwnerGroupIdAsync(cancellationToken) is { } owner)
        {
            options.Insert(0, new ApproverOption(owner, await users.GroupNameAsync(owner, cancellationToken) ?? "Proprietário", true));
        }

        return options;
    }

    /// <summary>Regra padrão (CA 1): "Proprietário aprova tudo"; não volta se o tenant a excluiu.</summary>
    public async Task SeedDefaultAsync(CancellationToken cancellationToken = default)
    {
        if (await db.ApprovalRules.IgnoreQueryFilters().AnyAsync(r => r.SeedKey == ApprovalRule.DefaultSeedKey, cancellationToken) || await users.OwnerGroupIdAsync(cancellationToken) is not { } owner)
        {
            return;
        }

        db.ApprovalRules.Add(new ApprovalRule { MinAmount = 0, ApproverGroupId = owner, Level = 1, SeedKey = ApprovalRule.DefaultSeedKey, CreatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken);
    }

    internal async Task<string> ApproverNameAsync(ApprovalRule rule, CancellationToken ct) =>
        rule.ApproverGroupId is { } g ? await users.GroupNameAsync(g, ct) ?? "Grupo" : rule.ApproverUserId is { } u ? await users.DisplayNameAsync(u, ct) ?? "Usuário" : "—";

    private async Task<Error?> OwnerOnlyAsync(CancellationToken ct) =>
        currentUser.UserId is { } me && (await users.OwnerIdsAsync(ct)).Contains(me)
            ? null
            : Error.Forbidden(Error.Codes.Forbidden, "Só o Proprietário altera as regras de alçada.");

    private async Task NotifyOwnersAsync(CancellationToken ct)
    {
        var who = currentUser.UserId is { } me ? await users.DisplayNameAsync(me, ct) ?? "um usuário" : "o sistema";
        foreach (var owner in await users.OwnerIdsAsync(ct))
        {
            notifications.Notify(owner, "finance.approval_rules", $"Regra de alçada alterada por {who}", "/app/financeiro/alcadas");
        }
    }

    private static Error NotFound() => Error.NotFound("approval_rule_not_found", "Regra de alçada não encontrada.");
}
