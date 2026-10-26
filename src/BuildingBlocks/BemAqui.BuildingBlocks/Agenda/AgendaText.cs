using System.Globalization;

namespace BemAqui.BuildingBlocks.Agenda;

/// <summary>Textos da pauta (CA 5): título com a contagem e o porquê numa linha, em português.</summary>
public static class AgendaText
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Money(decimal value) => value.ToString("C2", PtBr);

    /// <summary>"1 ordem de pagamento aguarda você" / "3 ordens de pagamento aguardam você".</summary>
    public static string Count(int count, string singular, string plural) =>
        count.ToString(PtBr) + " " + (count == 1 ? singular : plural);

    /// <summary>"hoje", "amanhã", "ontem" ou "segunda-feira, 20/10" (o ano só quando não é o de hoje).</summary>
    public static string Day(DateOnly date, DateOnly today) => (date.DayNumber - today.DayNumber) switch
    {
        0 => "hoje",
        1 => "amanhã",
        -1 => "ontem",
        _ => date.ToString("dddd", PtBr) + ", " + DayMonth(date, today),
    };

    /// <summary>"20/10" (ou "20/10/2025" fora do ano corrente).</summary>
    public static string DayMonth(DateOnly date, DateOnly today) => date.ToString(date.Year == today.Year ? "dd/MM" : "dd/MM/yyyy", PtBr);

    /// <summary>
    /// "a primeira vence segunda-feira, 20/10", "o primeiro vence hoje", "o mais antigo venceu ontem" ou "o mais antigo venceu em 10/10".
    /// </summary>
    public static string Due(string subject, DateOnly date, DateOnly today)
    {
        if (date >= today)
        {
            return $"{subject} vence {Day(date, today)}";
        }

        return date.DayNumber == today.DayNumber - 1 ? $"{subject} venceu ontem" : $"{subject} venceu em {DayMonth(date, today)}";
    }

    /// <summary>"R$ 38.420,00 no total".</summary>
    public static string Total(decimal total) => Money(total) + " no total";

    /// <summary>Junta as partes não vazias com "; ", com a primeira letra maiúscula e ponto final (CA 5: o porquê numa linha).</summary>
    public static string Sentence(params string?[] parts)
    {
        var text = string.Join("; ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return text.Length == 0 ? string.Empty : char.ToUpper(text[0], PtBr) + text[1..] + ".";
    }

    /// <summary>"OP-2026-0001 · R$ 1.200,00" (valor opcional).</summary>
    public static string WithAmount(string label, decimal? amount) => amount is { } value ? label + " · " + Money(value) : label;

    /// <summary>Só rota relativa do próprio app (CA 1, mesma regra das notificações da FND-07): sem "//", sem barra invertida.</summary>
    public static bool IsAppLink(string? link) =>
        !string.IsNullOrWhiteSpace(link) && (link == "/app" || link.StartsWith("/app/", StringComparison.Ordinal) || link.StartsWith("/app?", StringComparison.Ordinal))
        && !link.Contains((char)92, StringComparison.Ordinal) && !link.Contains("//", StringComparison.Ordinal);
}
