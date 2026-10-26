using System.Text.Json.Serialization;

namespace BemAqui.Production.Contracts;

/// <summary>Gatilho da execução do plano (PLN-01a CA 10): de hora em hora, "Recalcular agora" ou por evento (PLN-01b).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScheduleTrigger>))]
public enum ScheduleTrigger
{
    Hourly,
    Manual,
    Event,
}

/// <summary>
/// Situação da execução do plano: <c>Pending</c> é a marca "pendente" do tenant (no máximo uma; ADR 0029 §2), que a próxima execução
/// assume; <c>Succeeded</c> gravou o plano; <c>Failed</c> manteve o plano anterior e guarda o erro.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScheduleRunStatus>))]
public enum ScheduleRunStatus
{
    Pending,
    Succeeded,
    Failed,
}

/// <summary>Previsão de uma etapa não concluída (PLN-01a CA 15 e 16): horas restantes, início e fim previstos ou o motivo de não ter data.</summary>
public sealed record StepForecastInfo(Guid StepId, int Sequence, decimal Hours, DateOnly? PlannedStart, DateOnly? PlannedEnd, string? NoForecastReason);

/// <summary>Previsão da OS de fabricação: início (primeira etapa não concluída), fim (última etapa) ou o motivo, e as etapas.</summary>
public sealed record OrderForecastInfo(Guid OrderId, DateOnly? PlannedStart, DateOnly? PlannedEnd, string? NoForecastReason, IReadOnlyList<StepForecastInfo> Steps);

/// <summary>Fim da fabricação do projeto: o maior fim entre as OS; sem data se alguma OS ficou sem data (o motivo é o da primeira).</summary>
public sealed record ProjectFabricationForecast(Guid ProjectId, DateOnly? FabricationEnd, string? NoForecastReason);

/// <summary>Horas de uma etapa num centro e num dia.</summary>
public sealed record ScheduleAllocationInfo(Guid StepId, Guid OrderId, Guid WorkCenterId, DateOnly Date, decimal Hours);

/// <summary>Execução do plano (<c>production.schedule_runs</c>): gatilho, situação, contagens, duração e erro.</summary>
public sealed record ScheduleRunInfo(
    Guid Id, ScheduleTrigger Trigger, ScheduleRunStatus Status, DateTimeOffset RequestedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, int OrdersCount, int StepsCount,
    int ChangedOrdersCount, int WithoutForecastCount, int DurationMs, string? Error);

/// <summary>Etapa de uma OS virtual da simulação (PLN-05): nome, centro e horas padrão.</summary>
public sealed record SimulatedStep(string Name, Guid? WorkCenterId, decimal StandardHours);

/// <summary>
/// Projeto virtual da simulação (PLN-05: o orçamento "e se fechar hoje?"): id escolhido por quem chama, número, data prometida, posição
/// na fila (null = pela ordem padrão) e as OS, cada uma com as etapas em sequência.
/// </summary>
public sealed record SimulatedProject(Guid ProjectId, string Number, DateOnly? PromisedDate, int? QueuePosition, IReadOnlyList<IReadOnlyList<SimulatedStep>> Orders);

/// <summary>Resultado da simulação, em memória: a previsão de cada OS (as virtuais com id novo) e o fim da fabricação de cada projeto.</summary>
public sealed record ScheduleSimulation(IReadOnlyList<OrderForecastInfo> Orders, IReadOnlyList<ProjectFabricationForecast> Projects);

/// <summary>
/// Plano de produção para os outros módulos e telas (PLN-01a CA 15, ADR 0029): lê as tabelas do plano (a última execução bem-sucedida)
/// ou simula em memória com outra ordem de fila ou um projeto virtual, <b>sem gravar nada</b> (PLN-02 e PLN-05). Sem custo.
/// </summary>
public interface IProductionSchedule
{
    /// <summary>Previsão das OS pedidas; as que não estão no plano (concluídas, canceladas, de instalação, sem etapas) ficam fora.</summary>
    Task<IReadOnlyList<OrderForecastInfo>> ForOrdersAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default);

    /// <summary>Fim da fabricação de cada projeto pedido que tem OS no plano.</summary>
    Task<IReadOnlyList<ProjectFabricationForecast>> FabricationEndByProjectsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default);

    /// <summary>Alocações de <paramref name="from"/> a <paramref name="to"/> (inclusive), opcionalmente só de alguns centros.</summary>
    Task<IReadOnlyList<ScheduleAllocationInfo>> AllocationsAsync(DateOnly from, DateOnly to, IReadOnlyCollection<Guid>? workCenterIds = null, CancellationToken cancellationToken = default);

    /// <summary>Última execução terminada (com sucesso ou com erro); null se o plano nunca rodou.</summary>
    Task<ScheduleRunInfo?> LastRunAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Roda o motor em memória com a fila na ordem de <paramref name="queueOrder"/> (ids de projeto; quem não está nela vai depois, pela
    /// ordem padrão) e, se houver, o projeto virtual. Não grava nada.
    /// </summary>
    Task<ScheduleSimulation> SimulateAsync(IReadOnlyList<Guid> queueOrder, SimulatedProject? extraProject = null, CancellationToken cancellationToken = default);
}
