using BemAqui.SharedKernel;

namespace BemAqui.HR.Domain;

/// <summary>Grupo de custo do título da folha (RH-02c CA 4): mão de obra direta (cargo com centro de trabalho) ou pessoal administrativo.</summary>
public enum PayrollCostGroup
{
    Direct,
    Admin,
}

/// <summary>Como sai a data de pagamento da folha (RH-02c CA 1): o n-ésimo dia útil de um mês, uma data fixa que antecipa, ou informada.</summary>
public abstract record PaymentDateRule
{
    public sealed record NthBusinessDay(int Year, int Month, int N) : PaymentDateRule;

    public sealed record Fixed(DateOnly Date) : PaymentDateRule;

    public sealed record Informed : PaymentDateRule;
}

/// <summary>Parâmetros dos vencimentos da folha (RH-02c, "A confirmar com a contadora"): 5º dia útil, dia 20, 30/11 e 20/12.</summary>
public sealed record PayrollDueParameters(int SalaryBusinessDay, int AdvanceDay, int ThirteenthFirstMonth, int ThirteenthFirstDay, int ThirteenthSecondMonth, int ThirteenthSecondDay)
{
    public static PayrollDueParameters Default { get; } = new(5, 20, 11, 30, 12, 20);
}

/// <summary>Funcionário da previsão: o líquido da última Mensal fechada, o salário-base vigente, o grupo de custo e o desligamento.</summary>
public sealed record ForecastEmployee(Guid EmployeeId, decimal LastNet, decimal BaseSalary, DateOnly? TerminationDate);

/// <summary>Guia da previsão: a guia da última Mensal fechada (valor e vencimento de lá).</summary>
public sealed record ForecastCharge(PayrollChargeKind Kind, decimal Amount);

/// <summary>Holerite de Férias em rascunho na janela (o líquido entra no mês da competência).</summary>
public sealed record ForecastVacation(Guid EmployeeId, DateOnly Period, decimal Net, Guid RunId);

/// <summary>O que a previsão sabe da folha: a última Mensal fechada, os meses, as pessoas, as guias, as férias e as folhas de 13º já fechadas.</summary>
public sealed record ForecastInput(
    DateOnly LastMonthly, Guid LastRunId, int Months, IReadOnlyList<ForecastEmployee> Employees, IReadOnlyList<ForecastCharge> Charges, IReadOnlyList<ForecastVacation> Vacations,
    IReadOnlyCollection<(PayrollRunType Type, int Year)> ClosedThirteenth);

/// <summary>Linha desejada da previsão (sem o vencimento, que sai do calendário no serviço).</summary>
public sealed record ForecastLine(DateOnly Period, PayrollForecastKind Kind, Guid? EmployeeId, PayrollChargeKind? ChargeKind, decimal Amount, Guid? SourceRunId)
{
    public string Key => PayrollForecastRules.Key(Period, Kind, EmployeeId, ChargeKind);
}

/// <summary>Regras do fechamento da folha sem banco (RH-02c CA 1 e 4).</summary>
public static class PayrollClosingRules
{
    public const string PreconditionCode = "payroll_close_blocked";
    public const string ReopenInPaymentCode = "payroll_payment_in_progress";

    /// <summary>
    /// Data de pagamento pelo tipo (CA 1): Mensal, o n-ésimo dia útil do mês seguinte; Adiantamento, o dia de adiantamento da competência;
    /// 13º 1ª e 2ª parcelas, as datas do ano da competência; Férias e Rescisão, informada no fechamento.
    /// </summary>
    public static PaymentDateRule Rule(PayrollRunType type, DateOnly period, PayrollDueParameters p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var next = period.AddMonths(1);
        return type switch
        {
            PayrollRunType.Monthly => new PaymentDateRule.NthBusinessDay(next.Year, next.Month, p.SalaryBusinessDay),
            PayrollRunType.Advance => new PaymentDateRule.Fixed(DayOf(period.Year, period.Month, p.AdvanceDay)),
            PayrollRunType.Thirteenth1 => new PaymentDateRule.Fixed(DayOf(period.Year, p.ThirteenthFirstMonth, p.ThirteenthFirstDay)),
            PayrollRunType.Thirteenth2 => new PaymentDateRule.Fixed(DayOf(period.Year, p.ThirteenthSecondMonth, p.ThirteenthSecondDay)),
            _ => new PaymentDateRule.Informed(),
        };
    }

    /// <summary>
    /// Resolve a regra com o calendário (dia não útil antecipa): o n-ésimo dia útil (null se o mês não tem tantos) ou o dia útil anterior
    /// mais próximo da data fixa (até 31 dias antes; sem dia útil, a própria data).
    /// </summary>
    public static DateOnly? Resolve(PaymentDateRule rule, Func<DateOnly, bool> isBusinessDay)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(isBusinessDay);
        switch (rule)
        {
            case PaymentDateRule.NthBusinessDay nth:
                var count = 0;
                for (var day = new DateOnly(nth.Year, nth.Month, 1); day.Month == nth.Month; day = day.AddDays(1))
                {
                    if (isBusinessDay(day) && ++count == nth.N)
                    {
                        return day;
                    }
                }

                return null;
            case PaymentDateRule.Fixed fixedDate:
                for (var i = 0; i <= 31; i++)
                {
                    var day = fixedDate.Date.AddDays(-i);
                    if (isBusinessDay(day))
                    {
                        return day;
                    }
                }

                return fixedDate.Date;
            default:
                return null;
        }
    }

    /// <summary>Datas que a resolução consulta no calendário (para buscar os dias úteis numa leitura só).</summary>
    public static (DateOnly From, DateOnly To)? Range(PaymentDateRule rule) => rule switch
    {
        PaymentDateRule.NthBusinessDay nth => (new DateOnly(nth.Year, nth.Month, 1), new DateOnly(nth.Year, nth.Month, DateTime.DaysInMonth(nth.Year, nth.Month))),
        PaymentDateRule.Fixed f => (f.Date.AddDays(-31), f.Date),
        _ => null,
    };

    /// <summary>Grupo de custo pelo cargo (CA 4): cargo com centro de trabalho padrão é mão de obra direta.</summary>
    public static PayrollCostGroup GroupOf(bool positionHasWorkCenter) => positionHasWorkCenter ? PayrollCostGroup.Direct : PayrollCostGroup.Admin;

    /// <summary>A guia vai inteira para o grupo com a maior base do FGTS na competência (CA 4); empate fica na mão de obra direta, sem base, no administrativo.</summary>
    public static PayrollCostGroup ChargeGroup(IEnumerable<(PayrollCostGroup Group, decimal FgtsBase)> bases)
    {
        ArgumentNullException.ThrowIfNull(bases);
        var list = bases.ToList();
        var direct = list.Where(b => b.Group == PayrollCostGroup.Direct).Sum(b => b.FgtsBase);
        var admin = list.Where(b => b.Group == PayrollCostGroup.Admin).Sum(b => b.FgtsBase);
        return direct > 0m && direct >= admin ? PayrollCostGroup.Direct : PayrollCostGroup.Admin;
    }

    /// <summary>"Salário Mensal outubro de 2026 — M001" (CA 2): sem valor nem dado pessoal além da matrícula.</summary>
    public static string SalaryDescription(PayrollRunType type, DateOnly period, string? registration, string name, bool forecast = false) =>
        $"Salário {(forecast ? "previsto " : string.Empty)}{PayrollRules.TypeLabel(type)} {PayrollRules.PeriodLabel(period)} — {(string.IsNullOrWhiteSpace(registration) ? name : registration)}";

    /// <summary>"Guia FGTS outubro de 2026" (CA 2).</summary>
    public static string ChargeDescription(PayrollChargeKind kind, DateOnly period, bool forecast = false) =>
        $"Guia {(forecast ? "prevista " : string.Empty)}{PayrollRules.ChargeLabel(kind)} {PayrollRules.PeriodLabel(period)}";

    /// <summary>"Folha de outubro de 2026 fechada. 12 títulos a pagar gerados." (CA 2).</summary>
    public static string ClosedMessage(DateOnly period, int count) =>
        $"Folha de {PayrollRules.PeriodLabel(period)} fechada. {count} {(count == 1 ? "título a pagar gerado" : "títulos a pagar gerados")}.";

    public static int SalaryDueOffsetMonths(PayrollRunType type) => type == PayrollRunType.Monthly ? 1 : 0;

    private static DateOnly DayOf(int year, int month, int day) => new(year, month, Math.Clamp(day, 1, DateTime.DaysInMonth(year, month)));

    public static Error Blocked(string message) => Error.Conflict(PreconditionCode, message);
}

/// <summary>Folha prevista sem banco (RH-02c CA 14 e 15): as linhas desejadas para a janela depois da última Mensal fechada.</summary>
public static class PayrollForecastRules
{
    public const int DefaultMonths = 3;
    public const int MaxMonths = 12;
    public const string ReplacedReason = "Substituída pela folha real";

    public static string Key(DateOnly period, PayrollForecastKind kind, Guid? employeeId, PayrollChargeKind? chargeKind) =>
        $"{period:yyyy-MM}|{kind}|{employeeId?.ToString() ?? "-"}|{chargeKind?.ToString() ?? "-"}";

    /// <summary>Os meses da janela: os <paramref name="months"/> meses seguintes à última Mensal fechada.</summary>
    public static IReadOnlyList<DateOnly> Window(DateOnly lastMonthly, int months) =>
        Enumerable.Range(1, Math.Clamp(months, 0, MaxMonths)).Select(i => lastMonthly.AddMonths(i)).ToList();

    /// <summary>
    /// Linhas desejadas (CA 14): um salário por funcionário ativo e mês (o líquido da última folha), uma guia por tipo e mês (o valor da
    /// última), o 13º (50% do salário-base em novembro e em dezembro, se a folha do 13º do ano ainda não fechou) e as férias em rascunho na
    /// janela (no mês da competência). Funcionário desligado antes do mês sai daquele mês em diante.
    /// </summary>
    public static IReadOnlyList<ForecastLine> Plan(ForecastInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var lines = new List<ForecastLine>();
        var window = Window(input.LastMonthly, input.Months);
        foreach (var month in window)
        {
            var active = input.Employees.Where(e => e.TerminationDate is not { } end || end >= month).ToList();
            lines.AddRange(active.Where(e => e.LastNet > 0m).Select(e => new ForecastLine(month, PayrollForecastKind.Salary, e.EmployeeId, null, e.LastNet, input.LastRunId)));
            lines.AddRange(input.Charges.Where(c => c.Amount > 0m).Select(c => new ForecastLine(month, PayrollForecastKind.Charge, null, c.Kind, c.Amount, input.LastRunId)));
            foreach (var (kind, type, monthOfYear) in new[] { (PayrollForecastKind.Thirteenth1, PayrollRunType.Thirteenth1, 11), (PayrollForecastKind.Thirteenth2, PayrollRunType.Thirteenth2, 12) })
            {
                if (month.Month == monthOfYear && !input.ClosedThirteenth.Contains((type, month.Year)))
                {
                    lines.AddRange(active.Where(e => e.BaseSalary > 0m).Select(e => new ForecastLine(month, kind, e.EmployeeId, null, PayrollRules.Round(e.BaseSalary * 0.5m), null)));
                }
            }
        }

        lines.AddRange(input.Vacations.Where(v => v.Net > 0m && window.Contains(v.Period))
            .Select(v => new ForecastLine(v.Period, PayrollForecastKind.Vacation, v.EmployeeId, null, v.Net, v.RunId)));
        return lines.GroupBy(l => l.Key).Select(g => g.First()).ToList();
    }

    /// <summary>Tipo de folha da linha (para a data e a descrição).</summary>
    public static PayrollRunType RunType(PayrollForecastKind kind) => kind switch
    {
        PayrollForecastKind.Thirteenth1 => PayrollRunType.Thirteenth1,
        PayrollForecastKind.Thirteenth2 => PayrollRunType.Thirteenth2,
        PayrollForecastKind.Vacation => PayrollRunType.Vacation,
        _ => PayrollRunType.Monthly,
    };
}
