using System.Text.Json.Serialization;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Dashboard.Application;

// DTOs dos blocos do painel do supervisor (DSH-03): só o que a permissão do bloco deixa ver, nenhum com custo (CA 9, ADR 0028 §2); o teste
// de arquitetura confere os nomes das propriedades.

/// <summary>
/// Cabeçalho do painel do supervisor (CA 1): hoje, as linhas para o filtro (dos centros ativos), os blocos que o usuário vê na ordem da tela e o
/// que ele pode fazer em cada um.
/// </summary>
public sealed record SupervisorOverview(
    DateOnly Today, IReadOnlyList<string> Lines, IReadOnlyList<string> Blocks, bool CanDecideQuality, bool CanCreatePurchaseRequest, bool CanViewPurchasing);

public sealed record SupervisorBoardColumnItem(string Key, string? Code, string Name, int Count, decimal RemainingHours, int LateCount, int AtRiskCount);

public sealed record SupervisorBoardCardItem(
    Guid OrderId, string Number, string ProductName, Guid ProjectId, string ProjectNumber, string PartyName, string ColumnKey, string? StepCode, string? StepName,
    IReadOnlyList<string> Codes, IReadOnlyList<string> StepNames, int CurrentStepIndex, DateOnly? PromisedDate, DeliveryRisk Risk, int? DelayDays);

/// <summary>Quadro compacto (CA 2): as colunas com contagem e horas restantes e só os cartões atrasados e em risco (até <see cref="SupervisorBlocks.BoardCardRows"/>).</summary>
public sealed record SupervisorBoardBlock(
    int OpenCount, int LateCount, int AtRiskCount, IReadOnlyList<SupervisorBoardColumnItem> Columns, IReadOnlyList<SupervisorBoardCardItem> Cards, DateTimeOffset UpdatedAt);

public sealed record SupervisorCapacityWeekItem(DateOnly Start, DateOnly End, int IsoWeek);

public sealed record SupervisorCapacityCellItem(DateOnly WeekStart, decimal CommittedHours, decimal CapacityHours, decimal? Percent, FactoryLoadBand Band);

public sealed record SupervisorCapacityRowItem(Guid? WorkCenterId, string Name, string? Line, IReadOnlyList<SupervisorCapacityCellItem> Cells, DateOnly? BusyUntil);

/// <summary>Capacidade (CA 3): a semana atual e as próximas 4 por centro e a linha "Fábrica", pelo <c>IFactoryLoad</c> da PLN-04.</summary>
public sealed record SupervisorCapacityBlock(
    DateOnly Today, DateTimeOffset? PlanComputedAt, IReadOnlyList<SupervisorCapacityWeekItem> Weeks, IReadOnlyList<SupervisorCapacityRowItem> Centers,
    SupervisorCapacityRowItem Factory, DateTimeOffset UpdatedAt);

public sealed record SupervisorQualityStepItem(
    Guid StepId, Guid OrderId, string OrderNumber, string StepCode, string StepName, string ProductName, string ProjectNumber, DateOnly? PromisedDate, int Attempt,
    DateTimeOffset? WaitingSince, bool SelfInspection, int OpenNonConformities);

public sealed record SupervisorNonConformityItem(
    Guid Id, string Number, Guid OrderId, string OrderNumber, string StepCode, string StepName, string ResponsibleName, NonConformityState Status, int AgeDays);

/// <summary>
/// Qualidade (CA 4): com <c>production.approve_quality</c>, as etapas que o usuário pode decidir; sem ela, só <see cref="PendingCount"/>. As NC
/// abertas (até <see cref="SupervisorBlocks.NonConformityRows"/>, da mais antiga) aparecem para todos.
/// </summary>
public sealed record SupervisorQualityBlock(
    bool CanDecide, int PendingCount, IReadOnlyList<SupervisorQualityStepItem> Steps, int OpenNonConformities, IReadOnlyList<SupervisorNonConformityItem> NonConformities,
    DateTimeOffset UpdatedAt);

/// <summary>Situação da compra da falta (CA 5), lida do Purchasing: sem solicitação, solicitada, em cotação ou com pedido.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PurchaseSituation>))]
public enum PurchaseSituation
{
    None,
    Requested,
    Quoting,
    Ordered,
}

/// <summary>Falta de material de uma OS: item, OS, projeto, quanto falta, quando é necessária, se bloqueia e em quantos dias úteis, e a compra.</summary>
public sealed record SupervisorShortageItem(
    Guid OrderId, string OrderNumber, Guid ProjectId, string ProjectNumber, Guid ItemId, string ItemCode, string ItemName, string Unit, decimal ShortQty, DateOnly? NeededOn,
    bool Blocks, int? BlocksInDays, PurchaseSituation Purchase, string PurchaseLabel, Guid? PurchaseRequestId, Guid? PurchaseOrderId);

/// <summary>Faltas de material (CA 5): as que bloqueiam primeiro, até <see cref="SupervisorBlocks.ShortageRows"/> linhas, com o total e quantas bloqueiam.</summary>
public sealed record SupervisorShortagesBlock(int Total, int Blocking, IReadOnlyList<SupervisorShortageItem> Items, bool CanCreateRequest, DateTimeOffset UpdatedAt);

public sealed record SupervisorStoppageOrderItem(Guid OrderId, string OrderNumber, decimal Hours);

public sealed record SupervisorStoppageItem(StoppageReason Reason, string Label, decimal Hours, int Pauses, IReadOnlyList<SupervisorStoppageOrderItem> Orders);

public sealed record SupervisorQualityRateItem(string StepCode, string StepName, int Decisions, int Rejections, decimal? RatePercent);

/// <summary>Indicadores (CA 6): nos últimos 30 dias, horas paradas por motivo e a taxa de NC por etapa de qualidade.</summary>
public sealed record SupervisorIndicatorsBlock(
    DateOnly From, DateOnly To, decimal StoppedHours, IReadOnlyList<SupervisorStoppageItem> Stoppages, IReadOnlyList<SupervisorQualityRateItem> QualityRates, DateTimeOffset UpdatedAt);
