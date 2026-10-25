namespace BemAqui.Production.Domain;

/// <summary>Projeto com OS de fabricação aberta (candidato à fila): número, data prometida e assinatura do contrato.</summary>
public sealed record QueueCandidate(Guid ProjectId, string Number, DateOnly? PromisedDate, DateOnly? SignedAt);

/// <summary>Posição de um projeto antes → depois; <c>From</c> nulo = entrou, <c>To</c> nulo = saiu.</summary>
public sealed record QueueMove(Guid ProjectId, int? From, int? To);

/// <summary>Resultado da manutenção da fila: a ordem nova, quem entrou (na ordem em que entrou) e quem saiu.</summary>
public sealed record QueueSync(IReadOnlyList<Guid> Order, IReadOnlyList<Guid> Entered, IReadOnlyList<Guid> Left)
{
    /// <summary>A fila estava vazia e foi montada agora (carga inicial, CA 3).</summary>
    public bool Initial { get; init; }

    public bool Changed => Entered.Count > 0 || Left.Count > 0;
}

/// <summary>
/// Regras puras da fila de prioridade (PLN-02 CA 2, 3 e 7): manutenção pela data prometida, posições contíguas e a validação da
/// permutação enviada no "Salvar ordem".
/// </summary>
public static class PlanningQueueRules
{
    /// <summary>Limite de projetos na lista enviada (a fila de um tenant cabe com folga).</summary>
    public const int MaxProjects = 1000;

    /// <summary>
    /// Ordem padrão (PLN-01a CA 2, CA 3 desta story): data prometida (mais cedo primeiro, sem data no fim), depois a assinatura e, no
    /// empate, o número do projeto.
    /// </summary>
    public static IReadOnlyList<QueueCandidate> DefaultOrder(IEnumerable<QueueCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return candidates
            .OrderBy(p => p.PromisedDate is null ? 1 : 0).ThenBy(p => p.PromisedDate ?? DateOnly.MinValue)
            .ThenBy(p => p.SignedAt is null ? 1 : 0).ThenBy(p => p.SignedAt ?? DateOnly.MinValue)
            .ThenBy(p => p.Number, StringComparer.Ordinal).ThenBy(p => p.ProjectId)
            .ToList();
    }

    /// <summary>
    /// Mantém a fila (CA 2): sai quem não tem mais OS de fabricação aberta; quem ganhou a primeira entra <b>pela data prometida</b>, logo
    /// antes do primeiro projeto da fila com data prometida posterior à dele (no empate, depois dos que já estavam; sem data prometida, no
    /// fim). Vários novos entram um a um pela ordem padrão. Com a fila vazia, o resultado é a ordem padrão inteira (carga inicial, CA 3).
    /// </summary>
    public static QueueSync Sync(IReadOnlyList<Guid> current, IReadOnlyCollection<QueueCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(candidates);
        var byId = candidates.GroupBy(c => c.ProjectId).ToDictionary(g => g.Key, g => g.First());
        var order = current.Distinct().Where(byId.ContainsKey).ToList();
        var left = current.Distinct().Where(id => !byId.ContainsKey(id)).ToList();
        var present = order.ToHashSet();
        var entered = new List<Guid>();
        foreach (var project in DefaultOrder(byId.Values.Where(c => !present.Contains(c.ProjectId))))
        {
            var at = project.PromisedDate is { } promised
                ? order.FindIndex(id => byId[id].PromisedDate is { } other && other > promised)
                : -1;
            if (at < 0)
            {
                order.Add(project.ProjectId);
            }
            else
            {
                order.Insert(at, project.ProjectId);
            }

            entered.Add(project.ProjectId);
        }

        return new QueueSync(order, entered, left) { Initial = current.Count == 0 && entered.Count > 0 };
    }

    /// <summary>
    /// Segurança (CA 7): a lista enviada é uma permutação exata da fila atual (mesmos projetos, nenhum a mais, a menos ou repetido).
    /// </summary>
    public static bool IsPermutation(IReadOnlyList<Guid> current, IReadOnlyList<Guid>? proposed)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (proposed is null || proposed.Count != current.Count || proposed.Count > MaxProjects)
        {
            return false;
        }

        var set = proposed.ToHashSet();
        return set.Count == proposed.Count && set.SetEquals(current);
    }

    /// <summary>Posições que mudaram (1..n), na ordem nova: entrou (<c>From</c> nulo), saiu (<c>To</c> nulo) e moveu.</summary>
    public static IReadOnlyList<QueueMove> Moves(IReadOnlyList<Guid> before, IReadOnlyList<Guid> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var from = Positions(before);
        var to = Positions(after);
        var moves = after.Select(id => new QueueMove(id, from.TryGetValue(id, out var f) ? f : null, to[id]))
            .Where(m => m.From != m.To).ToList();
        moves.AddRange(before.Where(id => !to.ContainsKey(id)).Select(id => new QueueMove(id, from[id], null)));
        return moves;
    }

    /// <summary>Posição contígua de cada projeto (1, 2, 3…), sem buraco.</summary>
    public static Dictionary<Guid, int> Positions(IReadOnlyList<Guid> order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var result = new Dictionary<Guid, int>(order.Count);
        foreach (var id in order)
        {
            result.TryAdd(id, result.Count + 1);
        }

        return result;
    }
}
