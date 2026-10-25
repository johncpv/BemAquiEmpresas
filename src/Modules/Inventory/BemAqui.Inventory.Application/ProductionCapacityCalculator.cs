using BemAqui.SharedKernel;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Entrada de uma linha do cálculo, já na unidade de estoque: necessidade por unidade do produto (null = "não calculado", CA 5),
/// disponível de hoje (físico − reservado, todos os locais), saldo a receber dos pedidos em aberto (null = linha informativa oculta) e
/// custo médio (null = sem <c>projects.view_cost</c>).
/// </summary>
public sealed record CapacityInput(decimal? PerUnit, decimal Available, decimal? OpenQuantity = null, decimal? AverageCost = null);

/// <summary>Resultado de uma linha: unidades possíveis, necessidade e falta para a quantidade, possíveis com os pedidos e o valor da falta.</summary>
public sealed record CapacityLineResult(decimal? Possible, decimal? Required, decimal? Shortage, decimal? PossibleWithOpen, decimal? ShortageCost, bool Limiting);

/// <summary>Resultado do cálculo: o total possível (menor dos itens calculados), o índice do primeiro item que limita e os totais.</summary>
public sealed record CapacityResult(
    IReadOnlyList<CapacityLineResult> Lines, decimal? Possible, int? LimitingIndex, decimal? PossibleWithOpen, int ShortageCount, int NotCalculatedCount, decimal? ShortageCost);

/// <summary>
/// Capacidade de produzir sem banco (EST-06 CA 1 a 3 e 5): possível por item = <c>floor(disponível ÷ necessidade)</c>; total possível =
/// o menor dos itens calculados; falta = <c>max(necessidade × quantidade − disponível, 0)</c>; "Com os pedidos em aberto" soma o saldo a
/// receber ao disponível só nessa linha informativa (ponto 92: o total e as faltas continuam só com o disponível de hoje). Quantidades
/// em 4 casas; valor da falta em 2 casas (<c>AwayFromZero</c>), só para exibição.
/// </summary>
public static class ProductionCapacityCalculator
{
    public const decimal MaxQuantity = 99_999m;

    /// <summary>Necessidade por unidade do produto na unidade de estoque: qtd × (1 + perda %) × fator, arredondada uma vez em 4 casas.</summary>
    public static decimal? PerUnit(decimal quantityPerUnit, decimal scrapPct, decimal? toStockFactor) =>
        toStockFactor is { } factor && quantityPerUnit > 0 && factor > 0
            ? Round(quantityPerUnit * (1m + (scrapPct / 100m)) * factor)
            : null;

    public static CapacityResult Calculate(IReadOnlyList<CapacityInput> lines, decimal quantity)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var results = new List<CapacityLineResult>(lines.Count);
        foreach (var line in lines)
        {
            if (line.PerUnit is not { } perUnit || perUnit <= 0)
            {
                results.Add(new CapacityLineResult(null, null, null, null, null, false));
                continue;
            }

            var available = Math.Max(0m, line.Available);
            var required = Round(perUnit * quantity);
            var shortage = Math.Max(0m, required - available);
            decimal? withOpen = line.OpenQuantity is { } open ? Math.Floor((available + Math.Max(0m, open)) / perUnit) : null;
            decimal? lineCost = line.AverageCost is { } unitCost ? Math.Round(shortage * unitCost, 2, MidpointRounding.AwayFromZero) : null;
            results.Add(new CapacityLineResult(Math.Floor(available / perUnit), required, shortage, withOpen, lineCost, false));
        }

        var calculated = results.Where(r => r.Possible is not null).ToList();
        decimal? possible = calculated.Count == 0 ? null : calculated.Min(r => r.Possible!.Value);
        int? limiting = possible is { } min ? results.FindIndex(r => r.Possible == min) : null;
        var final = results.Select(r => possible is not null && r.Possible == possible ? r with { Limiting = true } : r).ToList();
        decimal? possibleWithOpen = calculated.Count > 0 && calculated.All(r => r.PossibleWithOpen is not null) ? calculated.Min(r => r.PossibleWithOpen!.Value) : null;
        decimal? cost = calculated.Count > 0 && calculated.All(r => r.ShortageCost is not null) ? calculated.Sum(r => r.ShortageCost!.Value) : null;
        return new CapacityResult(final, possible, limiting, possibleWithOpen, calculated.Count(r => r.Shortage > 0), results.Count - calculated.Count, cost);
    }

    /// <summary>Quantidade desejada (CA 2): maior que zero, até 99.999, com até 4 casas.</summary>
    public static Error? ValidateQuantity(decimal quantity) =>
        quantity <= 0 || quantity > MaxQuantity || decimal.Round(quantity, 4) != quantity
            ? Error.Validation("validation", "Informe a quantidade desejada, maior que zero e até 99.999, com até 4 casas.",
                new Dictionary<string, string[]> { ["quantity"] = ["Informe a quantidade desejada, maior que zero e até 99.999, com até 4 casas."] })
            : null;

    public static decimal Round(decimal quantity) => Math.Round(quantity, 4, MidpointRounding.AwayFromZero);
}
