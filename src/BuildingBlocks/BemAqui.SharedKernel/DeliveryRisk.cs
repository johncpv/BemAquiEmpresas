using System.Text.Json.Serialization;

namespace BemAqui.SharedKernel;

/// <summary>Prazo de entrega diante da data prometida ao cliente (PRD-02 CA 4; PLN-01b CA 9).</summary>
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
/// Função única de atraso e risco (PRD-02 CA 4, substituída pela PLN-01b CA 9): o Kanban, a lista e o detalhe da OS, o job "OS atrasada",
/// o Projeto 360 e a lista de projetos usam só ela. <b>Atrasado</b> = prometido antes de hoje (America/Sao_Paulo) e a entrega não
/// terminou; <b>em risco</b> = data prevista depois da prometida, <b>sem tolerância</b> (ponto 34 da revisão pré-Fases 4 a 8). A regra
/// provisória da Fase 3 ("prometido em até 7 dias e fora da última etapa", item 62) saiu.
/// </summary>
public static class DeliveryRiskRules
{
    public static DeliveryAssessment Evaluate(DateOnly? promised, DateOnly? forecast, DateOnly today, bool finished)
    {
        if (finished || promised is not { } date)
        {
            return DeliveryAssessment.None;
        }

        if (date < today)
        {
            return new DeliveryAssessment(DeliveryRisk.Late, today.DayNumber - date.DayNumber);
        }

        return forecast is { } expected && expected > date ? new DeliveryAssessment(DeliveryRisk.AtRisk, null) : DeliveryAssessment.None;
    }

    /// <summary>Folga = prometido − previsto, em dias corridos: positiva antes do prometido, negativa depois; null sem uma das datas.</summary>
    public static int? SlackDays(DateOnly? promised, DateOnly? forecast) =>
        promised is { } p && forecast is { } f ? p.DayNumber - f.DayNumber : null;
}
