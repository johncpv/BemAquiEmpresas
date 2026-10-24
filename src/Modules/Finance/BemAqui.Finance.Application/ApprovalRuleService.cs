using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Regras de alçada (FIN-03b CA 1 a 5; CMP-03a, ADR 0024 §4): por tipo de documento (ordem de pagamento ou pedido de compra); só membros
/// do Proprietário editam; o aprovador precisa ter a permissão do tipo (<c>finance.approve</c> ou <c>purchasing.approve</c>); faixas do
/// mesmo tipo, nível e conta não se sobrepõem; o mesmo aprovador não decide dois níveis da mesma faixa.
/// </summary>
public sealed partial class ApprovalRuleService(IFinanceDb db, IChartOfAccounts chart, IUserDirectory users, ICurrentUser currentUser, INotificationService notifications, TimeProvider clock)
{
    /// <summary>Regras ativas do tipo (sem tipo, a ordem de pagamento, como na FIN-03b).</summary>
    public async Task<IReadOnlyList<ApprovalRuleView>> ListAsync(string? documentType = null, CancellationToken cancellationToken = default)
    {
        var type = TypeOf(documentType);
        var rules = await db.ApprovalRules.AsNoTracking().Where(r => r.IsActive && r.DocumentType == type).OrderBy(r => r.Level).ThenBy(r => r.AccountId != null).ThenBy(r => r.MinAmount)
            .ToListAsync(cancellationToken);
        var accounts = (await chart.FindAsync(rules.Where(r => r.AccountId != null).Select(r => r.AccountId!.Value).Distinct().ToList(), cancellationToken)).ToDictionary(a => a.Id, a => $"{a.Code} {a.Name}");
        var list = new List<ApprovalRuleView>();
        foreach (var r in rules)
        {
            list.Add(new ApprovalRuleView(r.Id, r.Level, r.MinAmount, r.MaxAmount, r.AccountId, r.AccountId is { } a ? accounts.GetValueOrDefault(a) : null, r.ApproverGroupId, r.ApproverUserId,
                await ApproverNameAsync(r, cancellationToken), r.SeedKey is ApprovalRule.DefaultSeedKey or ApprovalRule.PurchaseOrderSeedKey, r.DocumentType));
        }

        return list;
    }

    /// <summary>Tipo de documento conhecido ou a ordem de pagamento (padrão da FIN-03b).</summary>
    public static string TypeOf(string? documentType) => ApprovalDocumentTypes.IsKnown(documentType) ? documentType! : ApprovalDocumentTypes.PaymentOrder;

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

    /// <summary>Grupos e usuários que podem aprovar o tipo (têm <c>finance.approve</c> ou <c>purchasing.approve</c>), para o seletor da regra.</summary>
    public async Task<IReadOnlyList<ApproverOption>> ApproversAsync(string? documentType = null, CancellationToken cancellationToken = default)
    {
        var approvers = await users.UsersWithPermissionAsync(ApprovalDocumentTypes.ApproverPermission(TypeOf(documentType)), cancellationToken);
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

    /// <summary>
    /// Regras padrão (FIN-03b CA 1; CMP-03a CA 5, ponto 44): "Proprietário aprova tudo", uma própria para cada tipo de documento;
    /// idempotente e não volta se o tenant a excluiu.
    /// </summary>
    public async Task SeedDefaultAsync(CancellationToken cancellationToken = default)
    {
        if (await users.OwnerGroupIdAsync(cancellationToken) is not { } owner)
        {
            return;
        }

        var seeded = await db.ApprovalRules.IgnoreQueryFilters().Where(r => r.SeedKey != null).Select(r => r.SeedKey!).ToListAsync(cancellationToken);
        foreach (var type in ApprovalDocumentTypes.All)
        {
            var key = ApprovalRule.SeedKeyOf(type);
            if (!seeded.Contains(key))
            {
                db.ApprovalRules.Add(new ApprovalRule { DocumentType = type, MinAmount = 0, ApproverGroupId = owner, Level = 1, SeedKey = key, CreatedAt = clock.GetUtcNow() });
            }
        }

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
