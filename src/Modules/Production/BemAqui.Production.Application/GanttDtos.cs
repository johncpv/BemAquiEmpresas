using System.Text.Json.Serialization;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Production.Application;

/// <summary>
/// Situação da barra da etapa (PLN-03 CA 2): <c>Planned</c> (previsto, tracejado), <c>InProgress</c> (parte feita preenchida e o resto
/// tracejado), <c>Done</c> (início e fim reais, traço contínuo) e <c>NoForecast</c> (o plano não datou a etapa; o motivo vai junto).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<GanttStepState>))]
public enum GanttStepState
{
    Planned,
    InProgress,
    Done,
    NoForecast,
}

/// <summary>OS de fabricação (datas do plano) ou de instalação (datas digitadas na OS, só leitura, CA 4).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GanttOrderKind>))]
public enum GanttOrderKind
{
    Fabrication,
    Installation,
}

/// <summary>Filtros do cronograma (CA 6 e 9), por allowlist: período até 12 meses, projeto, cliente, linha (até 120), centro e "Só em risco".</summary>
public sealed record GanttQuery(DateOnly? From = null, DateOnly? To = null, Guid? ProjectId = null, Guid? PartyId = null, string? Line = null, Guid? WorkCenterId = null, bool AtRisk = false);

/// <summary>
/// Etapa no cronograma (CA 2 e 8): letra e nome, centro, situação, horas padrão e restantes (do plano), a proporção feita, início e fim
/// previstos (etapa não concluída) e reais (já começada ou concluída), o motivo sem previsão e se é a que define a entrega (CA 3).
/// </summary>
public sealed record GanttStep(
    Guid StepId, int Sequence, string Name, string? Code, Guid? WorkCenterId, string? WorkCenterName, GanttStepState State, decimal StandardHours, decimal RemainingHours,
    decimal Progress, DateOnly? PlannedStart, DateOnly? PlannedEnd, DateOnly? ActualStart, DateOnly? ActualEnd, string? NoForecastReason, bool DrivesDelivery);

/// <summary>OS no cronograma: número, tipo, situação, início e fim (das etapas ou, na instalação, as datas digitadas) e as etapas.</summary>
public sealed record GanttOrder(Guid OrderId, string Number, GanttOrderKind Kind, string Status, DateOnly? Start, DateOnly? End, IReadOnlyList<GanttStep> Steps);

/// <summary>
/// Projeto no cronograma (CA 1 e 3): posição na fila, número, nome e cliente, início (da primeira etapa), previsto (PLN-01b) e prometido,
/// folga, risco pela função única, quantos dias depois do prometido e o motivo sem previsão, com as OS. Sem custo nem dado pessoal.
/// </summary>
public sealed record GanttProject(
    int Position, Guid ProjectId, string ProjectNumber, string ProjectName, Guid? PartyId, string? PartyName, DateOnly? Start, DateOnly? ForecastDate, DateOnly? PromisedDate,
    int? SlackDays, DeliveryRisk Risk, int? DaysLate, string? NoForecastReason, IReadOnlyList<GanttOrder> Orders);

/// <summary>Opção de filtro (projeto ou cliente).</summary>
public sealed record GanttOption(Guid Id, string Label);

/// <summary>
/// Cronograma por projeto (CA 1 a 4, 6, 7 e 9): hoje, o período, quando o plano foi calculado, os projetos no período (na ordem da fila) e
/// as opções dos filtros (de toda a fila, para o seletor não encolher com o filtro).
/// </summary>
public sealed record GanttProjectsView(
    DateOnly Today, DateOnly From, DateOnly To, DateTimeOffset? PlanComputedAt, IReadOnlyList<GanttProject> Projects, IReadOnlyList<GanttOption> ProjectOptions,
    IReadOnlyList<GanttOption> PartyOptions, IReadOnlyList<string> Lines, IReadOnlyList<GanttOption> Centers);

/// <summary>Barra de uma etapa alocada no centro (CA 5 e 8): OS, projeto, etapa, horas, datas previstas, início real e a sub-linha.</summary>
public sealed record GanttCenterBar(
    Guid StepId, string StepName, string? Code, Guid OrderId, string OrderNumber, Guid ProjectId, string? ProjectNumber, string? ProjectName, GanttStepState State,
    decimal StandardHours, decimal RemainingHours, decimal Progress, DateOnly PlannedStart, DateOnly PlannedEnd, DateOnly? ActualStart, int Lane);

/// <summary>
/// Dia do centro (CA 5): horas alocadas ÷ capacidade lida agora, nas faixas da PLN-04, e o feriado ou a parada do calendário com o nome. Só
/// vêm os dias com horas, sem capacidade (dia não útil) ou com exceção; os outros dias úteis estão livres.
/// </summary>
public sealed record GanttCenterDay(
    DateOnly Date, decimal AllocatedHours, decimal CapacityHours, decimal? Percent, FactoryLoadBand Band, CalendarExceptionType? ExceptionType, string? ExceptionName);

/// <summary>Linha do centro ativo (CA 5): as barras na ordem do tempo, quantas sub-linhas e os dias.</summary>
public sealed record GanttCenterRow(Guid WorkCenterId, string Name, string? Line, int Lanes, IReadOnlyList<GanttCenterBar> Bars, IReadOnlyList<GanttCenterDay> Days);

/// <summary>Cronograma por centro (CA 5): hoje, o período, quando o plano foi calculado, os centros, as linhas e quantos centros ativos há.</summary>
public sealed record GanttCentersView(
    DateOnly Today, DateOnly From, DateOnly To, DateTimeOffset? PlanComputedAt, IReadOnlyList<GanttCenterRow> Centers, IReadOnlyList<string> Lines, int ActiveCenters);
