using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Application;

/// <summary>Simulador das alçadas (FIN-03b telas): "quem aprovaria uma OP de R$ X na conta Y para o fornecedor Z?".</summary>
public sealed class ApprovalSimulator(ApprovalResolver resolver, ApprovalRuleService rules)
{
    public async Task<Result<ApprovalSimulation>> SimulateAsync(decimal amount, Guid? accountId, Guid? partyId, CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            return ReceivableService.Invalid("amount", "Informe um valor maior que zero.");
        }

        var probe = new Payable { PartyId = partyId ?? Guid.Empty, AccountId = accountId ?? Guid.Empty, Amount = amount };
        var resolved = await resolver.ResolveAsync(amount, [probe], null, null, cancellationToken);
        if (resolved.IsFailure)
        {
            return resolved.Error!;
        }

        var levels = new List<ApprovalLevelView>();
        foreach (var l in resolved.Value.Levels)
        {
            levels.Add(new ApprovalLevelView(l.Level, l.Rule.Id, await rules.ApproverNameAsync(l.Rule, cancellationToken), resolved.Value.ReferenceAmount, resolved.Value.SplitWarning, "Simulado"));
        }

        return new ApprovalSimulation(resolved.Value.ReferenceAmount, resolved.Value.SplitWarning, levels);
    }
}
