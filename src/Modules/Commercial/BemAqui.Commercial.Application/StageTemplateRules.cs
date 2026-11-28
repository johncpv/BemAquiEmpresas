using BemAqui.BuildingBlocks.Modules;
using BemAqui.Commercial.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Application;

/// <summary>Etapa do modelo já normalizada (nome e letra aparados, letra maiúscula, horas em 4 casas, checklist sem linhas vazias).</summary>
public sealed record NormalizedStageStep(Guid? Id, int Sequence, string Name, string Code, StageRole Role, decimal StandardHoursPerUnit, Guid? PositionId, bool RequiresApproval, IReadOnlyList<string> Checklist);

/// <summary>
/// Regras do modelo de etapas sem banco (EXE-02 CA 2, 3 e 5). Os erros vêm por campo, com o caminho da linha (<c>steps[2].role</c>,
/// <c>suggestedMilestones[1].stageCode</c>), para o 422 marcar a linha certa.
/// </summary>
public static class StageTemplateRules
{
    public const int MaxName = 100;
    public const int MaxSteps = 30;
    public const int MaxStepName = 60;
    public const int MaxChecklist = 30;
    public const int MaxChecklistText = 200;
    public const int MaxMilestones = 10;
    public const int MaxDays = 365;
    public const decimal MaxHours = 99_999m;

    /// <summary>Segmentos aceitos: os do perfil Serviços (ADR 0046 §4).</summary>
    public static IReadOnlyList<BusinessSegment> Segments =>
        BusinessProfileCatalog.Find(BusinessProfileCatalog.Services)?.Segments ?? [];

    public static (IReadOnlyList<NormalizedStageStep> Steps, IReadOnlyList<SuggestedMilestone> Milestones, Dictionary<string, string[]> Errors) Validate(StageTemplateInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Add(string field, string message) => errors.TryAdd(field, [message]);

        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > MaxName)
        {
            Add("name", $"Informe o nome do modelo, com até {MaxName} caracteres.");
        }

        var segment = string.IsNullOrWhiteSpace(input.Segment) ? null : input.Segment.Trim();
        if (segment is not null && Segments.All(s => !string.Equals(s.Code, segment, StringComparison.Ordinal)))
        {
            Add("segment", "Escolha um segmento do perfil Serviços ou deixe em branco.");
        }

        var steps = NormalizeSteps(input.Steps ?? [], errors);
        RoleRules(steps, errors);
        var milestones = NormalizeMilestones(input.SuggestedMilestones ?? [], steps, errors);
        return (steps, milestones, errors);
    }

    private static List<NormalizedStageStep> NormalizeSteps(IReadOnlyList<StageStepInput> list, Dictionary<string, string[]> errors)
    {
        void Add(string field, string message) => errors.TryAdd(field, [message]);
        if (list.Count == 0)
        {
            Add("steps", "Inclua ao menos uma etapa.");
        }
        else if (list.Count > MaxSteps)
        {
            Add("steps", $"Inclua no máximo {MaxSteps} etapas.");
        }

        var normalized = new List<NormalizedStageStep>();
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
            if (!IsLetter(code))
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

            if (!Enum.IsDefined(s.Role))
            {
                Add(field + ".role", "Escolha o papel da etapa: Execução, Entrega ou Aceite do cliente.");
            }

            if (s.StandardHoursPerUnit < 0 || s.StandardHoursPerUnit > MaxHours)
            {
                Add(field + ".standardHoursPerUnit", "Informe as horas por unidade entre 0 e 99.999.");
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

            normalized.Add(new NormalizedStageStep(s.Id, i + 1, name, code, s.Role, Math.Round(s.StandardHoursPerUnit, 4, MidpointRounding.AwayFromZero), s.PositionId, s.RequiresApproval, checklist));
        }

        return normalized;
    }

    /// <summary>
    /// Papéis (CA 3): ao menos uma Execução; no máximo uma Entrega e um Aceite; o Aceite exige a Entrega antes e é a última etapa; depois da
    /// Entrega só pode vir o Aceite.
    /// </summary>
    private static void RoleRules(List<NormalizedStageStep> steps, Dictionary<string, string[]> errors)
    {
        void Add(string field, string message) => errors.TryAdd(field, [message]);
        if (steps.Count > 0 && steps.All(s => s.Role != StageRole.Execution))
        {
            Add("steps", "Inclua ao menos uma etapa de Execução.");
        }

        int? delivery = null;
        int? acceptance = null;
        for (var i = 0; i < steps.Count; i++)
        {
            var field = $"steps[{i}].role";
            switch (steps[i].Role)
            {
                case StageRole.Delivery when delivery is not null:
                    Add(field, "O modelo tem no máximo uma etapa de Entrega.");
                    break;
                case StageRole.Delivery:
                    delivery = i;
                    break;
                case StageRole.Acceptance when acceptance is not null:
                    Add(field, "O modelo tem no máximo uma etapa de Aceite do cliente.");
                    break;
                case StageRole.Acceptance:
                    acceptance = i;
                    if (delivery is null)
                    {
                        Add(field, "O Aceite do cliente vem depois de uma etapa de Entrega.");
                    }
                    else if (i != steps.Count - 1)
                    {
                        Add(field, "O Aceite do cliente é a última etapa.");
                    }

                    break;
                case StageRole.Execution when delivery is not null:
                    Add(field, "Depois da Entrega só pode vir o Aceite do cliente.");
                    break;
            }
        }
    }

    /// <summary>
    /// Marcos sugeridos (CA 5): até 10, % entre 0 e 100 com soma 100; o gatilho por etapa só aponta para a letra de uma etapa de Execução do
    /// modelo; o do aceite exige a etapa de Aceite; prazo de 0 a 365 dias. Letra em gatilho que não é por etapa é descartada.
    /// </summary>
    private static List<SuggestedMilestone> NormalizeMilestones(IReadOnlyList<SuggestedMilestoneInput> list, IReadOnlyList<NormalizedStageStep> steps, Dictionary<string, string[]> errors)
    {
        void Add(string field, string message) => errors.TryAdd(field, [message]);
        if (list.Count > MaxMilestones)
        {
            Add("suggestedMilestones", $"Inclua no máximo {MaxMilestones} marcos sugeridos.");
        }

        var execution = steps.Where(s => s.Role == StageRole.Execution).Select(s => s.Code).ToHashSet(StringComparer.Ordinal);
        var hasAcceptance = steps.Any(s => s.Role == StageRole.Acceptance);
        var result = new List<SuggestedMilestone>();
        for (var i = 0; i < list.Count && i < MaxMilestones; i++)
        {
            var m = list[i];
            var field = $"suggestedMilestones[{i}]";
            var percent = Math.Round(m.Percent, 2, MidpointRounding.AwayFromZero);
            if (percent <= 0 || percent > 100)
            {
                Add(field + ".percent", "Informe o percentual entre 0,01% e 100%.");
            }

            if (!Enum.IsDefined(m.Trigger))
            {
                Add(field + ".trigger", "Escolha o gatilho do marco.");
            }

            string? code = null;
            if (m.Trigger == SuggestedMilestoneTrigger.StageCompleted)
            {
                code = (m.StageCode ?? string.Empty).Trim().ToUpperInvariant();
                if (!execution.Contains(code))
                {
                    Add(field + ".stageCode", "Escolha a letra de uma etapa de Execução deste modelo.");
                }
            }
            else if (m.Trigger == SuggestedMilestoneTrigger.TechnicalAcceptance && !hasAcceptance)
            {
                Add(field + ".trigger", "Inclua a etapa de Aceite do cliente para usar este gatilho.");
            }

            if (m.DaysAfterEvent < 0 || m.DaysAfterEvent > MaxDays)
            {
                Add(field + ".daysAfterEvent", $"Informe o prazo entre 0 e {MaxDays} dias.");
            }

            result.Add(new SuggestedMilestone(percent, m.Trigger, code, m.DaysAfterEvent));
        }

        if (result.Count > 0 && result.Sum(m => m.Percent) != 100m)
        {
            Add("suggestedMilestones", "A soma dos marcos sugeridos tem de dar 100%.");
        }

        return result;
    }

    public static bool IsLetter(string code) => code.Length == 1 && code[0] is >= 'A' and <= 'Z';

    public static Error? ToError(Dictionary<string, string[]> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return errors.Count == 0 ? null : Error.Validation("validation", errors.First().Value[0], errors);
    }

    public static Error Invalid(string field, string message) =>
        Error.Validation("validation", message, new Dictionary<string, string[]> { [field] = [message] });
}
