using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Application;

/// <summary>Marco no modo implantação (COM-04c CA 1 a 3): data no passado (marco por data) e "recebido antes da implantação".</summary>
public sealed record OnboardingMilestoneInput(Guid MilestoneId, DateOnly? DueDate, bool ReceivedBefore);

public sealed record ApprovalOnboarding(DateOnly? CutoverDate, DateTimeOffset? CompletedAt);

public sealed partial class QuoteApprovalService
{
    /// <summary>
    /// Confere o modo implantação: precisa da data de corte; data só no marco por data; "recebido antes" só até a data de
    /// corte (data do marco, ou a assinatura para o marco na assinatura). Devolve o ajuste de cada marco.
    /// </summary>
    private async Task<Result<Dictionary<Guid, OnboardingMilestoneInput>>> OnboardingAsync(ApproveInput input, QuoteConditions conditions, CancellationToken ct)
    {
        if (!input.IsOnboarding)
        {
            return new Dictionary<Guid, OnboardingMilestoneInput>();
        }

        var status = await opening.StatusAsync(ct);
        if (status.CutoverDate is not { } cutover)
        {
            return QuoteService.Invalid("isOnboarding", "Defina a data de corte na Implantação financeira antes.");
        }

        var result = new Dictionary<Guid, OnboardingMilestoneInput>();
        var byId = conditions.Milestones.ToDictionary(m => m.Id);
        var inputs = input.Milestones ?? [];
        for (var i = 0; i < inputs.Count; i++)
        {
            var item = inputs[i];
            if (!byId.TryGetValue(item.MilestoneId, out var milestone))
            {
                return QuoteService.Invalid($"milestones[{i}]", "Marco não pertence a este orçamento.");
            }

            if (item.DueDate is not null && milestone.Trigger != MilestoneTrigger.Date)
            {
                return QuoteService.Invalid($"milestones[{i}].dueDate", "Só o marco por data tem a data editada.");
            }

            var date = milestone.Trigger == MilestoneTrigger.Date ? item.DueDate ?? milestone.DueDate
                : milestone.TriggerEvent == TriggerEvent.Signature ? input.SignedAt.AddDays(milestone.DaysAfterEvent ?? 0) : null;
            if (item.ReceivedBefore && date > cutover)
            {
                return QuoteService.Invalid($"milestones[{i}].receivedBefore", "Recebido depois da data de corte: lance a baixa no título.");
            }

            result[item.MilestoneId] = item;
        }

        return result;
    }
}
