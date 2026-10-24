using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// <see cref="IApprovalRouting"/> (ADR 0024 §4) sobre o núcleo do <see cref="ApprovalResolver"/> da FIN-03b: nenhuma regra de alçada é
/// repetida aqui. O valor de referência vem do chamador; os níveis e as decisões ficam no módulo do documento.
/// </summary>
public sealed class ApprovalRoutingContract(ApprovalResolver resolver, ApprovalRuleService rules, IFinanceDb db) : IApprovalRouting
{
    public async Task<Result<ApprovalRouting>> ResolveAsync(
        string documentType, decimal referenceAmount, decimal documentAmount, Guid? accountId, Guid? requesterId, CancellationToken cancellationToken = default)
    {
        if (!ApprovalDocumentTypes.IsKnown(documentType))
        {
            return Error.Validation(ApprovalResolver.NotViableCode, "Tipo de documento sem alçada.");
        }

        var resolved = await resolver.ResolveRulesAsync(documentType, Math.Max(referenceAmount, documentAmount), documentAmount, accountId, requesterId, cancellationToken);
        if (resolved.IsFailure)
        {
            return resolved.Error!;
        }

        var levels = new List<ApprovalRoutingLevel>();
        foreach (var level in resolved.Value.Levels)
        {
            levels.Add(new ApprovalRoutingLevel(level.Level, level.Rule.Id, await rules.ApproverNameAsync(level.Rule, cancellationToken), level.Eligible));
        }

        return new ApprovalRouting(Math.Max(referenceAmount, documentAmount), resolved.Value.SplitDetected, levels);
    }

    public Task<IReadOnlyList<Guid>> EligibleAsync(string documentType, int level, Guid ruleId, Guid? requesterId, CancellationToken cancellationToken = default) =>
        ApprovalDocumentTypes.IsKnown(documentType)
            ? resolver.EligibleByRuleAsync(documentType, level, ruleId, requesterId, cancellationToken)
            : Task.FromResult<IReadOnlyList<Guid>>([]);

    public async Task<string> ApproverNameAsync(Guid ruleId, CancellationToken cancellationToken = default)
    {
        var rule = await db.ApprovalRules.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(r => r.Id == ruleId, cancellationToken);
        return rule is null ? "—" : await rules.ApproverNameAsync(rule, cancellationToken);
    }
}
