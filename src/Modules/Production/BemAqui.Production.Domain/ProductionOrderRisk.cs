using System.Text.Json.Serialization;

namespace BemAqui.Production.Domain;

/// <summary>Prazo da OS diante da data prometida (PRD-02 CA 4).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DeliveryRisk>))]
public enum DeliveryRisk
{
    None,
    AtRisk,
    Late,
}

/// <summary>Resultado da avaliação: a situação e, se atrasada, quantos dias.</summary>
public readonly record struct DeliveryAssessment(DeliveryRisk Risk, int? DelayDays)
{
    public static DeliveryAssessment None { get; } = new(DeliveryRisk.None, null);
}

/// <summary>
/// Regra única de atraso e risco da OS de fabricação (PRD-02 CA 4, ponto 62 da revisão pré-Fase 3): o Kanban, a lista da PRD-01
/// e o job "OS atrasada" usam só esta função. Atrasada = prometido antes de hoje (America/Sao_Paulo) e OS não concluída; em
/// risco = prometido entre hoje e hoje + 7 dias e a OS fora da última etapa. Até a Fase 4 (PLN-01) a data é o prometido do
/// projeto; depois muda só a fonte da data.
/// </summary>
public static class ProductionOrderRisk
{
    public const int RiskWindowDays = 7;

    public static DeliveryAssessment Evaluate(DateOnly? promised, DateOnly today, ProductionOrderStatus status, bool atLastStep)
    {
        if (promised is not { } date || status is ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled)
        {
            return DeliveryAssessment.None;
        }

        if (date < today)
        {
            return new DeliveryAssessment(DeliveryRisk.Late, today.DayNumber - date.DayNumber);
        }

        return date <= today.AddDays(RiskWindowDays) && !atLastStep ? new DeliveryAssessment(DeliveryRisk.AtRisk, null) : DeliveryAssessment.None;
    }

    /// <summary>A OS chegou à última etapa: todas as anteriores concluídas (inclusive "Pronta para concluir"). Sem etapas, não.</summary>
    public static bool IsAtLastStep(IReadOnlyList<ProductionStepStatus?> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < steps.Count - 1; i++)
        {
            if (steps[i] != ProductionStepStatus.Done)
            {
                return false;
            }
        }

        return true;
    }
}
