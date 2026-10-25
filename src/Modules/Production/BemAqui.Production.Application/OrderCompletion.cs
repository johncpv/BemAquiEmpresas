using BemAqui.Production.Domain;

namespace BemAqui.Production.Application;

/// <summary>
/// "Concluir OS" (PRD-06a1 CA 2 e 3): a versão; a OS conclui com a quantidade inteira (sem campo de quantidade, CA 5). PRD-06a2: o local
/// de entrada (nulo = o sugerido), as séries editadas (nulo = as geradas, D15) e a confirmação explícita do custo zerado (CA 7).
/// </summary>
public sealed record CompleteOrderInput(uint Version, Guid? LocationId = null, IReadOnlyList<string>? SerialNumbers = null, bool ConfirmZeroCost = false);

/// <summary>"Escolher item do produto" (PRD-06a2 CA 1): o item de estoque que entra na conclusão.</summary>
public sealed record OrderProductInput(Guid ItemId, uint Version);

/// <summary>Item que pode ser o produto da OS (CA 1): produto acabado ou componente ativo, com o controle de série.</summary>
public sealed record OrderProductOption(Guid Id, string Code, string Name, string Type, bool TracksSerial);

/// <summary>Local de entrada do produto acabado (CA 2): os ativos, o padrão primeiro.</summary>
public sealed record CompletionLocationOption(Guid Id, string Code, string Name, bool IsDefault);

/// <summary>Reserva da OS que a conclusão libera (CA 2 e 5, ADR 0023 §6): item, local e quanto ainda estava reservado.</summary>
public sealed record CompletionReservationView(Guid ItemId, string ItemCode, string ItemName, string UnitCode, string LocationName, decimal Quantity);

/// <summary>
/// Impacto no estoque de "Concluir OS" e de "Estornar conclusão" (PRD-06a2 CA 2, 3, 4, 7 e 10). <c>Enabled</c> falso: o módulo Estoque
/// não está liberado no tenant e a OS conclui sem estoque. Sem custo (CA 14): o custo apurado vem em DTO separado,
/// <see cref="OrderCost"/>, com <c>projects.view_cost</c>; <c>ZeroCost</c> vale para todos, porque pede a confirmação explícita (CA 7). No
/// estorno: o movimento de entrada, as séries a cancelar e as já expedidas (bloqueiam o estorno).
/// </summary>
public sealed record CompletionStockView(
    bool Enabled, Guid? ProductId, string? ProductCode, string? ProductName, string? UnitCode, decimal Quantity, bool TracksSerial, bool TracksLot,
    IReadOnlyList<string> SuggestedSerials, Guid? SuggestedLocationId, IReadOnlyList<CompletionLocationOption> Locations, IReadOnlyList<CompletionReservationView> ReservationsToRelease,
    bool ZeroCost, Guid? EntryMovementId, string? EntryMovementNumber, int SerialsToCancel, int ShippedSerials, bool CompletedWithoutStock);

/// <summary>
/// Custo apurado da OS na conclusão (CA 2 e 3): horas da Produção + material baixado − devolvido (Inventory), e o unitário em 6 casas.
/// Lido com <c>projects.view_cost</c> (<c>GET …/completion/cost</c>); a entrada usa o mesmo cálculo, sem depender da permissão.
/// </summary>
public sealed record OrderCost(decimal Labor, decimal Material, decimal Total, decimal Unit);

/// <summary>"Estornar conclusão" (CA 10): motivo obrigatório, até 500.</summary>
public sealed record ReverseCompletionInput(string? Reason, uint Version);

/// <summary>
/// Parcela "fabricação concluída" no diálogo de impacto (CA 2 e 10): o marco sempre; valor, vencimento (data de hoje + prazo) e o título só
/// com <c>finance.view</c>. <c>Released</c>: o título já está em aberto; <c>Received</c>: já tem baixa.
/// </summary>
public sealed record CompletionMilestoneView(string Description, decimal? Amount, DateOnly? DueDate, Guid? ReceivableId, bool Released, bool Received);

/// <summary>
/// Impacto de "Concluir OS" e de "Estornar conclusão" (CA 2, 4, 10 e 11), lido ao abrir o diálogo: o que impede cada ação (o mesmo texto do
/// 409), se é a última OS de fabricação não cancelada do projeto, a situação do projeto e as parcelas do marco.
/// </summary>
public sealed record OrderCompletionPreview(
    Guid OrderId, string OrderNumber, ProductionOrderStatus Status, bool CanComplete, string? CompleteBlocker, bool CanReverse, string? ReverseBlocker,
    bool LastFabricationOrder, int OtherOpenOrders, Guid ProjectId, string ProjectNumber, string ProjectStatus, bool ProjectFabricatedByOrders, bool ShowAmounts,
    IReadOnlyList<CompletionMilestoneView> Milestones, string? StockNotice, uint Version, CompletionStockView? Stock = null);

/// <summary>Etapa vista pela regra da conclusão: nome, situação, se é de qualidade e se espera aprovação.</summary>
public sealed record CompletionStep(string Name, ProductionStepStatus? Status, bool IsQualityStep, bool AwaitingApproval = false);

/// <summary>Regras da conclusão da OS de fabricação sem banco (PRD-06a1 CA 1, 3, 4, 5 e 10).</summary>
public static class CompletionRules
{
    public const int MaxReversalReason = 500;

    /// <summary>Aviso do diálogo com o módulo Estoque não liberado no tenant: a OS conclui sem estoque (como na PRD-06a1).</summary>
    public const string StockNotice = "O Estoque não está liberado nesta versão do sistema: o produto acabado não entra no estoque.";
    public const string ReadyDica = "Todas as etapas e a qualidade foram feitas. Falta o supervisor concluir a OS.";
    public const string ProductRequired = "Escolha o item de estoque do produto antes de concluir.";
    public const string ZeroCostWarning = "Custo da OS zerado: o produto entra no estoque com custo 0.";
    public const string CompletedMessage = "OS concluída. Produto acabado no estoque.";
    public const string ProductShipped = "O produto já foi expedido.";
    public const string CompletedBeforeStock ="Concluída antes do controle de estoque: o produto não entrou no estoque.";

    /// <summary>PRD-06b: o produto da OS está num romaneio aberto (rascunho ou aguardando o estoque).</summary>
    public static string InShipment(string shipmentNumber) => $"O produto está no romaneio {shipmentNumber}. Cancele o romaneio antes de estornar a conclusão.";

    /// <summary>
    /// Custo apurado da OS (CA 3, ADR 0025 §2): horas apontadas + material baixado − devolvido; o unitário é o total ÷ quantidade em 6 casas
    /// <c>AwayFromZero</c> (o total do movimento fica em 2 casas, no motor).
    /// </summary>
    public static OrderCost Cost(decimal labor, decimal material, decimal quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "A OS conclui com quantidade positiva.");
        }

        var total = labor + material;
        return new OrderCost(labor, material, total, Math.Round(total / quantity, 6, MidpointRounding.AwayFromZero));
    }

    /// <summary>
    /// Perna da entrada do produto acabado na chave de idempotência <c>(ProductionOrder, osId, perna, Production)</c> (ADR 0023 §4): 1 na
    /// primeira conclusão; depois de um estorno, a próxima; sem estorno, a mesma (a segunda chamada devolve o mesmo movimento).
    /// </summary>
    public static int EntryLeg(int? lastLeg, bool lastReversed) => lastLeg is not { } leg ? 1 : lastReversed ? leg + 1 : leg;

    /// <summary>
    /// O que impede concluir (CA 4), na ordem: cancelada, já concluída, sem etapas, apontamento aberto, etapa de qualidade pendente
    /// ("Falta aprovar a Qualidade"), etapa aguardando aprovação e etapa pendente. Null: pode concluir (CA 1, "Pronta para concluir").
    /// </summary>
    public static string? Blocker(ProductionOrderStatus status, IReadOnlyList<CompletionStep> steps, string? openLaborStep)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (status == ProductionOrderStatus.Cancelled)
        {
            return "A OS foi cancelada.";
        }

        if (status == ProductionOrderStatus.Completed)
        {
            return "A OS já foi concluída.";
        }

        if (steps.Count == 0)
        {
            return "A OS não tem etapas. Escolha o roteiro.";
        }

        if (openLaborStep is not null)
        {
            return $"Há apontamento aberto na etapa {openLaborStep}.";
        }

        return steps.FirstOrDefault(s => s.Status != ProductionStepStatus.Done) switch
        {
            null => null,
            { IsQualityStep: true } => "Falta aprovar a Qualidade.",
            { AwaitingApproval: true } pending => $"Falta aprovar a etapa {pending.Name}.",
            var pending => $"Falta concluir a etapa {pending.Name}.",
        };
    }

    /// <summary>
    /// Última OS de fabricação do projeto (CA 2 e 7): todas as outras não canceladas (fora a de instalação, que nem entra) já concluídas.
    /// </summary>
    public static bool IsLastOrder(Guid orderId, IEnumerable<(Guid Id, ProductionOrderStatus Status)> projectOrders)
    {
        ArgumentNullException.ThrowIfNull(projectOrders);
        return projectOrders.Where(o => o.Id != orderId && o.Status != ProductionOrderStatus.Cancelled).All(o => o.Status == ProductionOrderStatus.Completed);
    }

    /// <summary>Motivo do estorno (CA 10): obrigatório, até 500.</summary>
    public static string? ReversalReason(string? reason)
    {
        var text = reason?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > MaxReversalReason ? null : text;
    }

    /// <summary>
    /// Estorno da conclusão nas etapas (CA 10): a última etapa volta a <c>Ready</c> (sem fim real nem quem concluiu) e a OS sai de
    /// <c>Completed</c>; a situação vem da regra única (<see cref="LaborRules.OrderStatus"/>): <c>InProgress</c>, ou <c>Quality</c> quando a
    /// última etapa é de qualidade.
    /// </summary>
    public static ProductionOrderStatus ReversedStatus(IReadOnlyList<StepState> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0)
        {
            return ProductionOrderStatus.InProgress;
        }

        var reopened = steps.Take(steps.Count - 1).Append(steps[^1] with { Status = ProductionStepStatus.Ready }).ToList();
        return LaborRules.OrderStatus(ProductionOrderStatus.InProgress, reopened);
    }
}