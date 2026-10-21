using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed partial class AmendmentService
{
    private sealed record Validated(string Description, List<ContractAmendmentMilestone> Milestones, List<ContractAmendmentReduction> Reductions, Dictionary<CostType, decimal> Budget);

    /// <summary>
    /// Revalida no servidor (CA 3, 4): acréscimo com marcos somando 100%; redução só de títulos sem baixa do contrato,
    /// 0 ≤ novo ≤ atual e o total igual à redução; orçado somando a variação (sem permissão de custo, proporcional).
    /// </summary>
    private async Task<Result<Validated>> ValidateAsync(Contract contract, ContractAmendment amendment, AmendmentInput input, CancellationToken ct)
    {
        var description = input.Description?.Trim();
        if (string.IsNullOrEmpty(description) || description.Length > 500)
        {
            return QuoteService.Invalid("description", "Descreva o aditivo, com até 500 caracteres.");
        }

        if (input.ValueDelta == 0 && input.DaysDelta == 0)
        {
            return QuoteService.Invalid("valueDelta", "Informe a variação de valor ou de prazo.");
        }

        if (Math.Abs(input.ValueDelta) > 9_999_999_999_999m || input.DaysDelta is < -3650 or > 3650)
        {
            return QuoteService.Invalid("valueDelta", "Variação fora do limite.");
        }

        var milestones = new List<ContractAmendmentMilestone>();
        var reductions = new List<ContractAmendmentReduction>();
        if (input.ValueDelta > 0)
        {
            var built = BuildMilestones(input.ValueDelta, input.Milestones ?? []);
            if (built.IsFailure)
            {
                return built.Error!;
            }

            milestones = built.Value;
        }
        else if (input.ValueDelta < 0)
        {
            var reducible = (await ReducibleAsync(contract.Id, ct)).ToDictionary(r => r.Id);
            if (reducible.Values.Sum(r => r.Amount) < -input.ValueDelta)
            {
                return QuoteService.Invalid("valueDelta", "A redução é maior que o saldo a receber sem baixa.");
            }

            var asked = input.Reductions ?? [];
            for (var i = 0; i < asked.Count; i++)
            {
                if (!reducible.TryGetValue(asked[i].ReceivableId, out var parcel) || asked[i].NewAmount < 0 || asked[i].NewAmount > parcel.Amount)
                {
                    return QuoteService.Invalid($"reductions[{i}].newAmount", "Escolha um título sem baixa do contrato, com o valor novo entre zero e o atual.");
                }

                reductions.Add(new ContractAmendmentReduction { AmendmentId = amendment.Id, ReceivableId = parcel.Id, NewAmount = Math.Round(asked[i].NewAmount, 2) });
            }

            if (reductions.Sum(r => reducible[r.ReceivableId].Amount - r.NewAmount) != -input.ValueDelta)
            {
                return QuoteService.Invalid("reductions", "A soma das reduções precisa ser igual à variação do valor.");
            }
        }

        var budget = await BudgetDeltasAsync(contract, input, ct);
        return budget.IsFailure ? budget.Error! : new Validated(description, milestones, reductions, budget.Value);
    }

    private static Result<List<ContractAmendmentMilestone>> BuildMilestones(decimal value, IReadOnlyList<AmendmentMilestoneInput> inputs)
    {
        if (inputs.Count == 0)
        {
            return QuoteService.Invalid("milestones", "Defina os marcos de pagamento do acréscimo.");
        }

        for (var i = 0; i < inputs.Count; i++)
        {
            var m = inputs[i];
            if (m.Percent is <= 0 or > 100)
            {
                return QuoteService.Invalid($"milestones[{i}].percent", "O percentual vai de 0 a 100.");
            }

            if (string.IsNullOrWhiteSpace(m.Description) || m.Description.Trim().Length > 120)
            {
                return QuoteService.Invalid($"milestones[{i}].description", "Descreva o marco, com até 120 caracteres.");
            }

            if (m.Trigger == MilestoneTrigger.Date ? m.DueDate is null : m.TriggerEvent is null)
            {
                return QuoteService.Invalid($"milestones[{i}].trigger", "Informe a data ou o evento do marco.");
            }
        }

        if (inputs.Sum(m => m.Percent) != 100m)
        {
            return QuoteService.Invalid("milestones", "Os marcos do acréscimo precisam somar 100%.");
        }

        var amounts = QuotePricing.LargestRemainder(value, inputs.Select(m => value * m.Percent / 100).ToList());
        return inputs.Select((m, i) => new ContractAmendmentMilestone
        {
            Sequence = i + 1, Description = m.Description.Trim(), Percent = m.Percent, Amount = amounts[i], Trigger = m.Trigger,
            TriggerEvent = m.Trigger == MilestoneTrigger.Event ? m.TriggerEvent : null, DueDate = m.Trigger == MilestoneTrigger.Date ? m.DueDate : null,
            DaysAfterEvent = m.Trigger == MilestoneTrigger.Event ? m.DaysAfterEvent ?? 0 : null,
        }).ToList();
    }

    /// <summary>Variação do orçado (CA 1): a informada com custo, ou proporcional ao orçado atual pelo maior resto.</summary>
    private async Task<Result<Dictionary<CostType, decimal>>> BudgetDeltasAsync(Contract contract, AmendmentInput input, CancellationToken ct)
    {
        if (input.ValueDelta == 0)
        {
            return new Dictionary<CostType, decimal>();
        }

        if (input.BudgetDeltas is { Count: > 0 } given && await CanCostAsync(ct))
        {
            return given.Values.Sum() == input.ValueDelta
                ? given.Where(kv => kv.Value != 0).ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 2))
                : QuoteService.Invalid("budgetDeltas", "A soma das naturezas precisa ser igual à variação do valor.");
        }

        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.ContractId == contract.Id, ct);
        var current = project is null ? [] : await BudgetAsync(project.Id, ct);
        var total = current.Values.Sum();
        if (total == 0)
        {
            return new Dictionary<CostType, decimal> { [CostType.Other] = input.ValueDelta };
        }

        var types = current.Keys.ToList();
        var sign = Math.Sign(input.ValueDelta);
        var size = Math.Abs(input.ValueDelta);
        var shares = QuotePricing.LargestRemainder(size, types.Select(t => size * current[t] / total).ToList());
        return types.Select((t, i) => (Type: t, Amount: shares[i] * sign)).Where(x => x.Amount != 0).ToDictionary(x => x.Type, x => x.Amount);
    }
}
