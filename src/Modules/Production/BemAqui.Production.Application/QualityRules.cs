using BemAqui.Production.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Production.Application;

/// <summary>Quem pode decidir a qualidade (CA 5): livre, só com a marca "Autoinspeção" ou recusado.</summary>
public enum QualitySegregation
{
    Allowed,
    SelfInspection,
    Forbidden,
}

/// <summary>Etapa vista pelas regras da qualidade: situação e se é de qualidade.</summary>
public sealed record QualityStepState(ProductionStepStatus? Status, bool IsQualityStep);

/// <summary>Resultado da reprovação nas etapas (CA 6): quais voltam a esperar e qual fica pronta.</summary>
public sealed record RejectionPlan(int ReturnIndex, IReadOnlyList<int> BackToWaiting, IReadOnlyList<int> QualityStepsReset);

/// <summary>
/// Regras da qualidade sem banco (PRD-05): etapa decidível, etapas de retorno e a sugerida (CA 6), efeito da aprovação e da
/// reprovação nas etapas (CA 3, 6, 7, nunca <c>Completed</c>), segregação por tentativa (CA 5) e os textos dos diálogos de impacto.
/// </summary>
public static class QualityRules
{
    public const int MaxDescription = 1000;
    public const int MaxCause = 500;
    public const int MaxAction = 500;
    public const int MaxResult = 500;
    public const decimal MaxCost = 9_999_999_999_999_999.99m;
    public const string SelfInspectionLabel = "Autoinspeção";
    public const string SegregationMessage = "Quem fez uma etapa desta tentativa não aprova a qualidade dela.";

    /// <summary>A etapa de qualidade pode ser decidida: pronta, em inspeção ou pausada, numa OS não concluída nem cancelada.</summary>
    public static bool IsDecidable(ProductionOrderStatus order, ProductionStepStatus? step, bool isQualityStep) =>
        isQualityStep && order is not (ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled)
        && step is ProductionStepStatus.Ready or ProductionStepStatus.InProgress or ProductionStepStatus.Paused;

    /// <summary>
    /// Etapas de retorno (CA 6): as anteriores à de qualidade que não são de qualidade, da mais próxima para a mais distante; a
    /// sugerida é a produtiva imediatamente anterior.
    /// </summary>
    public static IReadOnlyList<int> ReturnOptions(IReadOnlyList<QualityStepState> steps, int qualityIndex)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var options = new List<int>();
        for (var i = qualityIndex - 1; i >= 0; i--)
        {
            if (!steps[i].IsQualityStep)
            {
                options.Add(i);
            }
        }

        return options;
    }

    /// <summary>Índice da etapa sugerida para o retorno, ou -1 sem opção.</summary>
    public static int SuggestedReturn(IReadOnlyList<QualityStepState> steps, int qualityIndex) =>
        ReturnOptions(steps, qualityIndex) is [var first, ..] ? first : -1;

    /// <summary>
    /// Reprovação (CA 6 e 7): a de retorno fica pronta, as seguintes até a de qualidade voltam a esperar (as de qualidade do meio
    /// também, com checklist zerado), e a de qualidade que reprovou fica <c>Rejected</c> até o retrabalho chegar de novo nela.
    /// </summary>
    public static RejectionPlan PlanRejection(IReadOnlyList<QualityStepState> steps, int qualityIndex, int returnIndex)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (qualityIndex < 0 || qualityIndex >= steps.Count || !steps[qualityIndex].IsQualityStep)
        {
            throw new ArgumentOutOfRangeException(nameof(qualityIndex));
        }

        if (!ReturnOptions(steps, qualityIndex).Contains(returnIndex))
        {
            throw new ArgumentOutOfRangeException(nameof(returnIndex));
        }

        var waiting = new List<int>();
        var reset = new List<int> { qualityIndex };
        for (var i = returnIndex + 1; i < qualityIndex; i++)
        {
            waiting.Add(i);
            if (steps[i].IsQualityStep)
            {
                reset.Add(i);
            }
        }

        return new RejectionPlan(returnIndex, waiting, reset);
    }

    /// <summary>Aplica a reprovação nas situações das etapas (sem banco; o serviço aplica nas entidades na mesma ordem).</summary>
    public static ProductionStepStatus?[] ApplyRejection(IReadOnlyList<QualityStepState> steps, int qualityIndex, int returnIndex)
    {
        var plan = PlanRejection(steps, qualityIndex, returnIndex);
        var result = steps.Select(s => s.Status).ToArray();
        result[plan.ReturnIndex] = ProductionStepStatus.Ready;
        foreach (var i in plan.BackToWaiting)
        {
            result[i] = ProductionStepStatus.Waiting;
        }

        result[qualityIndex] = ProductionStepStatus.Rejected;
        return result;
    }

    /// <summary>Aprovação (CA 3): a de qualidade vai a <c>Done</c> e a próxima fica pronta; é a última quando não há próxima.</summary>
    public static ProductionStepStatus?[] ApplyApproval(IReadOnlyList<QualityStepState> steps, int qualityIndex)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var result = steps.Select(s => s.Status).ToArray();
        result[qualityIndex] = ProductionStepStatus.Done;
        if (qualityIndex + 1 < result.Length && result[qualityIndex + 1] is ProductionStepStatus.Waiting or ProductionStepStatus.Rejected)
        {
            result[qualityIndex + 1] = ProductionStepStatus.Ready;
        }

        return result;
    }

    /// <summary>"Pronta para concluir" (D12, ponto 79): todas as etapas <c>Done</c>; a conclusão é explícita (PRD-06a1).</summary>
    public static bool ReadyToComplete(IEnumerable<ProductionStepStatus?> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var list = steps.ToList();
        return list.Count > 0 && list.All(s => s == ProductionStepStatus.Done);
    }

    /// <summary>
    /// Segregação por tentativa (CA 5, ponto 76): quem não apontou a tentativa decide; quem apontou só aprova sem outro aprovador
    /// elegível (usuário ativo com <c>production.approve_quality</c> fora dos envolvidos), e aí com a marca "Autoinspeção".
    /// </summary>
    public static QualitySegregation Segregation(Guid actor, IReadOnlyCollection<Guid> involved, IEnumerable<Guid> approvers)
    {
        ArgumentNullException.ThrowIfNull(involved);
        ArgumentNullException.ThrowIfNull(approvers);
        if (!involved.Contains(actor))
        {
            return QualitySegregation.Allowed;
        }

        return BemAqui.BuildingBlocks.Authorization.SoleOwnerRule.NoOtherEligible(approvers, actor, involved) ? QualitySegregation.SelfInspection : QualitySegregation.Forbidden;
    }

    /// <summary>Impacto da aprovação no diálogo (CA 3).</summary>
    public static string ApprovalImpact(string? nextStepName) =>
        nextStepName is null ? "A OS fica pronta para concluir." : $"A OS vai para {nextStepName}.";

    /// <summary>Impacto da reprovação no diálogo (CA 6): "Volta para {etapa}; {n} etapas voltam a esperar".</summary>
    public static string RejectionImpact(string returnStepName, int waiting) => waiting switch
    {
        0 => $"Volta para {returnStepName}.",
        1 => $"Volta para {returnStepName}; 1 etapa volta a esperar.",
        _ => $"Volta para {returnStepName}; {waiting} etapas voltam a esperar.",
    };

    /// <summary>Rascunho da descrição pelos itens "Não conforme" (propositivo).</summary>
    public static string DraftDescription(IEnumerable<string> nonConforming)
    {
        ArgumentNullException.ThrowIfNull(nonConforming);
        var items = nonConforming.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        return items.Count == 0 ? string.Empty : "Não conforme: " + string.Join("; ", items) + ".";
    }

    /// <summary>Descrição curta da notificação (até 60 caracteres).</summary>
    public static string Excerpt(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= 60 ? line : line[..57].TrimEnd() + "...";
    }

    /// <summary>Idade da NC em dias (lista, CA 11), pelo dia de São Paulo; a encerrada para no encerramento.</summary>
    public static int AgeDays(DateOnly openedOn, DateOnly? closedOn, DateOnly today) => Math.Max((closedOn ?? today).DayNumber - openedOn.DayNumber, 0);

    /// <summary>Texto obrigatório com limite: devolve o texto limpo ou o erro de validação do campo.</summary>
    public static Result<string?> Text(string? value, string field, int max, string label, bool required)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return required ? LaborRules.Invalid(field, $"Informe {label}.") : (string?)null;
        }

        return text.Length > max ? LaborRules.Invalid(field, $"{Capital(label)} tem até {max} caracteres.") : text;
    }

    private static string Capital(string label) => label.Length == 0 ? label : char.ToUpperInvariant(label[0]) + label[1..];
}