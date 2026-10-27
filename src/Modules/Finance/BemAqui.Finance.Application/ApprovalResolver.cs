using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public sealed record ResolvedLevel(int Level, ApprovalRule Rule, IReadOnlyList<Guid> Eligible);

public sealed record ApprovalResolution(decimal ReferenceAmount, string? SplitWarning, IReadOnlyList<ResolvedLevel> Levels);

/// <summary>Resultado do núcleo da resolução: os níveis e se o valor de referência trocou a regra (fracionamento).</summary>
public sealed record RuleResolution(IReadOnlyList<ResolvedLevel> Levels, bool SplitDetected);

/// <summary>
/// Resolve a alçada no envio (FIN-03b CA 6 a 9; ADR 0024 §4): uma regra por nível do tipo de documento e os aprovadores elegíveis de
/// cada nível (com a permissão do tipo, <see cref="ApprovalDocumentTypes.ApproverPermission"/>, sem o solicitante salvo a exceção do
/// tipo). Na OP, a exceção é a da D6 (Proprietário único ou o parâmetro); no pedido de compra, a do <see cref="SoleOwnerRule"/>. O
/// <see cref="ApprovalRoutingContract"/> usa o mesmo núcleo (<see cref="ResolveRulesAsync"/>).
/// </summary>
public sealed class ApprovalResolver(IFinanceDb db, IUserDirectory users, IFinanceParameters parameters, TimeProvider clock)
{
    public const string NotViableCode = "approval_not_viable";

    public async Task<Result<ApprovalResolution>> ResolveAsync(decimal total, IReadOnlyCollection<Payable> payables, Guid? requester, Guid? excludeOrderId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payables);
        var since = clock.GetUtcNow().AddDays(-30);
        var suppliers = payables.Select(p => p.PayeeId).Distinct().ToList();
        var recent = await db.PaymentOrderItems.AsNoTracking()
            .Join(db.PaymentOrders, i => i.PaymentOrderId, o => o.Id, (i, o) => new { i.PayableId, i.ApprovedTotal, o.Id, o.Status, o.SubmittedAt })
            .Join(db.Payables, x => x.PayableId, p => p.Id, (x, p) => new { x.Id, x.Status, x.SubmittedAt, x.ApprovedTotal, PartyId = p.PayeeId })
            .Where(x => suppliers.Contains(x.PartyId) && x.SubmittedAt >= since && x.Status != PaymentOrderStatus.Rejected && x.Status != PaymentOrderStatus.Cancelled && x.Id != excludeOrderId)
            .ToListAsync(ct);
        var bySupplier = suppliers.Select(s => (Party: s, Sum: recent.Where(r => r.PartyId == s).Sum(r => r.ApprovedTotal) + total, Orders: recent.Where(r => r.PartyId == s).Select(r => r.Id).Distinct().Count() + 1)).ToList();
        var top = bySupplier.OrderByDescending(s => s.Sum).First();
        var largest = payables.Max(p => p.Amount);
        var reference = ApprovalRules.Reference(total, largest, top.Sum);
        var account = payables.Select(p => p.AccountId).Distinct().Count() == 1 ? payables.First().AccountId : (Guid?)null;
        var resolved = await ResolveRulesAsync(ApprovalDocumentTypes.PaymentOrder, reference, Math.Max(total, largest), account, requester, ct);
        if (resolved.IsFailure)
        {
            return resolved.Error!;
        }

        var warning = top.Orders > 1 && resolved.Value.SplitDetected
            ? $"Possível fracionamento: {top.Orders} ordens para o mesmo fornecedor em 30 dias, {ReceivableService.Money(top.Sum)}."
            : null;
        return new ApprovalResolution(reference, warning, resolved.Value.Levels);
    }

    /// <summary>
    /// Núcleo da resolução (FIN-03b CA 6 a 9, ADR 0019 §6): as regras ativas do tipo pelo valor de referência (conta vence faixa), um
    /// aprovador elegível por nível e níveis com aprovadores diferentes. Na OP os códigos de erro são os da FIN-03b; nos demais tipos,
    /// <c>approval_not_viable</c> com o motivo (CMP-03a CA 7).
    /// </summary>
    public async Task<Result<RuleResolution>> ResolveRulesAsync(string documentType, decimal reference, decimal documentAmount, Guid? account, Guid? requester, CancellationToken ct)
    {
        var isPaymentOrder = documentType == ApprovalDocumentTypes.PaymentOrder;
        var noun = isPaymentOrder ? "esta ordem" : "este pedido";
        var rules = await db.ApprovalRules.AsNoTracking().Where(r => r.IsActive && r.DocumentType == documentType).ToListAsync(ct);
        var resolved = ApprovalRules.Resolve(rules, reference, account);
        var plain = ApprovalRules.Resolve(rules, documentAmount, account);
        var split = reference > documentAmount && !resolved.Select(r => r.Id).SequenceEqual(plain.Select(r => r.Id));
        if (resolved.Count == 0 || resolved[0].Level != 1)
        {
            return Error.Validation(isPaymentOrder ? "approval_rule_missing" : NotViableCode, $"Nenhuma regra de alçada cobre {noun}. Ajuste as alçadas.");
        }

        var approvers = await users.UsersWithPermissionAsync(ApprovalDocumentTypes.ApproverPermission(documentType), ct);
        var self = await SelfAllowedAsync(documentType, requester, approvers, ct);
        var levels = new List<ResolvedLevel>();
        foreach (var rule in resolved)
        {
            var eligible = (await MembersAsync(rule, ct)).Where(approvers.Contains).Where(m => m != requester || (self && rule.Level == 1)).ToList();
            if (eligible.Count == 0)
            {
                return Error.Validation(isPaymentOrder ? "approval_level_without_approver" : NotViableCode,
                    isPaymentOrder
                        ? $"O nível {rule.Level} não tem aprovador elegível. Ajuste as alçadas."
                        : $"O nível {rule.Level} não tem aprovador elegível: ninguém da regra tem a permissão Aprovar em Compras, além de quem pediu. Ajuste as alçadas de pedido de compra.");
            }

            if (levels.LastOrDefault() is { Eligible: [var only] } previous && eligible.All(e => e == only))
            {
                return Error.Validation(isPaymentOrder ? "approval_level_without_approver" : NotViableCode, $"O nível {rule.Level} não tem aprovador diferente do nível {previous.Level}.");
            }

            levels.Add(new ResolvedLevel(rule.Level, rule, eligible));
        }

        return new RuleResolution(levels, split);
    }

    /// <summary>Quem pode decidir o nível agora (CA 13, 15): está na regra, tem a permissão e não decidiu outro nível.</summary>
    public async Task<IReadOnlyList<Guid>> EligibleAsync(PaymentOrderLevel level, Guid? requester, CancellationToken ct)
    {
        var candidates = await EligibleByRuleAsync(ApprovalDocumentTypes.PaymentOrder, level.Level, level.ApprovalRuleId, requester, ct);
        var submitted = await db.PaymentOrders.AsNoTracking().Where(o => o.Id == level.PaymentOrderId).Select(o => o.SubmittedAt).FirstAsync(ct);
        var decided = await db.PaymentOrderApprovals.AsNoTracking()
            .Where(a => a.PaymentOrderId == level.PaymentOrderId && a.Decision == PaymentOrderDecision.Approved && a.Level != level.Level && a.DecidedAt >= submitted)
            .Select(a => a.DecidedBy).ToListAsync(ct);
        return candidates.Where(m => !decided.Contains(m)).ToList();
    }

    /// <summary>Membros da regra gravada com a permissão do tipo, sem o solicitante (salvo a exceção do tipo, só no nível 1).</summary>
    public async Task<IReadOnlyList<Guid>> EligibleByRuleAsync(string documentType, int level, Guid ruleId, Guid? requester, CancellationToken ct)
    {
        var rule = await db.ApprovalRules.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(r => r.Id == ruleId && r.DocumentType == documentType, ct);
        if (rule is null)
        {
            return [];
        }

        var approvers = await users.UsersWithPermissionAsync(ApprovalDocumentTypes.ApproverPermission(documentType), ct);
        var self = await SelfAllowedAsync(documentType, requester, approvers, ct);
        return (await MembersAsync(rule, ct)).Where(approvers.Contains).Where(m => m != requester || (self && level == 1)).ToList();
    }

    /// <summary>
    /// O solicitante pode decidir o próprio documento? OP (D6): é do Proprietário e o Proprietário tem um membro só ou o parâmetro
    /// <c>finance.owner_can_approve_own</c> está ligado. Demais tipos (ADR 0024 §4): só o Proprietário único, pelo <see cref="SoleOwnerRule"/>.
    /// </summary>
    private async Task<bool> SelfAllowedAsync(string documentType, Guid? requester, IReadOnlyList<Guid> approvers, CancellationToken ct)
    {
        if (requester is not { } r)
        {
            return false;
        }

        var owners = await users.OwnerIdsAsync(ct);
        if (documentType == ApprovalDocumentTypes.PaymentOrder)
        {
            return owners.Contains(r) && (owners.Count == 1 || (await parameters.LimitsAsync(ct)).OwnerCanApproveOwn);
        }

        return owners.Contains(r) && SoleOwnerRule.NoOtherEligible(approvers, r, [r]);
    }

    private async Task<IReadOnlyList<Guid>> MembersAsync(ApprovalRule rule, CancellationToken ct) =>
        rule.ApproverGroupId is { } g ? await users.GroupMembersAsync(g, ct) : rule.ApproverUserId is { } u ? [u] : [];
}
