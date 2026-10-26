using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Production.Contracts;

// Contratos de leitura do painel do supervisor (DSH-03, ADR 0028 §1): moram no Production, dono do quadro, da qualidade e das pausas;
// o módulo Dashboard só compõe. Nenhum leva custo (ADR 0028 §2). A "linha" é a do centro de trabalho (a mesma do filtro do Kanban).

/// <summary>Coluna do quadro compacto (PRD-02 CA 1): a etapa pela letra, quantas OS, as horas restantes e quantas estão atrasadas e em risco.</summary>
public sealed record BoardColumnSummary(string Key, string? Code, string Name, int Count, decimal RemainingHours, int LateCount, int AtRiskCount);

/// <summary>Cartão atrasado ou em risco do quadro compacto: a OS, o produto, o projeto e o cliente, a etapa atual e o prazo.</summary>
public sealed record BoardHighlightCard(
    Guid OrderId, string Number, string ProductName, Guid ProjectId, string ProjectNumber, string PartyName, string ColumnKey, string? StepCode, string? StepName,
    IReadOnlyList<string> Codes, IReadOnlyList<string> StepNames, int CurrentStepIndex, DateOnly? PromisedDate, DeliveryRisk Risk, int? DelayDays);

/// <summary>Quadro compacto (DSH-03 CA 2): colunas com as contagens, só os cartões atrasados e em risco, e os contadores do Kanban.</summary>
public sealed record ProductionBoardSummary(IReadOnlyList<BoardColumnSummary> Columns, IReadOnlyList<BoardHighlightCard> Highlighted, int LateCount, int AtRiskCount, int OpenCount);

/// <summary>
/// Quadro da produção para o painel do supervisor (DSH-03 CA 2): o mesmo cálculo do Kanban (PRD-02), com o filtro de linha, e a linha de
/// cada OS (a do centro da etapa atual; na "Pronta para concluir", a da última etapa).
/// </summary>
public interface IProductionBoard
{
    Task<ProductionBoardSummary> SummaryAsync(string? line, CancellationToken cancellationToken = default);

    /// <summary>Linha de cada OS pedida (nula sem centro ou sem linha); as que não existem ficam fora.</summary>
    Task<IReadOnlyDictionary<Guid, string?>> LinesOfOrdersAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default);
}

/// <summary>Etapa de qualidade pronta para decidir (PRD-05 CA 2) com a segregação do item 76 já aplicada para o usuário do request.</summary>
public sealed record QualityPendingStep(
    Guid StepId, Guid OrderId, string OrderNumber, string StepCode, string StepName, string ProductName, string ProjectNumber, DateOnly? PromisedDate, int Attempt,
    DateTimeOffset? WaitingSince, bool SelfInspection, int OpenNonConformities);

/// <summary>Fila de qualidade: as etapas que o usuário pode decidir (vazia quando não pedidas) e quantas aguardam a qualidade no total.</summary>
public sealed record QualityPendingSummary(IReadOnlyList<QualityPendingStep> Decidable, int TotalCount);

/// <summary>Situação da NC para os outros módulos (PRD-05 CA 9).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NonConformityState>))]
public enum NonConformityState
{
    Open,
    InAction,
    Closed,
}

/// <summary>NC não encerrada (PRD-05 CA 11): número, OS, etapa, responsável, situação e dias aberta. Sem custo.</summary>
public sealed record OpenNonConformity(
    Guid Id, string Number, Guid OrderId, string OrderNumber, string StepCode, string StepName, Guid ResponsibleUserId, string ResponsibleName, NonConformityState Status,
    int AgeDays, DateTimeOffset OpenedAt);

public sealed record OpenNonConformities(IReadOnlyList<OpenNonConformity> Items, int Total);

/// <summary>Qualidade para o painel do supervisor (DSH-03 CA 4): a fila da PRD-05 e as NC abertas, com o filtro de linha.</summary>
public interface IQualityOverview
{
    /// <summary>
    /// Etapas de qualidade aguardando decisão na linha; com <paramref name="withItems"/>, também as que o usuário do request pode decidir (sem
    /// as OS em que ele apontou etapa da tentativa, salvo a "Autoinspeção"), da que espera há mais tempo para a mais nova.
    /// </summary>
    Task<QualityPendingSummary> PendingAsync(string? line, bool withItems, CancellationToken cancellationToken = default);

    /// <summary>NC abertas ou em ação na linha, da mais antiga para a mais nova, até <paramref name="limit"/>, e o total.</summary>
    Task<OpenNonConformities> OpenNonConformitiesAsync(string? line, int limit, CancellationToken cancellationToken = default);
}

/// <summary>Motivo de parada do indicador (PRD-03a): só os que param a produção; intervalo e fim de turno ficam fora.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StoppageReason>))]
public enum StoppageReason
{
    Material,
    Machine,
    Other,
}

/// <summary>Horas paradas de uma OS no motivo (o drill-down até a aba Apontamentos da OS).</summary>
public sealed record StoppageOrderHours(Guid OrderId, string OrderNumber, decimal Hours);

/// <summary>Horas paradas por motivo no período: Σ minutos de pausa ÷ 60, quantas pausas e as OS que mais pararam.</summary>
public sealed record StoppageByReason(StoppageReason Reason, decimal Hours, int Pauses, IReadOnlyList<StoppageOrderHours> Orders);

/// <summary>Taxa de NC da etapa de qualidade (pela letra): reprovações ÷ (aprovações + reprovações) no período, em %, 1 casa; nula sem decisão.</summary>
public sealed record QualityRateByStep(string StepCode, string StepName, int Decisions, int Rejections, decimal? RatePercent);

public sealed record ProductionIndicatorsSummary(DateOnly From, DateOnly To, IReadOnlyList<StoppageByReason> Stoppages, IReadOnlyList<QualityRateByStep> QualityRates);

/// <summary>Indicadores da produção (DSH-03 CA 6, ponto 51): horas paradas por motivo (<c>step_pauses</c>) e a taxa de NC por etapa de qualidade.</summary>
public interface IProductionIndicators
{
    /// <summary>De <paramref name="from"/> a <paramref name="to"/>, os dois inclusive (datas de negócio), na linha (a do centro da etapa).</summary>
    Task<ProductionIndicatorsSummary> SummaryAsync(DateOnly from, DateOnly to, string? line, CancellationToken cancellationToken = default);
}
