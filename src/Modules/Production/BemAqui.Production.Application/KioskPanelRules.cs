using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Production.Application;

/// <summary>
/// Regras puras do Painel da TV (PNL-01b, ADR 0030 §5): nome curto do projeto, operador pelo primeiro nome e inicial, ordem dos cartões
/// e das linhas de atraso, e o limite de linhas das visões.
/// </summary>
public static class KioskPanelRules
{
    /// <summary>Nome curto do projeto (ponto 49): até 28 caracteres, com reticências quando corta.</summary>
    public const int ProjectNameLength = 28;

    /// <summary>Linhas das visões "Fila e previsões" e "Atrasos e riscos" (CA 5 e 6).</summary>
    public const int ViewRows = 6;

    public static string ShortName(string? name)
    {
        var value = (name ?? string.Empty).Trim();
        return value.Length <= ProjectNameLength ? value : value[..(ProjectNameLength - 1)].TrimEnd() + "…";
    }

    /// <summary>Primeiro nome e inicial do sobrenome (ponto 48): "João da Silva" vira "João S."; o nome completo nunca sai do servidor.</summary>
    public static string FirstNameAndInitial(string? name)
    {
        var parts = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => "Operador",
            1 => parts[0],
            _ => parts[0] + " " + char.ToUpperInvariant(parts[^1][0]) + ".",
        };
    }

    /// <summary>Quem está na etapa (item 65 da Fase 3): o primeiro a entrar e "+{n}" com os outros ("João S. +1"); null sem ninguém.</summary>
    public static string? Operators(IReadOnlyList<string> namesByStart)
    {
        ArgumentNullException.ThrowIfNull(namesByStart);
        var names = namesByStart.Select(FirstNameAndInitial).Distinct(StringComparer.Ordinal).ToList();
        return names.Count switch
        {
            0 => null,
            1 => names[0],
            _ => names[0] + " +" + (names.Count - 1),
        };
    }

    /// <summary>Ordem dos cartões na coluna (CA 3): atrasadas (maior atraso primeiro), em risco, depois pelo fim previsto e pelo número.</summary>
    public static IEnumerable<KioskCard> Ordered(IEnumerable<KioskCard> cards) =>
        cards.OrderBy(c => Rank(c.Risk)).ThenByDescending(c => c.DelayDays ?? 0).ThenBy(c => c.PlannedEnd ?? DateOnly.MaxValue).ThenBy(c => c.Number, StringComparer.Ordinal);

    /// <summary>Dias da linha de "Atrasos e riscos": atrasada, desde o prometido; em risco, quanto a previsão passa do prometido.</summary>
    public static int Days(DeliveryRisk risk, int? delayDays, DateOnly? promised, DateOnly? forecast) => risk switch
    {
        DeliveryRisk.Late => delayDays ?? 0,
        DeliveryRisk.AtRisk when promised is { } p && forecast is { } f => Math.Max(f.DayNumber - p.DayNumber, 0),
        _ => 0,
    };

    /// <summary>Ordem de "Atrasos e riscos" (CA 6): do maior atraso para o menor, as atrasadas antes das em risco.</summary>
    public static IEnumerable<KioskDelayRow> Ordered(IEnumerable<KioskDelayRow> rows) =>
        rows.OrderBy(r => Rank(r.Risk)).ThenByDescending(r => r.Days).ThenBy(r => r.Number, StringComparer.Ordinal);

    private static int Rank(DeliveryRisk risk) => risk switch
    {
        DeliveryRisk.Late => 0,
        DeliveryRisk.AtRisk => 1,
        _ => 2,
    };
}
