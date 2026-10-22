using System.Globalization;
using BemAqui.Inventory.Contracts;

namespace BemAqui.Inventory.Domain;

/// <summary>
/// Custo médio ponderado móvel global por item (<c>04-fluxos</c> §3.1, ADR 0023 §3 e §4), sem banco. O custo médio é arredondado em
/// 6 casas <c>AwayFromZero</c> só ao gravar; o total da linha em 2 casas (ADR 0019 §9).
/// </summary>
public static class AverageCostCalculator
{
    public static decimal RoundCost(decimal value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);

    public static decimal LineTotal(decimal quantity, decimal unitCost) => Math.Round(quantity * unitCost, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Entrada com custo: <c>(saldo × médio + qtd × custo) ÷ (saldo + qtd)</c>; com o físico total do item zerado, o médio passa a
    /// ser o custo da entrada (CA 18 e 19).
    /// </summary>
    public static decimal AfterEntry(decimal onHand, decimal average, decimal quantity, decimal unitCost)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Entrada com quantidade positiva.");
        }

        return onHand <= 0 ? unitCost : (onHand * average + quantity * unitCost) / (onHand + quantity);
    }

    /// <summary>
    /// Estorno de entrada (CA 21): sai pelo custo da entrada, <c>(saldo × médio − qtd × custo) ÷ (saldo − qtd)</c>; se o físico
    /// restante zerar ou o resultado for ≤ 0, fica o último médio válido.
    /// </summary>
    public static decimal AfterEntryReversal(decimal onHand, decimal average, decimal quantity, decimal unitCost)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantidade estornada positiva.");
        }

        var remaining = onHand - quantity;
        if (remaining <= 0)
        {
            return average;
        }

        var result = (onHand * average - quantity * unitCost) / remaining;
        return result <= 0 ? average : result;
    }

    /// <summary>
    /// Custo médio depois de um movimento com <paramref name="quantity"/> com sinal, sobre o físico total do item antes dele.
    /// Entrada (menos a perna de entrada da transferência) pondera pelo custo dela; estorno de entrada usa a proteção do CA 21;
    /// saída, transferência e ajuste negativo nunca mudam o médio (CA 19).
    /// </summary>
    public static decimal Next(StockMovementType type, decimal onHand, decimal average, decimal quantity, decimal unitCost, bool reversesEntry)
    {
        if (type == StockMovementType.Transfer)
        {
            return average;
        }

        if (quantity > 0)
        {
            return RoundCost(AfterEntry(onHand, average, quantity, unitCost));
        }

        return reversesEntry ? RoundCost(AfterEntryReversal(onHand, average, -quantity, unitCost)) : average;
    }

    /// <summary>Sentido esperado da quantidade por tipo: entrada positiva, saída negativa; transferência e ajuste nos dois.</summary>
    public static bool SignAllowed(StockMovementType type, decimal quantity) => quantity != 0 && type switch
    {
        StockMovementType.Receipt or StockMovementType.Return or StockMovementType.Production or StockMovementType.OpeningBalance => quantity > 0,
        StockMovementType.Issue or StockMovementType.Shipment => quantity < 0,
        _ => true,
    };
}

/// <summary>Regras da segregação dos lançamentos manuais (D7, ADR 0023 §5), sem banco.</summary>
public static class ManualPostingRules
{
    public const int MaxReason = 200;

    public static readonly TimeSpan AuthorReversalWindow = TimeSpan.FromHours(24);

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Money(decimal value) => "R$ " + value.ToString("N2", PtBr);

    /// <summary>
    /// Por que o lançamento precisa de aprovação (CA 9), ou null: valor acima do limite (entrada e saída) ou custo da entrada que desvia
    /// mais que o percentual do custo médio atual (só quando o item já tem custo médio). Saldo inicial e transferência ficam fora.
    /// </summary>
    public static string? ApprovalReason(ManualPostingKind kind, decimal value, decimal? unitCost, decimal average, decimal limit, decimal deviationPct)
    {
        if (kind is not (ManualPostingKind.Receipt or ManualPostingKind.Issue))
        {
            return null;
        }

        var reasons = new List<string>();
        if (value > limit)
        {
            reasons.Add("Acima de " + Money(limit));
        }

        if (kind == ManualPostingKind.Receipt && unitCost is { } cost && average > 0)
        {
            var deviation = (cost - average) / average * 100m;
            if (Math.Abs(deviation) > deviationPct)
            {
                var pct = Math.Round(Math.Abs(deviation), 1, MidpointRounding.AwayFromZero).ToString("0.#", PtBr);
                reasons.Add($"Custo {pct}% {(deviation > 0 ? "acima" : "abaixo")} do médio");
            }
        }

        return reasons.Count == 0 ? null : string.Join(" · ", reasons);
    }

    /// <summary>O autor estorna o próprio movimento manual com <c>inventory.edit</c> até o limite de valor e em até 24 h (CA 20).</summary>
    public static bool AuthorMayReverse(decimal value, DateTimeOffset postedAt, DateTimeOffset now, decimal limit) =>
        Math.Abs(value) <= limit && now - postedAt <= AuthorReversalWindow;
}
