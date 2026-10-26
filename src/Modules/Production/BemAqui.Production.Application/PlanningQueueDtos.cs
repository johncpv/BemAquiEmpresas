using BemAqui.Production.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Production.Application;

/// <summary>
/// Linha da fila (PLN-02 CA 1): posição, projeto, cliente, OS de fabricação abertas, horas restantes (do plano), prometido, previsto
/// (PLN-01b), folga em dias e o risco pela função única; sem previsão, o motivo (código) e a OS que impede a data. Sem custo.
/// </summary>
public sealed record PlanningQueueRow(
    int Position, Guid ProjectId, string ProjectNumber, string ProjectName, Guid? PartyId, string? PartyName, int OpenOrders, decimal RemainingHours,
    DateOnly? PromisedDate, DateOnly? ForecastDate, int? SlackDays, DeliveryRisk Risk, bool HasForecast, string? NoForecastReason, Guid? ReasonOrderId, string? ReasonOrderNumber);

/// <summary>A fila com a versão (devolvida no "Salvar ordem") e quem reordenou por último.</summary>
public sealed record PlanningQueueView(uint Version, DateTimeOffset? ReorderedAt, string? ReorderedBy, IReadOnlyList<PlanningQueueRow> Rows);

/// <summary>Ordem proposta para a prévia (CA 4): os ids de todos os projetos da fila, na ordem nova.</summary>
public sealed record PlanningQueuePreviewInput(IReadOnlyList<Guid>? Order);

/// <summary>"Salvar ordem" (CA 6): a ordem nova e a versão da fila de quando a tela abriu.</summary>
public sealed record PlanningQueueSaveInput(IReadOnlyList<Guid>? Order, uint Version);

/// <summary>
/// Prévia de um projeto (CA 4 e 5): posição, previsto e folga antes → depois, risco antes → depois e se passa a atrasar ou piora
/// (<c>Worsened</c>, com quantos dias depois do prometido).
/// </summary>
public sealed record PlanningQueuePreviewRow(
    Guid ProjectId, int PositionBefore, int PositionAfter, DateOnly? PromisedDate, DateOnly? ForecastBefore, DateOnly? ForecastAfter, int? SlackBefore, int? SlackAfter,
    DeliveryRisk RiskBefore, DeliveryRisk RiskAfter, bool Worsened, int? DaysLate);

/// <summary>Resultado da prévia, calculada em memória (nada é gravado), e quanto levou.</summary>
public sealed record PlanningQueuePreview(IReadOnlyList<PlanningQueuePreviewRow> Rows, int DurationMs);

/// <summary>"Ordem salva" (CA 6): a fila gravada, se mudou e se o recálculo foi pedido.</summary>
public sealed record PlanningQueueSaved(bool Changed, bool RecalculationRequested, PlanningQueueView Queue);

/// <summary>Posição antes → depois de um projeto na mudança (nulo = entrou ou saiu).</summary>
public sealed record PlanningQueueMoveView(Guid ProjectId, string ProjectNumber, int? From, int? To);

/// <summary>Mudança na fila (CA 8): quando, quem (ou "Sistema"), o tipo, o motivo e as posições antes → depois.</summary>
public sealed record PlanningQueueChangeView(Guid Id, DateTimeOffset At, string Who, PlanningQueueChangeKind Kind, string Reason, IReadOnlyList<PlanningQueueMoveView> Moves);
