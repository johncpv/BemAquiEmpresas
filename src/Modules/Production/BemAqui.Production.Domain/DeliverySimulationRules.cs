namespace BemAqui.Production.Domain;

/// <summary>
/// Posição do projeto virtual na fila (PLN-05 CA 3), pura: o padrão é o fim; "Na posição {n}" vale de 1 até o tamanho da fila + 1 (fora da
/// faixa vai para o fim). Os projetos reais a partir da posição andam uma casa, na mesma ordem.
/// </summary>
public static class DeliverySimulationRules
{
    public static int Position(int? requested, int queueSize) =>
        requested is { } n && n >= 1 && n <= queueSize ? n : queueSize + 1;

    /// <summary>Posição de cada projeto real com o virtual em <paramref name="position"/> (1-based).</summary>
    public static Dictionary<Guid, int> ShiftedPositions(IReadOnlyList<Guid> order, int position)
    {
        ArgumentNullException.ThrowIfNull(order);
        var result = new Dictionary<Guid, int>(order.Count);
        for (var i = 0; i < order.Count; i++)
        {
            result.TryAdd(order[i], i + 1 < position ? i + 1 : i + 2);
        }

        return result;
    }
}
