using BemAqui.Production.Contracts;

namespace BemAqui.Production.Domain;

/// <summary>
/// OS de fabricação não cancelada do projeto, como a previsão do projeto enxerga (PLN-01b CA 5 e 6): concluída (com a data), aberta com o
/// fim previsto do plano, ou aberta sem previsão com o motivo em código (<see cref="ProjectForecastReasons"/>).
/// </summary>
public sealed record ProjectForecastOrder(Guid OrderId, string Number, bool Completed, DateOnly? End, string? ReasonCode);

/// <summary>
/// Entrada da previsão do projeto: as OS de fabricação não canceladas, o fim digitado de cada OS de instalação não cancelada (COM-06) e se
/// o orçamento do projeto tem custo de instalação.
/// </summary>
public sealed record ProjectForecastInput(Guid ProjectId, IReadOnlyList<ProjectForecastOrder> Orders, IReadOnlyList<DateOnly> InstallationEnds, bool HasInstallationBudget);

/// <summary>Previsão do projeto: data prevista de entrega, fim da fabricação, ou o motivo (código) e a OS que impede a data.</summary>
public sealed record ProjectForecastResult(Guid ProjectId, DateOnly? ForecastDate, DateOnly? FabricationEnd, string? Reason, Guid? ReasonOrderId);

/// <summary>
/// Data prevista de entrega do projeto (PLN-01b CA 5 e 6, pontos 31 e 32), pura: <b>fim da fabricação</b> = maior fim previsto entre as OS de
/// fabricação não canceladas (a concluída conta pela data da conclusão); <b>entrega</b> = (a) com OS de instalação: max(fim da fabricação +
/// prazo de instalação, maior fim digitado da instalação); (b) sem OS de instalação e com custo de instalação no orçamento: fim da
/// fabricação + prazo; (c) sem instalação: o fim da fabricação. O prazo é em dias úteis do calendário padrão. Alguma OS sem previsão ou
/// nenhuma OS de fabricação: sem data, com o motivo.
/// </summary>
public static class ProjectForecastCalculator
{
    public const int DefaultInstallationDays = 5;
    public const int MinInstallationDays = 0;
    public const int MaxInstallationDays = 120;

    /// <summary>Limite da busca do dia útil (10 anos): calendário sem nenhum dia útil não trava o cálculo.</summary>
    private const int SearchLimitDays = 3650;

    public static ProjectForecastResult Compute(ProjectForecastInput input, int installationDays, Func<DateOnly, bool> isWorkingDay)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(isWorkingDay);
        if (input.Orders.Count == 0)
        {
            return new ProjectForecastResult(input.ProjectId, null, null, ProjectForecastReasons.NoFabricationOrder, null);
        }

        if (input.Orders.Where(o => !o.Completed && o.End is null).OrderBy(o => o.Number, StringComparer.Ordinal).FirstOrDefault() is { } blocked)
        {
            return new ProjectForecastResult(input.ProjectId, null, null, blocked.ReasonCode ?? ProjectForecastReasons.NoCapacity, blocked.OrderId);
        }

        var fabricationEnd = input.Orders.Max(o => o.End!.Value);
        var days = Math.Clamp(installationDays, MinInstallationDays, MaxInstallationDays);
        DateOnly delivery;
        if (input.InstallationEnds.Count > 0)
        {
            var installed = AddWorkingDays(fabricationEnd, days, isWorkingDay);
            var typed = input.InstallationEnds.Max();
            delivery = typed > installed ? typed : installed;
        }
        else
        {
            delivery = input.HasInstallationBudget ? AddWorkingDays(fabricationEnd, days, isWorkingDay) : fabricationEnd;
        }

        return new ProjectForecastResult(input.ProjectId, delivery, fabricationEnd, null, null);
    }

    /// <summary>O <paramref name="days"/>-ésimo dia útil depois de <paramref name="from"/> (0 = o próprio dia).</summary>
    public static DateOnly AddWorkingDays(DateOnly from, int days, Func<DateOnly, bool> isWorkingDay)
    {
        ArgumentNullException.ThrowIfNull(isWorkingDay);
        var date = from;
        for (int counted = 0, guard = 0; counted < days && guard < SearchLimitDays; guard++)
        {
            date = date.AddDays(1);
            if (isWorkingDay(date))
            {
                counted++;
            }
        }

        return date;
    }

    /// <summary>
    /// Código do motivo pelo texto da etapa sem previsão do motor (PLN-01a CA 8): "Etapa {nome} sem centro de trabalho" → <c>NoWorkCenter</c>;
    /// "Centro {nome} sem capacidade…" → <c>NoCapacity</c>.
    /// </summary>
    public static string ReasonCode(string? stepReason) =>
        stepReason is not null && stepReason.EndsWith("sem centro de trabalho", StringComparison.Ordinal) ? ProjectForecastReasons.NoWorkCenter : ProjectForecastReasons.NoCapacity;
}
