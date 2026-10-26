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
/// Entrega simulada do projeto virtual (PLN-05 CA 3 e 4), sem gravar nada: tamanho da fila real e posição usada (1 até o tamanho + 1), hoje
/// (a aprovação), o fim da fabricação e a entrega prevista (com o prazo de instalação da PLN-01b quando <c>IncludesInstallation</c>), ou o
/// motivo em código (<see cref="ProjectForecastReasons"/>), e os projetos reais que passam a atrasar (prometido antes da nova previsão, que
/// piora). Só ids e datas: nenhuma hora, centro ou custo.
/// </summary>
public sealed record DeliverySimulation(
    int QueueSize, int Position, DateOnly Today, DateOnly? FabricationEnd, DateOnly? DeliveryDate, string? NoForecastReason, bool IncludesInstallation,
    IReadOnlyList<Guid> NewlyLateProjectIds);

/// <summary>
/// Política nomeada do rate limit das simulações do motor (PLN-02 CA 10 e PLN-05 CA 10, §4.3 transversal): 20 por minuto por usuário, por
/// rota; acima, 429 com <see cref="SimulateLimitMessage"/>. Fica no contrato para o Commercial usar sem depender do Production.
/// </summary>
public static class PlanningRateLimits
{
    public const string SimulatePolicy = "planning-simulate";

    public const int SimulatePerMinute = 20;

    public const string SimulateLimitMessage = "Aguarde alguns segundos para simular de novo.";
}

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

    /// <summary>
    /// "Se eu aceitar agora, quando entrego?" (PLN-05): roda <see cref="SimulateAsync"/> com a fila de hoje (<see cref="IPlanningQueue"/>), sem e
    /// com o projeto virtual na posição <see cref="SimulatedProject.QueuePosition"/> (null ou fora da faixa = no fim), e devolve a entrega
    /// prevista do virtual (fim da fabricação + prazo de instalação quando <paramref name="hasInstallationBudget"/>) e quem passa a atrasar.
    /// Só lê: nenhum lock, nada gravado.
    /// </summary>
    Task<DeliverySimulation> SimulateDeliveryAsync(SimulatedProject project, bool hasInstallationBudget, CancellationToken cancellationToken = default);

    /// <summary>
    /// Última previsão de entrega publicada de cada projeto pedido (PLN-01b CA 11: a TV na PNL-01b); os que nunca foram calculados ficam fora.
    /// </summary>
    Task<IReadOnlyList<ProjectDeliveryForecast>> ProjectForecastsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// Motivo da previsão vazia do projeto (PLN-01b, regras): sempre código, traduzido na tela (ADR 0019 §19, sem texto livre no evento).
/// </summary>
public static class ProjectForecastReasons
{
    /// <summary>Alguma OS de fabricação do projeto sem etapas ("Sem roteiro").</summary>
    public const string NoRouting = "NoRouting";

    /// <summary>Etapa sem centro de trabalho.</summary>
    public const string NoWorkCenter = "NoWorkCenter";

    /// <summary>Centro sem capacidade nos próximos 2 anos.</summary>
    public const string NoCapacity = "NoCapacity";

    /// <summary>Projeto sem nenhuma OS de fabricação não cancelada.</summary>
    public const string NoFabricationOrder = "NoFabricationOrder";

    public static IReadOnlyList<string> All { get; } = [NoRouting, NoWorkCenter, NoCapacity, NoFabricationOrder];
}

/// <summary>
/// Data prevista do projeto mudou (PLN-01b CA 7): entrega prevista e fim da fabricação, ou o motivo (código) e a OS que impede a data, e
/// quando foi calculada. Só ids, datas e código (ADR 0019 §19). O Commercial grava em <c>commercial.project_forecasts</c> só se
/// <c>ComputedAt</c> for mais novo que o gravado (o outbox não garante ordem).
/// </summary>
public sealed record ProjectForecastChanged(
    Guid ProjectId, DateOnly? ForecastDate, DateOnly? FabricationEnd, string? Reason, DateTimeOffset ComputedAt, Guid? ReasonOrderId = null) : BemAqui.SharedKernel.IIntegrationEvent;

/// <summary>Previsão do projeto no plano (PLN-01b CA 11, PNL-01b): entrega, fim da fabricação, motivo e quando foi calculada.</summary>
public sealed record ProjectDeliveryForecast(Guid ProjectId, DateOnly? ForecastDate, DateOnly? FabricationEnd, string? Reason, Guid? ReasonOrderId, DateTimeOffset ComputedAt);
