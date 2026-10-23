using BemAqui.Production.Domain;

namespace BemAqui.Production.Application;

/// <summary>"Concluir OS" (PRD-06a1 CA 2 e 3): só a versão; a OS conclui com a quantidade inteira (sem campo de quantidade, CA 5).</summary>
public sealed record CompleteOrderInput(uint Version);

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
    IReadOnlyList<CompletionMilestoneView> Milestones, string StockNotice, uint Version);

/// <summary>Etapa vista pela regra da conclusão: nome, situação, se é de qualidade e se espera aprovação.</summary>
public sealed record CompletionStep(string Name, ProductionStepStatus? Status, bool IsQualityStep, bool AwaitingApproval = false);

/// <summary>Regras da conclusão da OS de fabricação sem banco (PRD-06a1 CA 1, 3, 4, 5 e 10).</summary>
public static class CompletionRules
{
    public const int MaxReversalReason = 500;
    public const string StockNotice = "O produto acabado ainda não entra no estoque nesta versão.";
    public const string StockDica = "A entrada do produto acabado no estoque, com as séries e os lotes, chega numa próxima versão. Até lá, a OS concluída só libera a parcela e muda o projeto.";
    public const string ReadyDica = "Todas as etapas e a qualidade foram feitas. Falta o supervisor concluir a OS.";

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