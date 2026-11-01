using System.Text;
using System.Text.RegularExpressions;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Domain;

/// <summary>
/// Regras puras da régua de cobrança (FIN-09): passos sugeridos (−3, 0, +3 e +10), limites, campos do texto (lista fixa), montagem literal
/// (nenhum texto do usuário vira template) e o passo que cai num dia, com o dia não útil levando o lembrete ao próximo dia útil.
/// </summary>
public static partial class CollectionRules
{
    public const int MaxSteps = 6;
    public const int MinDays = -30;
    public const int MaxDays = 90;
    public const int MaxTextLength = 1000;

    public static readonly TimeOnly DefaultSendTime = new(9, 0);
    public static readonly TimeOnly EarliestSendTime = new(7, 0);
    public static readonly TimeOnly LatestSendTime = new(20, 0);

    public const string Company = "{empresa}";
    public const string Customer = "{cliente}";
    public const string Number = "{numero}";
    public const string Amount = "{valor}";
    public const string DueDate = "{vencimento}";
    public const string DigitableLine = "{linha_digitavel}";
    public const string DaysOverdue = "{dias_de_atraso}";

    /// <summary>Os únicos campos aceitos no texto (CA 2), na ordem da tela.</summary>
    public static IReadOnlyList<string> Placeholders { get; } = [Company, Customer, Number, Amount, DueDate, DigitableLine, DaysOverdue];

    /// <summary>Textos padrão (ADR 0042): ajustáveis na tela; a revisão final do comercial e do jurídico está pendente.</summary>
    public static IReadOnlyList<CollectionStep> DefaultSteps { get; } =
    [
        new(-3, "Olá, {cliente}. Lembramos que o título {numero}, no valor de {valor}, vence em {vencimento}. Se precisar de outra via do boleto, é só responder este e-mail."),
        new(0, "Olá, {cliente}. O título {numero}, no valor de {valor}, vence hoje ({vencimento}). Pague pelo boleto ou pelo Pix para evitar multa e juros."),
        new(3, "Olá, {cliente}. Não identificamos o pagamento do título {numero}, no valor de {valor}, vencido em {vencimento} ({dias_de_atraso} dias de atraso). Se precisar de ajuda, responda este e-mail."),
        new(10, "Olá, {cliente}. O título {numero}, no valor de {valor}, está em atraso há {dias_de_atraso} dias (vencimento em {vencimento}). Por favor, regularize o pagamento ou responda este e-mail para combinarmos."),
    ];

    /// <summary>Títulos que a régua cobra (CA 3): só os lançados à mão e as parcelas de contrato; implantação (FIN-00) fica fora.</summary>
    public static bool OriginCovered(DocumentOrigin origin) => origin is DocumentOrigin.Manual or DocumentOrigin.Contract;

    public static string PlaceholderList => string.Join(", ", Placeholders);

    public static string PlaceholderMessage => "Use só os campos da lista: " + PlaceholderList + ".";

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex Token();

    /// <summary>
    /// Texto literal do passo (CA 2): até 1.000 caracteres e só os campos da lista. Chave solta (como em <c>{{</c> ou <c>{%</c>) também é
    /// recusada, porque não é campo da lista. HTML é aceito como texto e sai escapado.
    /// </summary>
    public static string? TextError(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Escreva o texto do lembrete.";
        }

        if (text.Length > MaxTextLength)
        {
            return "O texto do lembrete pode ter até 1.000 caracteres.";
        }

        var rest = Token().Replace(text, m => Placeholders.Contains(m.Value, StringComparer.Ordinal) ? string.Empty : "\u0000");
        return rest.Contains('\u0000', StringComparison.Ordinal) || rest.Contains('{', StringComparison.Ordinal) || rest.Contains('}', StringComparison.Ordinal)
            ? PlaceholderMessage
            : null;
    }

    /// <summary>Passos (CA 1): de 1 a 6, dias de −30 a +90, sem repetir; devolve o campo e a mensagem do primeiro erro.</summary>
    public static (string Field, string Message)? StepsError(IReadOnlyList<CollectionStep>? steps)
    {
        if (steps is null || steps.Count == 0)
        {
            return ("steps", "Inclua pelo menos um passo na régua.");
        }

        if (steps.Count > MaxSteps)
        {
            return ("steps", "A régua tem até 6 passos.");
        }

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (step is null)
            {
                return ("steps", "Inclua pelo menos um passo na régua.");
            }

            if (step.Days is < MinDays or > MaxDays)
            {
                return ($"steps[{i}].days", "Os dias do passo vão de 30 antes a 90 depois do vencimento.");
            }

            if (steps.Take(i).Any(s => s.Days == step.Days))
            {
                return ($"steps[{i}].days", "Dois passos no mesmo dia: mude os dias de um deles.");
            }

            if (TextError(step.Text) is { } text)
            {
                return ($"steps[{i}].text", text);
            }
        }

        return null;
    }

    public static bool SendTimeValid(TimeOnly time) => time >= EarliestSendTime && time <= LatestSendTime;

    /// <summary>
    /// Montagem literal (CA 2): cada campo da lista vira o valor (numa passada só, então um valor com "{cliente}" não é trocado de novo).
    /// O resultado é texto puro; o template fixo do e-mail recebe como valor e escapa no HTML.
    /// </summary>
    public static string Render(string text, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(values);
        return Token().Replace(text, m => values.TryGetValue(m.Value, out var v) ? v ?? string.Empty : m.Value);
    }

    /// <summary>Dias de atraso no dia (0 antes do vencimento).</summary>
    public static int DaysLate(DateOnly dueDate, DateOnly today) => Math.Max(0, today.DayNumber - dueDate.DayNumber);

    /// <summary>
    /// Passo que cai em <paramref name="today"/> (um dia útil): o dia do passo (vencimento + dias) entre o dia útil anterior (exclusive) e hoje
    /// (inclusive). Assim o passo de sábado, domingo ou feriado sai no próximo dia útil. Dois passos no mesmo dia útil: vale o mais adiantado
    /// (o de mais dias). Nulo: nenhum passo hoje.
    /// </summary>
    public static int? StepOn(DateOnly dueDate, IEnumerable<int> stepDays, DateOnly today, DateOnly previousBusinessDay)
    {
        ArgumentNullException.ThrowIfNull(stepDays);
        int? found = null;
        foreach (var days in stepDays)
        {
            var target = dueDate.AddDays(days);
            if (target > previousBusinessDay && target <= today && (found is null || days > found))
            {
                found = days;
            }
        }

        return found;
    }

    /// <summary>Faixa de vencimentos que pode ter passo hoje: (dia útil anterior − maior passo, hoje − menor passo].</summary>
    public static (DateOnly From, DateOnly To) DueWindow(IReadOnlyCollection<int> stepDays, DateOnly today, DateOnly previousBusinessDay)
    {
        ArgumentNullException.ThrowIfNull(stepDays);
        return (previousBusinessDay.AddDays(1 - stepDays.Max()), today.AddDays(-stepDays.Min()));
    }

    /// <summary>Título do e-mail, escrito pelo sistema (nunca pelo usuário): "vence em", "vence hoje" ou "em atraso".</summary>
    public static string Headline(string number, DateOnly dueDate, DateOnly today)
    {
        var diff = dueDate.DayNumber - today.DayNumber;
        var sb = new StringBuilder("Título ").Append(number);
        return (diff switch
        {
            > 1 => sb.Append(" vence em ").Append(diff).Append(" dias"),
            1 => sb.Append(" vence amanhã"),
            0 => sb.Append(" vence hoje"),
            -1 => sb.Append(" está em atraso há 1 dia"),
            _ => sb.Append(" está em atraso há ").Append(-diff).Append(" dias"),
        }).ToString();
    }

    /// <summary>Rótulo do passo na linha do tempo: "3 dias antes", "No vencimento", "10 dias depois".</summary>
    public static string StepLabel(int days) => days switch
    {
        0 => "No vencimento",
        -1 => "1 dia antes",
        1 => "1 dia depois",
        < 0 => -days + " dias antes",
        _ => days + " dias depois",
    };
}
