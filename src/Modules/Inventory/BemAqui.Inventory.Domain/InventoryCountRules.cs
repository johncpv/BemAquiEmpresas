namespace BemAqui.Inventory.Domain;

/// <summary>Resultado de uma linha no fechamento (CA 5 e 9): o contado usado (null = manter o sistema), os movimentos e a diferença.</summary>
public sealed record CountLineResult(decimal? Counted, decimal MovementDelta, decimal Difference);

/// <summary>Regras do inventário (EST-05), sem banco: diferença com os movimentos do período, não contados, recontagem e textos.</summary>
public static class InventoryCountRules
{
    public const string DocType = "INV";
    public const int MaxReason = 200;

    /// <summary>Linhas por inventário (o ajuste gerado aceita o mesmo teto).</summary>
    public const int MaxLines = 2000;

    public const string RecountByOtherMessage = "A recontagem precisa ser feita por outra pessoa.";

    public static decimal Round(decimal quantity) => StockAdjustmentRules.Round(quantity);

    /// <summary>Movimentos entre a foto e a contagem da linha (CA 5): o saldo do sistema na contagem − o da foto.</summary>
    public static decimal MovementDelta(decimal systemQty, decimal systemQtyAtCount) => Round(systemQtyAtCount - systemQty);

    /// <summary>Diferença da linha (CA 5): contado − (saldo da abertura + movimentos do período).</summary>
    public static decimal Difference(decimal counted, decimal systemQty, decimal movementDelta) => Round(counted - (systemQty + movementDelta));

    /// <summary>
    /// Fechamento da linha (CA 5 e 9). Contada: a diferença sobre o saldo do sistema no momento da contagem. Não contada com
    /// <see cref="UncountedPolicy.Zero"/>: contado 0 sobre o saldo de agora (toda a quantidade vira falta). Não contada com
    /// <see cref="UncountedPolicy.KeepSystem"/>: sem diferença.
    /// </summary>
    public static CountLineResult Resolve(decimal systemQty, decimal? counted, decimal? systemQtyAtCount, decimal systemQtyNow, UncountedPolicy policy)
    {
        if (counted is { } value)
        {
            var movement = MovementDelta(systemQty, systemQtyAtCount ?? systemQtyNow);
            return new CountLineResult(value, movement, Difference(value, systemQty, movement));
        }

        var now = MovementDelta(systemQty, systemQtyNow);
        return policy == UncountedPolicy.Zero ? new CountLineResult(0m, now, Difference(0m, systemQty, now)) : new CountLineResult(null, now, 0m);
    }

    /// <summary>Recontagem (CA 8, §2.2): quem contou a linha original não reconta.</summary>
    public static bool MayRecount(Guid? originalCounter, Guid actor) => originalCounter is null || originalCounter != actor;

    /// <summary>"{x} de {y} itens contados".</summary>
    public static string Progress(int counted, int total) => $"{counted} de {total} {(total == 1 ? "item contado" : "itens contados")}";

    /// <summary>Pergunta do fechamento com linhas não contadas (CA 9).</summary>
    public static string UncountedQuestion(int count) => $"Considerar {count} {(count == 1 ? "item não contado" : "itens não contados")} como zero?";

    public static string PolicyLabel(UncountedPolicy policy) => policy == UncountedPolicy.Zero ? "Considerar como zero" : "Manter o saldo do sistema";

    public static string StatusLabel(InventoryCountStatus status) => status switch
    {
        InventoryCountStatus.Counting => "Em contagem",
        InventoryCountStatus.Closed => "Fechado",
        _ => "Cancelado",
    };

    /// <summary>Divergências na ordem do valor absoluto (CA 7), depois pela quantidade e pela sequência (estável).</summary>
    public static IReadOnlyList<T> ByValue<T>(IEnumerable<T> lines, Func<T, decimal> value, Func<T, decimal> quantity, Func<T, int> sequence)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.OrderByDescending(l => Math.Abs(value(l))).ThenByDescending(l => Math.Abs(quantity(l))).ThenBy(sequence).ToList();
    }
}
