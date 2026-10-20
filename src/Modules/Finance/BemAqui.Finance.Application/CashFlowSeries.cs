namespace BemAqui.Finance.Application;

/// <summary>Movimento de um dia: o realizado (antes de hoje e no próprio dia) e o projetado (de hoje em diante).</summary>
public readonly record struct CashFlowDayTotals(decimal Inflow, decimal Outflow, decimal OverdueInflow = 0, decimal OverdueOutflow = 0);

/// <summary>
/// Série do fluxo de caixa (FIN-05 CA 2, 7 e 8), sem banco: saldo dia a dia a partir do saldo antes do início, agregação
/// semanal (segunda a domingo) e mensal com o saldo do último dia, menor saldo e primeiro dia negativo do projetado.
/// </summary>
public static class CashFlowSeries
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";

    public static readonly IReadOnlyList<string> Views = [Daily, Weekly, Monthly];

    /// <summary>Saldo no fim de cada dia de <paramref name="from"/> a <paramref name="to"/>; saídas chegam positivas. Sem <paramref name="project"/> (filtro por conta), nada é projetado.</summary>
    public static List<CashFlowPeriod> Days(DateOnly from, DateOnly to, DateOnly today, decimal balanceBefore, IReadOnlyDictionary<DateOnly, CashFlowDayTotals> days, bool project = true)
    {
        var result = new List<CashFlowPeriod>();
        var balance = balanceBefore;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var t = days.GetValueOrDefault(d);
            balance += t.Inflow - t.Outflow;
            result.Add(new CashFlowPeriod(d, d, t.Inflow, t.Outflow, balance, project && d >= today, d == today, t.OverdueInflow, t.OverdueOutflow));
        }

        return result;
    }

    /// <summary>Soma os dias de cada semana ou mês; o saldo do período é o do último dia (os totais batem com a diária).</summary>
    public static List<CashFlowPeriod> Aggregate(IReadOnlyList<CashFlowPeriod> days, string view)
    {
        if (view == Daily)
        {
            return [.. days];
        }

        return days
            .GroupBy(d => view == Weekly ? d.Start.AddDays(-(((int)d.Start.DayOfWeek + 6) % 7)) : new DateOnly(d.Start.Year, d.Start.Month, 1))
            .Select(g => new CashFlowPeriod(
                g.First().Start, g.Last().End, g.Sum(d => d.Inflow), g.Sum(d => d.Outflow), g.Last().Balance,
                g.Last().Projected, g.Any(d => d.ContainsToday), g.Sum(d => d.OverdueInflow), g.Sum(d => d.OverdueOutflow)))
            .ToList();
    }

    /// <summary>Menor saldo do projetado (o primeiro, se empatar) e o primeiro dia negativo (CA 8).</summary>
    public static (CashFlowPoint? Lowest, CashFlowPoint? FirstNegative) Highlights(IReadOnlyList<CashFlowPeriod> days)
    {
        var projected = days.Where(d => d.Projected).ToList();
        if (projected.Count == 0)
        {
            return (null, null);
        }

        var lowest = projected.Aggregate((a, b) => b.Balance < a.Balance ? b : a);
        var negative = projected.FirstOrDefault(d => d.Balance < 0);
        return (new CashFlowPoint(lowest.End, lowest.Balance), negative is null ? null : new CashFlowPoint(negative.End, negative.Balance));
    }

    /// <summary>Vencimentos da recorrente no horizonte, só das competências que ainda não geraram título (CA 4).</summary>
    public static IEnumerable<(DateOnly Competence, DateOnly Due)> Occurrences(DateOnly start, DateOnly? end, Func<DateOnly, DateOnly> dueOf, DateOnly today, DateOnly to, IReadOnlySet<DateOnly> generated)
    {
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        for (var c = start > first ? start : first; c <= to && (end is null || c <= end); c = c.AddMonths(1))
        {
            var due = dueOf(c);
            if (due >= today && due <= to && !generated.Contains(c))
            {
                yield return (c, due);
            }
        }
    }
}
