using System.Text.Json.Serialization;

namespace BemAqui.SharedKernel;

/// <summary>Gatilho do marco de pagamento (ADR 0019 §14): data fixa ou evento do projeto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MilestoneTrigger>))]
public enum MilestoneTrigger
{
    Date,
    Event,
}

/// <summary>Eventos que disparam um marco (COM-01b CA 2); o Finance usa o mesmo vocabulário na COM-04b.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TriggerEvent>))]
public enum TriggerEvent
{
    Signature,
    FabricationDone,
    Delivered,
    TechnicalAcceptance,
}

/// <summary>
/// Divisão de um valor por percentuais (mesma regra do <c>PaymentTermSplitter</c> da CAD-05b, ADR 0019 §9): cada parte é
/// <c>round(valor × % / 100, 2, AwayFromZero)</c> e a última absorve a diferença de centavos.
/// </summary>
public static class PercentSplit
{
    public static IReadOnlyList<decimal> Amounts(decimal amount, IReadOnlyList<decimal> percents)
    {
        ArgumentNullException.ThrowIfNull(percents);
        var result = new List<decimal>(percents.Count);
        var allocated = 0m;
        for (var i = 0; i < percents.Count; i++)
        {
            // A última parte só absorve os centavos quando os percentuais fecham 100%; senão, cada uma pelo seu %.
            var closes = percents.Sum() == 100m;
            var value = i == percents.Count - 1 && closes ? amount - allocated : Math.Round(amount * percents[i] / 100m, 2, MidpointRounding.AwayFromZero);
            allocated += value;
            result.Add(value);
        }

        return result;
    }

    /// <summary>"Dividir igualmente": percentuais iguais com 4 casas; o último absorve a diferença (33,3333 + 33,3333 + 33,3334).</summary>
    public static IReadOnlyList<decimal> EqualPercents(int count)
    {
        if (count <= 0)
        {
            return [];
        }

        var each = Math.Round(100m / count, 4, MidpointRounding.ToZero);
        return [.. Enumerable.Repeat(each, count - 1), 100m - (each * (count - 1))];
    }
}
