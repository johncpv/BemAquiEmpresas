using BemAqui.Commercial.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Production.Application;

/// <summary>O que acontece com o item do projeto no consumidor do <c>ProjectItemCreated</c> (PRD-01 CA 1 a 3, D14).</summary>
public enum FabricationDecision
{
    /// <summary>Equipamento de contrato que não é de implantação: a OS nasce sozinha.</summary>
    Generate,

    /// <summary>Componente fabricado ou mensagem antiga sem o tipo: fica para o "Gerar OS" manual.</summary>
    Manual,

    /// <summary>Serviço ou contrato de implantação: nunca gera OS.</summary>
    Never,
}

/// <summary>Etapa já normalizada para gravar na OS.</summary>
public sealed record FabricationStepDraft(
    Guid? Id, int Sequence, string Name, string Code, Guid? WorkCenterId, decimal StandardHours, bool IsQualityStep, bool RequiresApproval, IReadOnlyList<string> Checklist);

/// <summary>
/// Regras da OS de fabricação sem banco (PRD-01): quais itens geram OS (D14), a ordem da sugestão do roteiro (CA 4), a máquina de
/// estados que decide se a OS já começou (CA 8, 17) e a validação das etapas, com as mesmas regras do roteiro (ENG-01 CA 5).
/// </summary>
public static class FabricationRules
{
    public const int MaxSteps = 30;
    public const int MaxStepName = 60;
    public const int MaxChecklist = 30;
    public const int MaxChecklistText = 120;
    public const decimal MaxHours = 99_999m;

    private static readonly ProductionStepStatus[] StartedSteps = [ProductionStepStatus.InProgress, ProductionStepStatus.Paused, ProductionStepStatus.Done, ProductionStepStatus.Rejected];

    /// <summary>Decisão automática pelo evento (CA 1 a 3): sem o tipo (mensagem antiga), nunca automática.</summary>
    public static FabricationDecision FromEvent(ProjectItemKind? kind, bool? isOnboarding) => (kind, isOnboarding) switch
    {
        (null, _) => FabricationDecision.Manual,
        (ProjectItemKind.Service, _) => FabricationDecision.Never,
        (_, true) => FabricationDecision.Never,
        (ProjectItemKind.Equipment, false) => FabricationDecision.Generate,
        _ => FabricationDecision.Manual,
    };

    /// <summary>Recusa do "Gerar OS" manual (CA 2 e 6); null quando pode gerar.</summary>
    public static Error? CanGenerate(ProjectItemInfo item, ProjectSummary project)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(project);
        if (item.IsOnboarding)
        {
            return Error.Validation("onboarding_no_fabrication", "Contrato de implantação não gera OS: a fabricação aconteceu antes do sistema.");
        }

        if (item.Kind == ProjectItemKind.Service)
        {
            return Error.Validation("item_no_fabrication", "Este item não gera OS de fabricação.");
        }

        return project.Status switch
        {
            "Planned" or "InProgress" => null,
            "Cancelled" => Error.Conflict("project_cancelled", $"O projeto {project.Number} foi cancelado: não gera OS nova."),
            _ => Error.Conflict("project_already_fabricated", $"O projeto {project.Number} já foi fabricado: não gera OS nova."),
        };
    }

    /// <summary>
    /// Roteiro sugerido (CA 4, R-PRP-1), só entre os ativos: o do item do projeto, o vinculado ao produto, o último usado com o
    /// mesmo produto e, havendo um único roteiro ativo, ele. Sem nenhum, a OS nasce sem etapas.
    /// </summary>
    public static Guid? PickRouting(Guid? itemRouting, Guid? productRouting, Guid? lastUsedWithProduct, IReadOnlyCollection<Guid> active)
    {
        ArgumentNullException.ThrowIfNull(active);
        foreach (var candidate in new[] { itemRouting, productRouting, lastUsedWithProduct })
        {
            if (candidate is { } id && active.Contains(id))
            {
                return id;
            }
        }

        return active.Count == 1 ? active.First() : null;
    }

    /// <summary>A OS começou (CA 8, 17): saiu de Aberta ou alguma etapa já foi iniciada.</summary>
    public static bool HasStarted(ProductionOrderStatus status, IEnumerable<ProductionStepStatus?> steps) =>
        status != ProductionOrderStatus.Open || steps.Any(s => s is { } st && StartedSteps.Contains(st));

    /// <summary>Situação das etapas de uma OS que não começou (CA 7): a primeira pronta, as demais aguardando.</summary>
    public static ProductionStepStatus InitialStatus(int index) => index == 0 ? ProductionStepStatus.Ready : ProductionStepStatus.Waiting;

    /// <summary>Etapa atual: a primeira que não terminou; com todas terminadas, a última; sem etapas, -1.</summary>
    public static int CurrentIndex(IReadOnlyList<ProductionStepStatus?> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0)
        {
            return -1;
        }

        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i] != ProductionStepStatus.Done)
            {
                return i;
            }
        }

        return steps.Count - 1;
    }

    /// <summary>
    /// Etapas da OS (CA 7) com as regras do roteiro (ENG-01 CA 5): de 1 a 30, nome, letra de A a Z sem repetir, centro, horas
    /// entre 0 e 99.999, primeira sem qualidade, nunca duas de qualidade seguidas e checklist de até 30 itens de 120 caracteres.
    /// </summary>
    public static (IReadOnlyList<FabricationStepDraft> Steps, Dictionary<string, string[]> Errors) Validate(IReadOnlyList<FabricationStepInput>? steps)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Add(string field, string message) => errors.TryAdd(field, [message]);
        var list = steps ?? [];
        if (list.Count == 0)
        {
            Add("steps", "Inclua ao menos uma etapa.");
        }
        else if (list.Count > MaxSteps)
        {
            Add("steps", $"Inclua no máximo {MaxSteps} etapas.");
        }

        var normalized = new List<FabricationStepDraft>();
        var codes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < list.Count && i < MaxSteps; i++)
        {
            var s = list[i];
            var field = $"steps[{i}]";
            var name = (s.Name ?? string.Empty).Trim();
            if (name.Length == 0 || name.Length > MaxStepName)
            {
                Add(field + ".name", $"Informe o nome da etapa, com até {MaxStepName} caracteres.");
            }

            var code = (s.Code ?? string.Empty).Trim().ToUpperInvariant();
            if (!(code.Length == 1 && code[0] is >= 'A' and <= 'Z'))
            {
                Add(field + ".code", "Informe a letra da etapa, de A a Z.");
            }
            else if (codes.TryGetValue(code, out var first))
            {
                Add(field + ".code", $"A letra {code} já está na etapa {first + 1}.");
            }
            else
            {
                codes[code] = i;
            }

            if (s.WorkCenterId is null)
            {
                Add(field + ".workCenterId", "Escolha o centro de trabalho da etapa.");
            }

            if (s.StandardHours < 0 || s.StandardHours > MaxHours)
            {
                Add(field + ".standardHours", "Informe as horas padrão entre 0 e 99.999.");
            }

            if (s.IsQualityStep && i == 0)
            {
                Add(field + ".isQualityStep", "A primeira etapa não pode ser de qualidade.");
            }
            else if (s.IsQualityStep && i > 0 && list[i - 1].IsQualityStep)
            {
                Add(field + ".isQualityStep", "Duas etapas de qualidade não podem ficar seguidas.");
            }

            var checklist = (s.Checklist ?? []).Select(t => (t ?? string.Empty).Trim()).Where(t => t.Length > 0).ToList();
            if (checklist.Count > MaxChecklist)
            {
                Add(field + ".checklist", $"O checklist da etapa tem até {MaxChecklist} itens.");
            }

            var tooLong = checklist.FindIndex(t => t.Length > MaxChecklistText);
            if (tooLong >= 0)
            {
                Add($"{field}.checklist[{tooLong}]", $"Cada item do checklist tem até {MaxChecklistText} caracteres.");
            }

            normalized.Add(new FabricationStepDraft(s.Id, i + 1, name, code, s.WorkCenterId, Math.Round(s.StandardHours, 4, MidpointRounding.AwayFromZero), s.IsQualityStep,
                s.RequiresApproval, checklist));
        }

        return (normalized, errors);
    }


    public static Error? ToError(Dictionary<string, string[]> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return errors.Count == 0 ? null : Error.Validation("validation", errors.First().Value[0], errors);
    }
}