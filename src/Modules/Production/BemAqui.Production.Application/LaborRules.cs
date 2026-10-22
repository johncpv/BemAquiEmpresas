using BemAqui.Production.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Production.Application;

/// <summary>Intervalo de uma pausa (fim nulo = em aberto).</summary>
public sealed record PauseSpan(DateTimeOffset StartedAt, DateTimeOffset? EndedAt);

/// <summary>Intervalo de um apontamento do funcionário, para a regra de sobreposição (fim nulo = em aberto).</summary>
public sealed record EntrySpan(Guid Id, DateTimeOffset StartedAt, DateTimeOffset? EndedAt);

/// <summary>Etapa vista pela regra da situação da OS: situação e se é de qualidade.</summary>
public sealed record StepState(ProductionStepStatus? Status, bool IsQualityStep);

/// <summary>Etapa da fila do operador antes de ordenar (CA 1): centro, prioridade e prometido da OS.</summary>
public sealed record QueueKey(Guid StepId, bool IsMyCenter, int Priority, DateOnly? PromisedDate, string OrderNumber, int Sequence);

/// <summary>
/// Regras do apontamento sem banco (PRD-03a): minutos líquidos, situação da etapa e da OS com pausa, saída e vários operadores
/// (04-fluxos §1, CA 7 a 17), ordem da fila (CA 1), sobreposição e retroativo do supervisor (CA 18 e 19).
/// </summary>
public static class LaborRules
{
    public const int MaxBackdateDays = 7;
    public const int MaxPauseNotes = 120;
    public const int MaxReason = 200;
    public const int DefaultOpenAlertHours = 12;

    /// <summary>Σ das pausas em minutos (a aberta conta até <paramref name="now"/>), arredondado.</summary>
    public static int PausedMinutes(IEnumerable<PauseSpan> pauses, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pauses);
        return Round(pauses.Sum(p => Math.Max(((p.EndedAt ?? now) - p.StartedAt).TotalMinutes, 0)));
    }

    /// <summary>Minutos líquidos = (fim − início) − Σ pausas; o aberto conta até <paramref name="now"/>; nunca negativo.</summary>
    public static int NetMinutes(DateTimeOffset startedAt, DateTimeOffset? endedAt, IEnumerable<PauseSpan> pauses, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pauses);
        var end = endedAt ?? now;
        var paused = pauses.Sum(p => Math.Max(((p.EndedAt ?? end) - p.StartedAt).TotalMinutes, 0));
        return Math.Max(Round((end - startedAt).TotalMinutes - paused), 0);
    }

    /// <summary>Horas em <c>numeric(18,4)</c>.</summary>
    public static decimal Hours(int minutes) => Math.Round(minutes / 60m, 4, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Situação da etapa já iniciada e não concluída depois de uma mudança (CA 11, 12, 14 e 15): alguém trabalhando ou aguardando
    /// aprovação, <c>InProgress</c>; senão <c>Paused</c> (todos em pausa ou ninguém nela).
    /// </summary>
    public static ProductionStepStatus StartedStepStatus(bool anyoneWorking, bool awaitingApproval) =>
        anyoneWorking || awaitingApproval ? ProductionStepStatus.InProgress : ProductionStepStatus.Paused;

    /// <summary>
    /// Situação da OS pelas etapas (04-fluxos §1, CA 11, 12, 16 e 17): a etapa atual em andamento, <c>InProgress</c>; pausada,
    /// <c>Paused</c>; de qualidade pronta, <c>Quality</c> ("Aguardando qualidade" até a PRD-05); todas concluídas, <c>InProgress</c>
    /// ("Pronta para concluir", conclusão explícita na PRD-06a1). Concluída e cancelada não mudam.
    /// </summary>
    public static ProductionOrderStatus OrderStatus(ProductionOrderStatus current, IReadOnlyList<StepState> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (current is ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled || steps.Count == 0)
        {
            return current;
        }

        var index = -1;
        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i].Status != ProductionStepStatus.Done)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return ProductionOrderStatus.InProgress;
        }

        return steps[index] switch
        {
            { Status: ProductionStepStatus.InProgress } => ProductionOrderStatus.InProgress,
            { Status: ProductionStepStatus.Paused } => ProductionOrderStatus.Paused,
            { Status: ProductionStepStatus.Ready, IsQualityStep: true } => ProductionOrderStatus.Quality,
            _ => index == 0 && current == ProductionOrderStatus.Open ? ProductionOrderStatus.Open : ProductionOrderStatus.InProgress,
        };
    }

    /// <summary>
    /// Ordem da fila (CA 1): primeiro as do centro do cargo, depois as dos outros; cada grupo pela prioridade da OS e pela data
    /// prometida do projeto (sem data por último), e por fim pelo número da OS e pela etapa.
    /// </summary>
    public static IReadOnlyList<Guid> QueueOrder(IEnumerable<QueueKey> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        return steps.OrderByDescending(s => s.IsMyCenter).ThenBy(s => s.Priority).ThenBy(s => s.PromisedDate is null).ThenBy(s => s.PromisedDate)
            .ThenBy(s => s.OrderNumber, StringComparer.Ordinal).ThenBy(s => s.Sequence).Select(s => s.StepId).ToList();
    }

    /// <summary>Intervalos [início, fim) se cruzam; fim nulo é em aberto (CA 19).</summary>
    public static EntrySpan? Overlapping(DateTimeOffset startedAt, DateTimeOffset endedAt, IEnumerable<EntrySpan> others, Guid? except = null)
    {
        ArgumentNullException.ThrowIfNull(others);
        return others.FirstOrDefault(o => o.Id != except && startedAt < (o.EndedAt ?? DateTimeOffset.MaxValue) && o.StartedAt < endedAt);
    }

    /// <summary>
    /// Intervalo do "Apontar por funcionário" e da correção (CA 18 a 20): início no passado e até 7 dias, fim depois do início e
    /// não no futuro; pausas dentro do intervalo, sem se cruzar, com motivo e texto de até 120.
    /// </summary>
    public static Error? ValidateInterval(DateTimeOffset startedAt, DateTimeOffset endedAt, IReadOnlyList<(PauseReason Reason, string? Notes, DateTimeOffset StartedAt, DateTimeOffset EndedAt)> pauses, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(pauses);
        if (startedAt > now)
        {
            return Invalid("startedAt", "O início não pode ser no futuro.");
        }

        if (endedAt <= startedAt)
        {
            return Invalid("endedAt", "O fim precisa ser depois do início.");
        }

        if (endedAt > now.AddMinutes(1))
        {
            return Invalid("endedAt", "O fim não pode ser no futuro.");
        }

        if (startedAt < now.AddDays(-MaxBackdateDays))
        {
            return Error.Validation("labor_backdate_limit", "Apontamento retroativo só até 7 dias.", new Dictionary<string, string[]> { ["startedAt"] = ["Apontamento retroativo só até 7 dias."] });
        }

        var ordered = pauses.OrderBy(p => p.StartedAt).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var p = ordered[i];
            if (p.StartedAt < startedAt || p.EndedAt > endedAt || p.EndedAt <= p.StartedAt || (i > 0 && p.StartedAt < ordered[i - 1].EndedAt))
            {
                return Invalid("pauses", "Cada pausa precisa ficar dentro do apontamento, sem cruzar com outra.");
            }

            if (p.Notes is { Length: > MaxPauseNotes })
            {
                return Invalid("pauses", $"O texto da pausa tem até {MaxPauseNotes} caracteres.");
            }
        }

        return null;
    }

    /// <summary>"Marque os {n} itens que faltam" (CA 13).</summary>
    public static string MissingChecklist(int missing) => missing == 1 ? "Marque o item que falta." : $"Marque os {missing} itens que faltam.";

    /// <summary>Etapa precisa da BOM liberada (D12): a primeira etapa do roteiro nunca; as seguintes, a partir da ENG-02.</summary>
    public static bool NeedsBom(int sequenceIndex) => sequenceIndex > 0;

    internal static Error Invalid(string field, string message) =>
        Error.Validation("validation", message, new Dictionary<string, string[]> { [field] = [message] });

    private static int Round(double minutes) => (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
}