using System.Text.Json.Serialization;
using BemAqui.Inventory.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Inventory.Domain;

/// <summary>
/// Reserva de material de uma OS num local (<c>inventory.reservations</c>, EST-02, ADR 0023 §2 e §6): uma linha por OS, item e
/// local, que fica <c>Active</c> enquanto tem necessidade e <c>Released</c> quando o alvo zera (volta a <c>Active</c> se o alvo
/// subir de novo). <c>RequestedQuantity</c> é a necessidade da linha naquele local, <c>Quantity</c> o reservado (espelhado em
/// <c>stock_positions.quantity_reserved</c> na mesma transação), <c>QuantityIssued</c> o já baixado (EST-03); a falta da linha
/// fica no local padrão. Só o <c>ReservationAllocator</c> muda a reserva, sob a trava do item (teste de arquitetura).
/// </summary>
[DocumentName("A reserva")]
public sealed class Reservation : Entity, IAuditable, IConcurrencyVersioned
{
    public Guid ItemId { get; init; }

    public Guid LocationId { get; init; }

    public Guid ProductionOrderId { get; init; }

    public Guid ProjectId { get; set; }

    /// <summary>Linha da BOM vigente que pede o item (a primeira, se o item aparece em mais de uma).</summary>
    public Guid? BomLineId { get; set; }

    public int BomRevision { get; set; }

    /// <summary>Quando a BOM vigente foi liberada: a ordem de atendimento das faltas (ponto 28).</summary>
    public DateTimeOffset? BomReleasedAt { get; set; }

    public decimal RequestedQuantity { get; set; }

    public decimal Quantity { get; set; }

    public decimal QuantityIssued { get; set; }

    public ReservationState Status { get; set; } = ReservationState.Active;

    /// <summary>Última origem da mudança ("Evento BomReleased · mensagem {id}", "Entrada MOV-…", "Recomposição").</summary>
    public string Origin { get; set; } = string.Empty;

    /// <summary>Gravada pela recomposição da implantação (D3): "Calculado na implantação".</summary>
    public bool IsBackfill { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    public uint Version { get; set; }

    /// <summary>Falta da reserva: necessidade − reservado − baixado, nunca negativa.</summary>
    public decimal Shortage => Math.Max(0m, RequestedQuantity - Quantity - QuantityIssued);
}

/// <summary>Tipo da mudança no histórico da reserva (CA 18).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReservationChangeKind>))]
public enum ReservationChangeKind
{
    /// <summary>Reservou mais (reconciliação até o alvo).</summary>
    Reserved,

    /// <summary>Liberou (alvo menor, OS cancelada ou concluída).</summary>
    Released,

    /// <summary>Completou a falta com material que chegou (ponto 29).</summary>
    Completed,

    /// <summary>Só a necessidade ou a falta mudou, sem mexer no reservado.</summary>
    Requested,

    /// <summary>Baixou para a OS pela requisição (EST-03): o reservado vira baixado.</summary>
    Issued,

    /// <summary>Voltou ao estoque pela devolução ou pelo estorno da requisição (EST-03): o baixado desce.</summary>
    Returned,
}

/// <summary>
/// Histórico da reserva (<c>inventory.reservation_changes</c>, CA 18): quanto foi reservado, liberado e completado, por qual
/// origem e quando. Só inclusão; lido na aba Material da OS e no item.
/// </summary>
public sealed class ReservationChange : Entity
{
    public Guid ReservationId { get; init; }

    public Guid ProductionOrderId { get; init; }

    public Guid ItemId { get; init; }

    public Guid LocationId { get; init; }

    public ReservationChangeKind Kind { get; init; }

    /// <summary>Variação do reservado (positiva ao reservar ou completar, negativa ao liberar).</summary>
    public decimal QuantityDelta { get; init; }

    public decimal QuantityAfter { get; init; }

    public decimal RequestedAfter { get; init; }

    public decimal ShortageAfter { get; init; }

    public string Origin { get; init; } = string.Empty;

    public bool IsBackfill { get; init; }

    public DateTimeOffset At { get; init; }
}

/// <summary>Origens legíveis da reserva (CA 18).</summary>
public static class ReservationOrigins
{
    public const string Backfill = "Recomposição";
    public const string Completion = "Conclusão da OS";
    public const string Manual = "Reconciliação";

    public static string Entry(string movementNumber) => "Entrada " + movementNumber;

    /// <summary>Baixa pela requisição (EST-03): "Requisição REQ-2026-0001".</summary>
    public static string Requisition(string number) => "Requisição " + number;

    /// <summary>Devolução ao estoque (EST-03): "Devolução REQ-2026-0002".</summary>
    public static string Return(string number) => "Devolução " + number;

    /// <summary>Estorno da requisição ou da devolução (EST-03): "Estorno REQ-2026-0001".</summary>
    public static string Reversal(string number) => "Estorno " + number;
}

/// <summary>Reserva de uma linha num local, para o planejamento puro (sem banco).</summary>
public sealed record ReservationSlot(Guid LocationId, decimal Requested, decimal Quantity, decimal Issued);

/// <summary>
/// Resultado do planejamento de uma linha: as reservas por local depois da reconciliação, a falta (registrada no local padrão) e o
/// "Baixado acima da BOM" (já baixado − alvo, quando o alvo desce abaixo do baixado, CA 8).
/// </summary>
public sealed record ReservationPlan(IReadOnlyList<ReservationSlot> Slots, decimal Shortage, decimal OverIssued);

/// <summary>
/// Reconciliação pura de uma linha (OS e item) até o alvo (EST-02 CA 1, 3 a 6, 8 e 11; ADR 0023 §6), sem banco:
/// <list type="bullet">
/// <item>Alvo abaixo do reservado + baixado: libera o excedente, primeiro dos locais de menor prioridade, nunca abaixo do baixado.</item>
/// <item>Alvo acima: reserva o disponível dos locais na prioridade (o padrão primeiro, depois os demais em ordem de código), uma
/// reserva por local; o que não couber vira falta no local da falta (o padrão).</item>
/// <item>A necessidade de cada local é o reservado + o baixado; a do local da falta soma a falta.</item>
/// </list>
/// Completar a falta quando chega material é o mesmo cálculo com o alvo atual e só o local da entrada como candidato.
/// </summary>
public static class ReservationPlanner
{
    public const int Decimals = 4;

    public static decimal Round(decimal quantity) => Math.Round(quantity, Decimals, MidpointRounding.AwayFromZero);

    public static ReservationPlan Plan(
        decimal target, IReadOnlyList<ReservationSlot> current, IReadOnlyList<Guid> priority, IReadOnlyDictionary<Guid, decimal> available, Guid shortageLocation)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(priority);
        ArgumentNullException.ThrowIfNull(available);
        target = Math.Max(0m, Round(target));
        var quantity = current.GroupBy(s => s.LocationId).ToDictionary(g => g.Key, g => g.Sum(s => s.Quantity));
        var issued = current.GroupBy(s => s.LocationId).ToDictionary(g => g.Key, g => g.Sum(s => s.Issued));
        var totalIssued = issued.Values.Sum();
        var need = Math.Max(0m, target - totalIssued);
        var reserved = quantity.Values.Sum();
        var shortage = 0m;
        if (reserved > need)
        {
            var excess = reserved - need;
            foreach (var location in quantity.Keys.OrderByDescending(l => Rank(priority, l)).ThenByDescending(l => l).ToList())
            {
                var take = Math.Min(quantity[location], excess);
                quantity[location] -= take;
                excess -= take;
            }
        }
        else
        {
            var extra = need - reserved;
            foreach (var location in priority.Distinct())
            {
                if (extra <= 0)
                {
                    break;
                }

                var take = Math.Min(Math.Max(0m, Round(available.GetValueOrDefault(location))), extra);
                if (take > 0)
                {
                    quantity[location] = quantity.GetValueOrDefault(location) + take;
                    extra -= take;
                }
            }

            shortage = extra;
        }

        var locations = quantity.Keys.Union(issued.Keys).ToHashSet();
        if (shortage > 0)
        {
            locations.Add(shortageLocation);
        }

        var slots = locations.OrderBy(l => Rank(priority, l)).ThenBy(l => l)
            .Select(l =>
            {
                var q = quantity.GetValueOrDefault(l);
                var i = issued.GetValueOrDefault(l);
                return new ReservationSlot(l, q + i + (l == shortageLocation ? shortage : 0m), q, i);
            }).ToList();
        return new ReservationPlan(slots, shortage, Math.Max(0m, totalIssued - target));
    }

    /// <summary>
    /// Ordem de atendimento das faltas quando chega material (ponto 28, CA 11): primeiro a OS que originou a compra, depois a BOM
    /// liberada há mais tempo (sem data por último), e por fim o id da OS (estável).
    /// </summary>
    public static IReadOnlyList<Guid> CompletionOrder(IEnumerable<(Guid OrderId, DateTimeOffset? ReleasedAt)> orders, Guid? originOrderId)
    {
        ArgumentNullException.ThrowIfNull(orders);
        return orders.GroupBy(o => o.OrderId).Select(g => (OrderId: g.Key, ReleasedAt: g.Min(x => x.ReleasedAt)))
            .OrderBy(o => o.OrderId == originOrderId ? 0 : 1)
            .ThenBy(o => o.ReleasedAt is null ? 1 : 0)
            .ThenBy(o => o.ReleasedAt)
            .ThenBy(o => o.OrderId)
            .Select(o => o.OrderId).ToList();
    }

    /// <summary>
    /// Ordem de redução das reservas quando o inventário conta abaixo do reservado (EST-05, decisão do PO de 23/10/2026): primeiro a
    /// BOM liberada mais recentemente, a sem data por último (recomposição antiga), e por fim o id da OS (estável).
    /// </summary>
    public static IReadOnlyList<Guid> ReductionOrder(IEnumerable<(Guid OrderId, DateTimeOffset? ReleasedAt)> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);
        return orders.GroupBy(o => o.OrderId).Select(g => (OrderId: g.Key, ReleasedAt: g.Max(x => x.ReleasedAt)))
            .OrderBy(o => o.ReleasedAt is null ? 1 : 0)
            .ThenByDescending(o => o.ReleasedAt)
            .ThenBy(o => o.OrderId)
            .Select(o => o.OrderId).ToList();
    }

    /// <summary>Alvo por item da BOM liberada (CA 1): a soma das linhas do item; 0 para a OS cancelada ou concluída.</summary>
    public static IReadOnlyDictionary<Guid, decimal> Targets(IEnumerable<(Guid ItemId, decimal RequiredQty)> lines, bool orderClosed)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.GroupBy(l => l.ItemId).ToDictionary(g => g.Key, g => orderClosed ? 0m : Math.Max(0m, Round(g.Sum(l => l.RequiredQty))));
    }

    private static int Rank(IReadOnlyList<Guid> priority, Guid location)
    {
        for (var i = 0; i < priority.Count; i++)
        {
            if (priority[i] == location)
            {
                return i;
            }
        }

        return int.MaxValue;
    }
}
