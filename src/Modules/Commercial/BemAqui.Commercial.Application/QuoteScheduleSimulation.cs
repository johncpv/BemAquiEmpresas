using BemAqui.Commercial.Domain;

namespace BemAqui.Commercial.Application;

/// <summary>Item do orçamento para a simulação: sequência, tipo, quantidade e os centros do roteiro na ordem das etapas (vazio sem roteiro).</summary>
public sealed record SimulationItem(Guid Id, int Sequence, QuoteItemKind Kind, decimal Quantity, IReadOnlyList<Guid> RoutingCenters);

/// <summary>Linha de Mão de obra com centro, na ordem da composição: horas para uma unidade do item.</summary>
public sealed record SimulationLaborLine(Guid QuoteItemId, Guid WorkCenterId, decimal Hours);

/// <summary>Etapa da OS virtual: um centro e as horas (Σ horas das linhas do centro × quantidade do item).</summary>
public sealed record SimulationStep(Guid WorkCenterId, decimal Hours);

/// <summary>OS virtual de um item do orçamento, com as etapas em sequência.</summary>
public sealed record SimulationOrder(Guid QuoteItemId, IReadOnlyList<SimulationStep> Steps);

/// <summary>
/// Projeto virtual do orçamento (PLN-05 CA 1 e 2, pontos 54 a 56), puro: uma OS por item que gera OS pela D14 da Fase 3 (Equipamento e
/// Produto; Serviço nunca) e tem horas por centro; na OS, uma etapa por centro das linhas de Mão de obra do item, com horas = Σ horas das
/// linhas daquele centro × quantidade (4 casas). A ordem das etapas segue os centros do roteiro (o do item ou o sugerido para o produto);
/// sem roteiro, a ordem das linhas na composição; centros fora do roteiro entram no fim, na ordem das linhas.
/// </summary>
public static class QuoteScheduleSimulation
{
    public static IReadOnlyList<SimulationOrder> Build(IEnumerable<SimulationItem> items, IEnumerable<SimulationLaborLine> labor)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(labor);
        var lines = labor.ToLookup(l => l.QuoteItemId);
        var orders = new List<SimulationOrder>();
        foreach (var item in items.Where(GeneratesOrder).OrderBy(i => i.Sequence).ThenBy(i => i.Id))
        {
            var mine = lines[item.Id].ToList();
            var hours = mine.GroupBy(l => l.WorkCenterId).ToDictionary(g => g.Key, g => Math.Round(g.Sum(l => l.Hours) * item.Quantity, 4, MidpointRounding.AwayFromZero));
            var byLines = mine.Select(l => l.WorkCenterId).Distinct();
            var sequence = item.RoutingCenters.Where(hours.ContainsKey).Concat(byLines).Distinct();
            var steps = sequence.Where(c => hours[c] > 0m).Select(c => new SimulationStep(c, hours[c])).ToList();
            if (steps.Count > 0)
            {
                orders.Add(new SimulationOrder(item.Id, steps));
            }
        }

        return orders;
    }

    /// <summary>D14 da Fase 3: Equipamento gera OS automática e Produto pelo "Gerar OS"; Serviço nunca.</summary>
    public static bool GeneratesOrder(SimulationItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Kind != QuoteItemKind.Service && item.Quantity > 0m;
    }

    /// <summary>Dias corridos de hoje até a entrega (CA 6), como o <c>delivery_days</c> do orçamento.</summary>
    public static int CalendarDays(DateOnly today, DateOnly date) => date.DayNumber - today.DayNumber;

    /// <summary>Dias além do prazo prometido (CA 4): 0 cabe no prazo; null sem prazo prometido ou sem data.</summary>
    public static int? DaysBeyond(DateOnly? promised, DateOnly? delivery) =>
        promised is { } p && delivery is { } d ? Math.Max(0, d.DayNumber - p.DayNumber) : null;
}
