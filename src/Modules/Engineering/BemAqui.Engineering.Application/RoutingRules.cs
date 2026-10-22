using System.Globalization;
using System.Text;
using BemAqui.Engineering.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Engineering.Application;

/// <summary>Etapa já normalizada (nome e letra aparados, letra maiúscula, checklist sem linhas vazias e numerado).</summary>
public sealed record NormalizedStep(Guid? Id, int Sequence, string Name, string Code, Guid? WorkCenterId, decimal StandardHours, bool IsQualityStep, bool RequiresApproval, IReadOnlyList<RoutingChecklistItem> Checklist);

/// <summary>
/// Regras do roteiro sem banco (ENG-01 CA 3 a 6, D10, D12): limites, letra de A a Z sem repetir, primeira etapa sem
/// qualidade e nunca duas de qualidade seguidas. Os erros vêm por campo, com o caminho da linha (<c>steps[2].code</c>).
/// </summary>
public static class RoutingRules
{
    public const int MaxName = 100;
    public const int MaxProductType = 60;
    public const int MaxSteps = 30;
    public const int MaxStepName = 60;
    public const int MaxChecklist = 30;
    public const int MaxChecklistText = 120;
    public const decimal MaxHours = 99_999m;

    public static (IReadOnlyList<NormalizedStep> Steps, Dictionary<string, string[]> Errors) Validate(string? name, string? productType, IReadOnlyList<RoutingStepInput>? steps)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Add(string field, string message) => errors.TryAdd(field, [message]);

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxName)
        {
            Add("name", $"Informe o nome do roteiro, com até {MaxName} caracteres.");
        }

        if (productType is { } type && type.Trim().Length > MaxProductType)
        {
            Add("productType", $"O tipo de produto tem até {MaxProductType} caracteres.");
        }

        var list = steps ?? [];
        if (list.Count == 0)
        {
            Add("steps", "Inclua ao menos uma etapa.");
        }
        else if (list.Count > MaxSteps)
        {
            Add("steps", $"Inclua no máximo {MaxSteps} etapas.");
        }

        var normalized = new List<NormalizedStep>();
        var codes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < list.Count && i < MaxSteps; i++)
        {
            var s = list[i];
            var field = $"steps[{i}]";
            var stepName = (s.Name ?? string.Empty).Trim();
            if (stepName.Length == 0 || stepName.Length > MaxStepName)
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

            normalized.Add(new NormalizedStep(s.Id, i + 1, stepName, code, s.WorkCenterId, Math.Round(s.StandardHours, 4, MidpointRounding.AwayFromZero), s.IsQualityStep, s.RequiresApproval,
                checklist.Select((t, n) => new RoutingChecklistItem(n + 1, t)).ToList()));
        }

        return (normalized, errors);
    }

    /// <summary>Centro obrigatório em cada etapa (CA 3); o roteiro de exemplo sem centro só nasce pelo seed (CA 8).</summary>
    public static void RequireWorkCenters(IReadOnlyList<NormalizedStep> steps, Dictionary<string, string[]> errors)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(errors);
        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i].WorkCenterId is null)
            {
                errors.TryAdd($"steps[{i}].workCenterId", ["Escolha o centro de trabalho da etapa."]);
            }
        }
    }

    /// <summary>Letra sugerida pela inicial do nome, sem acento (CA 3): "Expedição" → E; sem letra, nulo.</summary>
    public static string? SuggestLetter(string? name)
    {
        var plain = new string((name ?? string.Empty).Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        var first = plain.ToUpperInvariant().FirstOrDefault(c => c is >= 'A' and <= 'Z');
        return first == default ? null : first.ToString();
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