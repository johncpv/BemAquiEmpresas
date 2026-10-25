using BemAqui.Production.Contracts;

namespace BemAqui.Production.Domain;

/// <summary>Projeto na fila do plano (PLN-01a CA 2): a posição da fila manual (PLN-02) ou, sem ela, a data prometida, a assinatura e o número.</summary>
public sealed record ScheduleProjectInput(Guid ProjectId, string Number, DateOnly? PromisedDate, DateOnly? SignedAt, int? QueuePosition = null);

/// <summary>
/// Etapa da OS para o motor (CA 1 e 3): sequência, centro, horas padrão, horas apontadas (minutos líquidos ÷ 60, só os apontamentos da
/// tentativa atual: o retrabalho começa do zero) e a situação.
/// </summary>
public sealed record ScheduleStepInput(Guid StepId, int Sequence, string Name, Guid? WorkCenterId, decimal StandardHours, decimal LoggedHours, ProductionStepStatus Status);

/// <summary>OS de fabricação não concluída e não cancelada, com a sequência do item no projeto (as OS do projeto seguem a ordem dos itens).</summary>
public sealed record ScheduleOrderInput(Guid OrderId, string Number, Guid ProjectId, int ItemSequence, IReadOnlyList<ScheduleStepInput> Steps);

/// <summary>Capacidade de um centro a partir de hoje (<c>IWorkCenterCapacity</c>, lida uma vez por execução): horas por dia, até 2 anos.</summary>
public sealed record ScheduleCenterInput(Guid WorkCenterId, string Name, IReadOnlyList<DailyCapacity> Days);

/// <summary>Entrada do motor (CA 1): fila de projetos, OS e etapas, capacidade de cada centro e a data de hoje (local).</summary>
public sealed record ScheduleInput(DateOnly Today, IReadOnlyList<ScheduleProjectInput> Projects, IReadOnlyList<ScheduleOrderInput> Orders, IReadOnlyList<ScheduleCenterInput> Centers);

/// <summary>Horas de uma etapa num centro e num dia (<c>production.schedule_allocations</c>).</summary>
public sealed record ScheduledAllocation(Guid StepId, Guid OrderId, Guid WorkCenterId, DateOnly Date, decimal Hours);

/// <summary>Previsão de uma etapa não concluída (<c>production.schedule_step_forecasts</c>): horas restantes, início e fim, ou o motivo de não ter data.</summary>
public sealed record ScheduledStep(Guid StepId, Guid OrderId, int Sequence, decimal Hours, DateOnly? Start, DateOnly? End, string? NoForecastReason);

/// <summary>Previsão da OS: início (primeira etapa não concluída) e fim (última etapa), ou o motivo (CA 8).</summary>
public sealed record ScheduledOrder(Guid OrderId, Guid ProjectId, DateOnly? Start, DateOnly? End, string? NoForecastReason);

/// <summary>Fim da fabricação do projeto: o maior fim entre as OS; sem data se alguma OS ficou sem data.</summary>
public sealed record ScheduledProject(Guid ProjectId, DateOnly? FabricationEnd, string? NoForecastReason);

public sealed record ScheduleResult(IReadOnlyList<ScheduledStep> Steps, IReadOnlyList<ScheduledOrder> Orders, IReadOnlyList<ScheduledProject> Projects, IReadOnlyList<ScheduledAllocation> Allocations);

/// <summary>
/// Agendamento finito simples, para frente (PLN-01a, <c>04-fluxos</c> §3.4, ADR 0029 §4), puro e determinístico: (1) fila de projetos;
/// no projeto, OS pela sequência do item; na OS, etapas pela sequência; (2) horas restantes; (3) etapas em andamento primeiro, a partir
/// de hoje; depois as demais, cada uma no primeiro dia com sobra no centro a partir de max(hoje, dia seguinte ao fim da etapa anterior da
/// mesma OS), dividindo-se pelos dias. OS diferentes não esperam uma pela outra.
/// </summary>
public static class FiniteScheduler
{
    /// <summary>Horizonte da capacidade: 2 anos (<see cref="CapacityCalculator.MaxDays"/>).</summary>
    public const int HorizonDays = CapacityCalculator.MaxDays;

    private const decimal MinimumInProgressShare = 0.1m;

    /// <summary>
    /// Horas restantes (CA 3): <c>Done</c> = 0; <c>Waiting</c>, <c>Ready</c> e <c>Rejected</c> (retrabalho) = padrão; <c>InProgress</c> e
    /// <c>Paused</c> = max(padrão − apontadas, 10% do padrão). Quatro casas.
    /// </summary>
    public static decimal RemainingHours(ProductionStepStatus status, decimal standardHours, decimal loggedHours)
    {
        var standard = Math.Max(standardHours, 0m);
        var remaining = status switch
        {
            ProductionStepStatus.Done => 0m,
            ProductionStepStatus.InProgress or ProductionStepStatus.Paused => Math.Max(standard - Math.Max(loggedHours, 0m), standard * MinimumInProgressShare),
            _ => standard,
        };
        return Math.Round(remaining, 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>"Etapa {nome} sem centro de trabalho" (CA 8).</summary>
    public static string NoCenter(string stepName) => $"Etapa {stepName} sem centro de trabalho";

    /// <summary>"Centro {nome} sem capacidade nos próximos 2 anos" (CA 8).</summary>
    public static string NoCapacity(string centerName) => $"Centro {centerName} sem capacidade nos próximos 2 anos";

    /// <summary>
    /// Ordem da fila (CA 2): com a fila manual, pela posição (quem não está nela vai para o fim); antes dela, pela data prometida (mais
    /// cedo primeiro, sem data no fim), depois a assinatura e, no empate, o número do projeto.
    /// </summary>
    public static IReadOnlyList<ScheduleProjectInput> Queue(IEnumerable<ScheduleProjectInput> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);
        return projects
            .OrderBy(p => p.QueuePosition is null ? 1 : 0).ThenBy(p => p.QueuePosition ?? 0)
            .ThenBy(p => p.PromisedDate is null ? 1 : 0).ThenBy(p => p.PromisedDate ?? DateOnly.MinValue)
            .ThenBy(p => p.SignedAt is null ? 1 : 0).ThenBy(p => p.SignedAt ?? DateOnly.MinValue)
            .ThenBy(p => p.Number, StringComparer.Ordinal).ThenBy(p => p.ProjectId)
            .ToList();
    }

    public static ScheduleResult Run(ScheduleInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var board = new Board(input.Today, input.Centers);
        var orders = OrderedOrders(input);
        var planned = new Dictionary<Guid, ScheduledStep>();
        var allocations = new List<ScheduledAllocation>();

        // CA 4: quem já está na máquina continua, na ordem da fila, a partir de hoje.
        foreach (var order in orders)
        {
            foreach (var step in order.Steps.Where(s => s.Status is ProductionStepStatus.InProgress or ProductionStepStatus.Paused))
            {
                planned[step.StepId] = board.Place(order, step, input.Today, allocations);
            }
        }

        // CA 5 e 6: as demais etapas, pela fila; dentro da OS, em sequência e no dia seguinte ao fim da anterior.
        foreach (var order in orders)
        {
            DateOnly? previousEnd = null;
            string? blocked = null;
            foreach (var step in order.Steps.Where(s => s.Status != ProductionStepStatus.Done))
            {
                if (blocked is not null)
                {
                    planned[step.StepId] = new ScheduledStep(step.StepId, order.OrderId, step.Sequence, Remaining(step), null, null, blocked);
                    continue;
                }

                if (!planned.TryGetValue(step.StepId, out var placed))
                {
                    var earliest = previousEnd is { } end && end.AddDays(1) > input.Today ? end.AddDays(1) : input.Today;
                    placed = board.Place(order, step, earliest, allocations);
                    planned[step.StepId] = placed;
                }

                blocked = placed.NoForecastReason;
                previousEnd = placed.End is { } e && (previousEnd is null || e > previousEnd) ? e : previousEnd;
            }
        }

        var steps = orders.SelectMany(o => o.Steps.Where(s => s.Status != ProductionStepStatus.Done).Select(s => planned[s.StepId])).ToList();
        var stepsByOrder = steps.ToLookup(s => s.OrderId);
        var byOrder = orders.Select(o => Summarize(o, [.. stepsByOrder[o.OrderId]])).ToList();
        var projects = byOrder.GroupBy(o => o.ProjectId).Select(Project).ToList();
        return new ScheduleResult(steps, byOrder, projects, allocations);
    }

    private static decimal Remaining(ScheduleStepInput step) => RemainingHours(step.Status, step.StandardHours, step.LoggedHours);

    /// <summary>OS na ordem da fila (CA 1): projeto, sequência do item, número; OS sem etapas (sem roteiro) não entram (CA 9).</summary>
    private static List<ScheduleOrderInput> OrderedOrders(ScheduleInput input)
    {
        var rank = Queue(input.Projects).Select((p, i) => (p.ProjectId, i)).ToDictionary(x => x.ProjectId, x => x.i);
        return input.Orders.Where(o => o.Steps.Count > 0)
            .OrderBy(o => rank.TryGetValue(o.ProjectId, out var r) ? r : int.MaxValue)
            .ThenBy(o => o.ItemSequence).ThenBy(o => o.Number, StringComparer.Ordinal).ThenBy(o => o.OrderId)
            .Select(o => o with { Steps = [.. o.Steps.OrderBy(s => s.Sequence).ThenBy(s => s.StepId)] })
            .ToList();
    }

    private static ScheduledOrder Summarize(ScheduleOrderInput order, List<ScheduledStep> steps)
    {
        if (steps.FirstOrDefault(s => s.NoForecastReason is not null) is { } blocked)
        {
            return new ScheduledOrder(order.OrderId, order.ProjectId, null, null, blocked.NoForecastReason);
        }

        return steps.Count == 0
            ? new ScheduledOrder(order.OrderId, order.ProjectId, null, null, null)
            : new ScheduledOrder(order.OrderId, order.ProjectId, steps.Min(s => s.Start), steps.Max(s => s.End), null);
    }

    private static ScheduledProject Project(IGrouping<Guid, ScheduledOrder> orders)
    {
        if (orders.FirstOrDefault(o => o.NoForecastReason is not null) is { } blocked)
        {
            return new ScheduledProject(orders.Key, null, blocked.NoForecastReason);
        }

        return new ScheduledProject(orders.Key, orders.Max(o => o.End), null);
    }

    /// <summary>Capacidade que sobra em cada centro e dia, consumida na ordem de prioridade.</summary>
    private sealed class Board
    {
        private readonly DateOnly _today;
        private readonly Dictionary<Guid, (string Name, decimal[] Capacity, decimal[] Free)> _centers = [];

        public Board(DateOnly today, IReadOnlyList<ScheduleCenterInput> centers)
        {
            _today = today;
            foreach (var center in centers)
            {
                var capacity = new decimal[HorizonDays];
                foreach (var day in center.Days)
                {
                    var index = day.Date.DayNumber - today.DayNumber;
                    if (index >= 0 && index < HorizonDays)
                    {
                        capacity[index] = Math.Max(day.Hours, 0m);
                    }
                }

                _centers[center.WorkCenterId] = (center.Name, capacity, (decimal[])capacity.Clone());
            }
        }

        public ScheduledStep Place(ScheduleOrderInput order, ScheduleStepInput step, DateOnly earliest, List<ScheduledAllocation> allocations)
        {
            var hours = Remaining(step);
            ScheduledStep None(string reason) => new(step.StepId, order.OrderId, step.Sequence, hours, null, null, reason);
            if (step.WorkCenterId is not { } centerId)
            {
                return None(NoCenter(step.Name));
            }

            if (!_centers.TryGetValue(centerId, out var center))
            {
                return None(NoCenter(step.Name));
            }

            var from = Math.Max(earliest.DayNumber - _today.DayNumber, 0);
            if (hours == 0m)
            {
                // CA 8: horas 0 → início = fim = o primeiro dia útil do centro em que ela poderia começar, sem ocupar capacidade.
                for (var i = from; i < HorizonDays; i++)
                {
                    if (center.Capacity[i] > 0m)
                    {
                        var day = _today.AddDays(i);
                        return new ScheduledStep(step.StepId, order.OrderId, step.Sequence, 0m, day, day, null);
                    }
                }

                return None(NoCapacity(center.Name));
            }

            var taken = new List<(int Index, decimal Hours)>();
            var left = hours;
            for (var i = from; i < HorizonDays && left > 0m; i++)
            {
                var take = Math.Min(center.Free[i], left);
                if (take > 0m)
                {
                    taken.Add((i, take));
                    center.Free[i] -= take;
                    left -= take;
                }
            }

            if (left > 0m)
            {
                taken.ForEach(t => center.Free[t.Index] += t.Hours);
                return None(NoCapacity(center.Name));
            }

            allocations.AddRange(taken.Select(t => new ScheduledAllocation(step.StepId, order.OrderId, centerId, _today.AddDays(t.Index), t.Hours)));
            return new ScheduledStep(step.StepId, order.OrderId, step.Sequence, hours, _today.AddDays(taken[0].Index), _today.AddDays(taken[^1].Index), null);
        }
    }
}
