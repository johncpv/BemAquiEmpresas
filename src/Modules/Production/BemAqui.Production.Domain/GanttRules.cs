namespace BemAqui.Production.Domain;

/// <summary>Período do cronograma (PLN-03 CA 6): de <see cref="From"/> a <see cref="To"/>, inclusive.</summary>
public readonly record struct GanttPeriod(DateOnly From, DateOnly To)
{
    public int Days => To.DayNumber - From.DayNumber + 1;
}

/// <summary>Intervalo de uma barra para a distribuição em sub-linhas (CA 5).</summary>
public readonly record struct GanttSpan(Guid Id, DateOnly Start, DateOnly End);

/// <summary>Etapa candidata a "Define a entrega" (CA 3): posição da OS no projeto, sequência e o fim previsto.</summary>
public readonly record struct GanttDriverCandidate(Guid StepId, int OrderIndex, int Sequence, DateOnly? PlannedEnd);

/// <summary>
/// Regras puras do cronograma (PLN-03): o período padrão e o máximo (CA 6), a interseção com o período, a proporção feita da etapa em
/// andamento (CA 2), a etapa que define a entrega (CA 3) e as sub-linhas por centro quando as etapas se sobrepõem no dia (CA 5). Só leitura:
/// nada aqui muda datas (as datas são do motor, ADR 0029).
/// </summary>
public static class GanttRules
{
    /// <summary>Dias antes de hoje no período padrão.</summary>
    public const int DefaultDaysBefore = 7;

    /// <summary>Semanas depois de hoje no período padrão.</summary>
    public const int DefaultWeeksAfter = 12;

    /// <summary>O período vai até 12 meses (366 dias, para caber o ano bissexto).</summary>
    public const int MaxDays = 366;

    /// <summary>De 7 dias antes de hoje a 12 semanas depois (CA 6).</summary>
    public static GanttPeriod DefaultPeriod(DateOnly today) => new(today.AddDays(-DefaultDaysBefore), today.AddDays(7 * DefaultWeeksAfter));

    /// <summary>
    /// Período pedido: sem datas, o padrão; só uma das datas, a outra pelo tamanho do padrão. Null se o fim vem antes do início ou passa de
    /// <see cref="MaxDays"/> dias.
    /// </summary>
    public static GanttPeriod? Resolve(DateOnly today, DateOnly? from, DateOnly? to)
    {
        var standard = DefaultPeriod(today);
        var length = standard.Days - 1;
        var start = from ?? (to is { } end ? end.AddDays(-length) : standard.From);
        var finish = to ?? (from is { } begin ? begin.AddDays(length) : standard.To);
        if (finish < start || finish.DayNumber - start.DayNumber + 1 > MaxDays)
        {
            return null;
        }

        return new GanttPeriod(start, finish);
    }

    /// <summary>
    /// Período do cronograma de um projeto (Projeto 360, CA 11): o padrão alargado para caber o projeto inteiro (do primeiro início ao maior
    /// entre o fim, a previsão e o prometido), sem passar de <see cref="MaxDays"/> dias a partir do início.
    /// </summary>
    public static GanttPeriod ProjectPeriod(DateOnly today, IEnumerable<DateOnly?> dates)
    {
        ArgumentNullException.ThrowIfNull(dates);
        var standard = DefaultPeriod(today);
        var known = dates.Where(d => d is not null).Select(d => d!.Value).ToList();
        var from = known.Count == 0 ? standard.From : Min(standard.From, known.Min().AddDays(-DefaultDaysBefore));
        var to = known.Count == 0 ? standard.To : Max(standard.To, known.Max().AddDays(DefaultDaysBefore));
        return new GanttPeriod(from, Min(to, from.AddDays(MaxDays - 1)));
    }

    /// <summary>A barra de <paramref name="start"/> a <paramref name="end"/> aparece no período (inclusive nas pontas).</summary>
    public static bool Intersects(DateOnly start, DateOnly end, GanttPeriod period) => start <= period.To && end >= period.From;

    /// <summary>
    /// Proporção já feita da etapa (CA 2: apontadas ÷ padrão), pelas horas restantes do plano (padrão − apontado, com o mínimo de 10% do motor):
    /// de 0 a 1, com 4 casas; sem padrão, 0.
    /// </summary>
    public static decimal Progress(decimal standardHours, decimal remainingHours)
    {
        if (standardHours <= 0m)
        {
            return 0m;
        }

        var done = (standardHours - Math.Max(remainingHours, 0m)) / standardHours;
        return Math.Round(Math.Clamp(done, 0m, 1m), 4, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// A etapa que empurra a data (CA 3): a de maior fim previsto do projeto; no empate, a da OS mais adiante na fila do projeto e, nela, a de
    /// maior sequência. Null se nenhuma etapa tem fim previsto.
    /// </summary>
    public static Guid? DrivingStep(IEnumerable<GanttDriverCandidate> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        return steps.Where(s => s.PlannedEnd is not null)
            .OrderByDescending(s => s.PlannedEnd).ThenByDescending(s => s.OrderIndex).ThenByDescending(s => s.Sequence).ThenBy(s => s.StepId)
            .Select(s => (Guid?)s.StepId).FirstOrDefault();
    }

    /// <summary>
    /// Sub-linhas de um centro (CA 5): as barras na ordem do tempo, cada uma na primeira sub-linha livre (duas etapas que se tocam no mesmo
    /// dia ficam em sub-linhas diferentes). Devolve a sub-linha de cada id (0 = a primeira) e quantas sub-linhas o centro precisa.
    /// </summary>
    public static (IReadOnlyDictionary<Guid, int> Lanes, int Count) Lanes(IEnumerable<GanttSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);
        var ends = new List<DateOnly>();
        var lanes = new Dictionary<Guid, int>();
        foreach (var span in spans.OrderBy(s => s.Start).ThenBy(s => s.End).ThenBy(s => s.Id))
        {
            var lane = ends.FindIndex(end => end < span.Start);
            if (lane < 0)
            {
                ends.Add(span.End);
                lane = ends.Count - 1;
            }
            else
            {
                ends[lane] = span.End;
            }

            lanes[span.Id] = lane;
        }

        return (lanes, ends.Count);
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
}
