namespace BemAqui.Commercial.Domain;

/// <summary>
/// Fórmulas do orçamento (COM-01a CA 8, 12, 14 e 15; ADR 0019 §9): arredonda só no total da linha e no preço, com
/// <see cref="MidpointRounding.AwayFromZero"/>. Markup: custo × (1 + m). Margem: custo ÷ (1 − g).
/// </summary>
public static class QuotePricing
{
    public static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal Pct(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    /// <summary>Total da linha de custo: quantidade (ou horas) × custo unitário, 2 casas.</summary>
    public static decimal LineTotal(decimal quantity, decimal unitCost) => Money(quantity * unitCost);

    /// <summary>Preço sugerido; nulo quando a margem é 100% ou mais (não existe preço).</summary>
    public static decimal? SuggestedPrice(decimal unitCost, QuotePricingMode mode, decimal pct) => mode switch
    {
        QuotePricingMode.Markup => Money(unitCost * (1 + (pct / 100))),
        _ when pct >= 100 => null,
        _ => Money(unitCost / (1 - (pct / 100))),
    };

    /// <summary>% do item a partir do preço (preço digitado à mão): markup sobre o custo ou margem sobre o preço.</summary>
    public static decimal? PctOf(decimal unitCost, decimal unitPrice, QuotePricingMode mode) => mode switch
    {
        QuotePricingMode.Markup => unitCost > 0 ? Pct(((unitPrice / unitCost) - 1) * 100) : null,
        _ => unitPrice > 0 ? Pct((unitPrice - unitCost) / unitPrice * 100) : null,
    };

    /// <summary>Markup equivalente a uma margem (rótulo "Margem 40,00% · Markup 66,67%").</summary>
    public static decimal? MarkupFromMargin(decimal margin) => margin >= 100 ? null : Pct(margin / (100 - margin) * 100);

    /// <summary>Margem equivalente a um markup (rótulo "Markup 40,00% · Margem 28,57%").</summary>
    public static decimal MarginFromMarkup(decimal markup) => markup <= -100 ? 0 : Pct(markup / (100 + markup) * 100);

    /// <summary>
    /// Arredonda partes para 2 casas pelo maior resto (ADR 0019 §9): a soma fecha com <paramref name="total"/> (ex.: orçado por natureza
    /// do projeto = custo total do orçamento, COM-04a CA 8).
    /// </summary>
    public static IReadOnlyList<decimal> LargestRemainder(decimal total, IReadOnlyList<decimal> raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var floors = raw.Select(r => Math.Floor(r * 100) / 100).ToList();
        var cents = (int)Math.Round((total - floors.Sum()) * 100);
        var order = raw.Select((r, i) => (Remainder: r * 100 - Math.Floor(r * 100), i)).OrderByDescending(x => x.Remainder).ThenBy(x => x.i).Select(x => x.i).ToList();
        for (var k = 0; k < cents && order.Count > 0; k++)
        {
            floors[order[k % order.Count]] += 0.01m;
        }

        return floors;
    }

    /// <summary>Totais do orçamento (CA 15): Σ por item com cada produto arredondado; margem nula com preço zero.</summary>
    public static (decimal Cost, decimal Price, decimal? Margin) Totals(IEnumerable<(decimal Quantity, decimal UnitCost, decimal UnitPrice)> items)
    {
        var list = items.ToList();
        var cost = list.Sum(i => Money(i.Quantity * i.UnitCost));
        var price = list.Sum(i => Money(i.Quantity * i.UnitPrice));
        return (cost, price, price == 0 ? null : Pct((price - cost) / price * 100));
    }
}
