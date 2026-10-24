namespace BemAqui.Inventory.Domain;

/// <summary>Saldo de um lote no local, para a sugestão (CA 1): validade e entrada dão a ordem.</summary>
public sealed record LotStock(Guid LotId, string LotNumber, DateOnly? Expiry, DateTimeOffset? ReceivedAt, decimal OnHand);

/// <summary>Uma parte da linha num lote (CA 1): a primeira parte sugerida leva a tag "Sugerido".</summary>
public sealed record LotPick(Guid LotId, string LotNumber, DateOnly? Expiry, decimal Quantity, decimal OnHand, bool Suggested);

/// <summary>Uma linha da requisição para a regra do consumo acima da BOM (CA 3 e 4): reserva (nula no material extra), item e quantidade.</summary>
public sealed record RequisitionDemand(Guid? ReservationId, Guid ItemId, decimal Quantity);

/// <summary>Parte acima da reserva de cada linha, na ordem das linhas (CA 3): a reserva é consumida pela primeira linha que a usa.</summary>
public sealed record RequisitionExcess(int Index, decimal FromReservation, decimal Over);

/// <summary>
/// Regras da requisição de material (EST-03, pontos 55, 56 e 58), sem banco.
/// </summary>
public static class RequisitionRules
{
    public const string DocType = "REQ";
    public const int MaxReason = 200;
    public const int MaxComment = 500;
    public const int MaxLines = 100;

    public static readonly TimeSpan AuthorReversalWindow = ManualPostingRules.AuthorReversalWindow;

    /// <summary>
    /// Ordem da sugestão de lote (ponto 56): validade mais próxima primeiro (sem validade por último); empatado ou sem validade, o mais
    /// antigo pela entrada; por fim o número do lote (estável). Lote sem saldo fica fora.
    /// </summary>
    public static IReadOnlyList<LotStock> SuggestionOrder(IEnumerable<LotStock> lots)
    {
        ArgumentNullException.ThrowIfNull(lots);
        return lots.Where(l => l.OnHand > 0)
            .OrderBy(l => l.Expiry is null ? 1 : 0).ThenBy(l => l.Expiry)
            .ThenBy(l => l.ReceivedAt is null ? 1 : 0).ThenBy(l => l.ReceivedAt)
            .ThenBy(l => l.LotNumber, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Divide a quantidade pelos lotes na ordem da sugestão (CA 1): o primeiro lote leva a tag "Sugerido"; se ele não basta, a linha se
    /// divide nos seguintes. O que não couber em nenhum lote fica de fora (a tela mostra a falta; o servidor recusa com 409).
    /// </summary>
    public static IReadOnlyList<LotPick> SplitByLots(IEnumerable<LotStock> lots, decimal quantity)
    {
        var picks = new List<LotPick>();
        var left = Round(quantity);
        foreach (var lot in SuggestionOrder(lots))
        {
            if (left <= 0)
            {
                break;
            }

            var take = Math.Min(lot.OnHand, left);
            picks.Add(new LotPick(lot.LotId, lot.LotNumber, lot.Expiry, take, lot.OnHand, picks.Count == 0));
            left -= take;
        }

        return picks;
    }

    /// <summary>
    /// Quanto de cada linha sai da reserva e quanto passa dela (CA 3): as linhas da mesma reserva (divisão em lotes) consomem a reserva
    /// na ordem; o material extra (sem reserva) é todo excedente.
    /// </summary>
    public static IReadOnlyList<RequisitionExcess> Excess(IReadOnlyList<RequisitionDemand> lines, IReadOnlyDictionary<Guid, decimal> reserved)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(reserved);
        var left = reserved.ToDictionary(r => r.Key, r => Math.Max(0m, r.Value));
        var result = new List<RequisitionExcess>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var fromReservation = 0m;
            if (line.ReservationId is { } reservation && left.TryGetValue(reservation, out var available))
            {
                fromReservation = Math.Min(available, line.Quantity);
                left[reservation] = available - fromReservation;
            }

            result.Add(new RequisitionExcess(i, fromReservation, Round(line.Quantity - fromReservation)));
        }

        return result;
    }

    /// <summary>Valor do consumo acima da BOM (CA 4): Σ excedente × custo médio do momento, em 2 casas.</summary>
    public static decimal OverBomValue(IReadOnlyList<RequisitionDemand> lines, IReadOnlyList<RequisitionExcess> excess, IReadOnlyDictionary<Guid, decimal> averageCosts)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(excess);
        ArgumentNullException.ThrowIfNull(averageCosts);
        return excess.Where(e => e.Over > 0).Sum(e => AverageCostCalculator.LineTotal(e.Over, averageCosts.GetValueOrDefault(lines[e.Index].ItemId)));
    }

    /// <summary>Acima do limite (o mesmo <c>inventory.manual_approval_amount</c> da EST-01a), a requisição vai para aprovação (CA 4).</summary>
    public static bool NeedsApproval(decimal overBomValue, decimal limit) => overBomValue > limit;

    /// <summary>"Consumo acima da BOM: {motivo}" (ponto 55): o texto gravado no movimento.</summary>
    public static string OverReasonText(string reason) => "Consumo acima da BOM: " + reason;

    /// <summary>O que ainda pode ser devolvido (ou dado como perda) de uma linha baixada (CA 12): baixado − devolvido − perda, nunca negativo.</summary>
    public static decimal Returnable(decimal issued, decimal returned, decimal lost) => Math.Max(0m, Round(issued - returned - lost));

    /// <summary>O autor estorna a própria requisição com <c>inventory.edit</c> até o limite de valor e em até 24 h (CA 14, regra da EST-01a CA 20).</summary>
    public static bool AuthorMayReverse(decimal value, DateTimeOffset completedAt, DateTimeOffset now, decimal limit) =>
        ManualPostingRules.AuthorMayReverse(value, completedAt, now, limit);

    public static decimal Round(decimal quantity) => Math.Round(quantity, 4, MidpointRounding.AwayFromZero);
}
