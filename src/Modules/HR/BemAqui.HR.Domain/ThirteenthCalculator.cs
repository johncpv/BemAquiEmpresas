using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BemAqui.HR.Domain;

/// <summary>Parcela do 13º (FLH-04): a 1ª (adiantamento, sem INSS e IRRF) ou a 2ª (com o INSS e o IRRF do 13º inteiro).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ThirteenthInstallment>))]
public enum ThirteenthInstallment
{
    First = 1,
    Second = 2,
}

/// <summary>Avos da 1ª parcela (<c>hr.thirteenth_rules</c>, "A confirmar com a contadora").</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ThirteenthFirstAvos>))]
public enum ThirteenthFirstAvos
{
    /// <summary>
    /// Avos do ano projetados (pré-configurado): os meses até o da parcela pelo ponto e pelos afastamentos, os seguintes como trabalhados
    /// (quem está no ano inteiro recebe a metade da remuneração).
    /// </summary>
    ProjectedYear = 1,

    /// <summary>Só os avos até o mês da parcela, inclusive.</summary>
    UpToMonth = 2,
}

/// <summary>Que afastamento pela previdência tira dias do mês nos avos (<c>hr.thirteenth_leave_rules</c>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ThirteenthLeaveRule>))]
public enum ThirteenthLeaveRule
{
    /// <summary>Os dias pagos pela previdência, na doença e no acidente de trabalho (pré-configurado).</summary>
    InssLeaves = 1,

    /// <summary>Só os da doença; o acidente de trabalho conta como trabalhado.</summary>
    SicknessOnly = 2,
}

/// <summary>Médias das variáveis no 13º (<c>hr.thirteenth_averages</c>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ThirteenthAveragesRule>))]
public enum ThirteenthAveragesRule
{
    /// <summary>Média mensal das variáveis dos meses com avos, proporcional aos avos (pré-configurado).</summary>
    MonthsWithAvos = 1,

    /// <summary>Sem médias.</summary>
    None = 2,
}

/// <summary>Parâmetros do 13º (<c>hr.parameters</c>, pré-configurados pela legislação e "A confirmar com a contadora", PO 21/10/2026).</summary>
public sealed record ThirteenthParameters(ThirteenthFirstAvos FirstAvos, int MonthMinDays, ThirteenthLeaveRule LeaveRule, ThirteenthAveragesRule Averages, bool FirstWithFgts)
{
    public static ThirteenthParameters Default { get; } = new(ThirteenthFirstAvos.ProjectedYear, 15, ThirteenthLeaveRule.InssLeaves, ThirteenthAveragesRule.MonthsWithAvos, true);
}

/// <summary>Faltas injustificadas do mês (dias, do espelho de ponto fechado).</summary>
public sealed record ThirteenthAbsence(int Month, int Days);

/// <summary>Variáveis do mês (horas extras, DSR, adicional noturno, prêmios habituais) das folhas Mensais fechadas do ano.</summary>
public sealed record ThirteenthVariable(int Month, decimal Amount);

/// <summary>
/// Retrato das entradas do 13º de um funcionário (ADR 0039 §2, <c>inputs_snapshot</c>, <c>[PersonalData]</c>): ano, parcela, até que mês
/// contam os avos, salário de referência, vínculo, adicionais, dependentes, afastamentos e faltas do ano, variáveis por mês, adiantamentos
/// já pagos (1ª parcela e férias) e os parâmetros. Sem nome nem documento.
/// </summary>
public sealed record ThirteenthSnapshot(
    int Year,
    ThirteenthInstallment Installment,
    int LastMonth,
    decimal BaseSalary,
    DateOnly Admission,
    DateOnly? Termination,
    int? InsalubrityPct,
    bool HazardPay,
    IReadOnlyList<PayrollCalcDependent> Dependents,
    IReadOnlyList<PayrollCalcLeave> Leaves,
    IReadOnlyList<ThirteenthAbsence> Absences,
    IReadOnlyList<ThirteenthVariable> Variables,
    decimal AdvancePaid,
    decimal VacationAdvance,
    PayrollCalcParameters Monthly,
    ThirteenthParameters Parameters);

/// <summary>Um mês nos avos (CA 3): dias no vínculo, faltas, dias pela previdência, dias trabalhados e se conta.</summary>
public sealed record ThirteenthMonth(int Month, int EmployedDays, int AbsenceDays, int LeaveDays, int WorkedDays, bool Counts, bool Projected = false);

public sealed record ThirteenthInput(ThirteenthSnapshot Employee, IReadOnlyList<PayrollCalcItem> Items, IReadOnlyList<PayrollCalcTable> Tables);

/// <summary>Funcionário na estimativa do 13º (CA 7): salário atual e o vínculo, para os avos projetados do ano.</summary>
public sealed record ThirteenthForecastEmployee(Guid EmployeeId, decimal BaseSalary, DateOnly Admission, DateOnly? Termination);

/// <summary>Estimativa do 13º na folha prevista (CA 7, <c>hr.forecast_13th</c>): as pessoas, os meses das datas limite e o mínimo de dias.</summary>
public sealed record ThirteenthForecast(IReadOnlyList<ThirteenthForecastEmployee> Employees, int FirstMonth, int SecondMonth, int MonthMinDays);

/// <summary>
/// Motor do 13º salário (FLH-04, ADR 0039 §2): função pura do Domain, como o <see cref="PayrollCalculator"/>, com as mesmas tabelas
/// (FLH-01) e o mesmo INSS e IRRF (<see cref="PayrollTaxRules.Inss"/>, <see cref="PayrollCalculator.IrrfOption"/>). Avos por mês com
/// 15 dias ou mais trabalhados (<see cref="ThirteenthMonths"/>); 1ª parcela = metade da remuneração × avos ÷ 12, sem INSS e IRRF, com FGTS
/// (<see cref="ThirteenthAdvance"/>); 2ª parcela = remuneração de dezembro × avos ÷ 12 + médias (<see cref="ThirteenthAverages"/>) −
/// adiantamentos, com o INSS e o IRRF exclusivo na fonte sobre o 13º inteiro (<see cref="ThirteenthFinal"/>). Cada valor em 2 casas
/// (<c>AwayFromZero</c>); os impostos pelo arredondamento da tabela.
/// </summary>
public static class ThirteenthCalculator
{
    /// <summary>Versão do motor do 13º (gravada no holerite e na folha, ADR 0039 §2).</summary>
    public const string Version = "1.0.0";

    public const string SimulationMessage = "13º calculado em conferência: feche a folha de 13º lançada.";
    public const string EmptyMessage = "Nenhum funcionário com avos no ano.";
    public const string FirstOpenCode = "payroll_thirteenth_first_open";
    public const string FirstOpenMessage = "A 1ª parcela não foi paga: a 2ª será calculada sem adiantamento.";
    public const string NoAvosCode = "no_avos";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// As variáveis das médias (CA 2): as regras das horas da FLH-02b, as horas extras (dia útil, domingo ou folga e feriado), o adicional
    /// noturno e o DSR sobre elas. O banco de horas pago é acerto do saldo, não habitual, e fica fora.
    /// </summary>
    public static readonly PayrollCalculation[] VariableRules =
    [
        PayrollCalculation.OvertimeWorkday, PayrollCalculation.OvertimeRestDay, PayrollCalculation.OvertimeHoliday, PayrollCalculation.NightShift, PayrollCalculation.Dsr,
    ];

    public static bool IsThirteenth(PayrollRunType type) => type is PayrollRunType.Thirteenth1 or PayrollRunType.Thirteenth2;

    public static ThirteenthInstallment InstallmentOf(PayrollRunType type) => type == PayrollRunType.Thirteenth2 ? ThirteenthInstallment.Second : ThirteenthInstallment.First;

    /// <summary>Linha que entra nas médias: provento com uma das regras das horas da FLH-02b (<see cref="VariableRules"/>).</summary>
    public static bool IsVariable(PayrollItemKind kind, PayrollCalculation calculation) =>
        kind == PayrollItemKind.Earning && VariableRules.Contains(calculation);

    /// <summary>Último mês apurado pelo ponto e pelos afastamentos: dezembro na 2ª parcela; na 1ª, o mês da parcela.</summary>
    public static int LastMonth(ThirteenthInstallment installment, int periodMonth) =>
        installment == ThirteenthInstallment.Second ? 12 : Math.Clamp(periodMonth, 1, 12);

    /// <summary>
    /// Avos (CA 3), mês a mês de janeiro até <paramref name="lastMonth"/>: dias no vínculo (corridos), menos as faltas injustificadas do
    /// espelho fechado e os dias pagos pela previdência (depois dos <paramref name="companyDays"/> da empresa, pela regra do parâmetro); o
    /// mês conta com <c>MonthMinDays</c> dias ou mais trabalhados.
    /// </summary>
    public static IReadOnlyList<ThirteenthMonth> ThirteenthMonths(int year, int lastMonth, DateOnly admission, DateOnly? termination, IReadOnlyList<PayrollCalcLeave> leaves,
        IReadOnlyList<ThirteenthAbsence> absences, int companyDays, ThirteenthParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(leaves);
        ArgumentNullException.ThrowIfNull(absences);
        ArgumentNullException.ThrowIfNull(parameters);
        var months = new List<ThirteenthMonth>();
        for (var m = 1; m <= Math.Min(lastMonth, 12); m++)
        {
            var start = new DateOnly(year, m, 1);
            var end = start.AddMonths(1).AddDays(-1);
            var from = admission > start ? admission : start;
            var to = termination is { } t && t < end ? t : end;
            if (to < from)
            {
                months.Add(new ThirteenthMonth(m, 0, 0, 0, 0, false));
                continue;
            }

            var employed = to.DayNumber - from.DayNumber + 1;
            var leaveDays = 0;
            foreach (var leave in leaves.Where(l => l.Reason == AbsenceReason.MedicalCertificate || (l.Reason == AbsenceReason.WorkAccident && parameters.LeaveRule == ThirteenthLeaveRule.InssLeaves)))
            {
                var inssFrom = leave.Start.AddDays(Math.Max(companyDays, 0));
                var a = inssFrom > from ? inssFrom : from;
                var b = leave.End < to ? leave.End : to;
                leaveDays += b < a ? 0 : b.DayNumber - a.DayNumber + 1;
            }

            leaveDays = Math.Min(leaveDays, employed);
            var absence = Math.Min(absences.Where(x => x.Month == m).Sum(x => Math.Max(x.Days, 0)), employed - leaveDays);
            var worked = employed - leaveDays - absence;
            months.Add(new ThirteenthMonth(m, employed, absence, leaveDays, worked, worked >= Math.Max(parameters.MonthMinDays, 1)));
        }

        return months;
    }

    /// <summary>
    /// Os avos do retrato (CA 3): de janeiro ao último mês apurado; na 1ª parcela com os avos projetados (pré-configurado), os meses
    /// seguintes até dezembro entram pelo vínculo, como trabalhados (<see cref="ThirteenthMonth.Projected"/>).
    /// </summary>
    public static IReadOnlyList<ThirteenthMonth> MonthsOf(ThirteenthSnapshot e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var months = ThirteenthMonths(e.Year, e.LastMonth, e.Admission, e.Termination, e.Leaves, e.Absences, e.Monthly.SickLeaveCompanyDays, e.Parameters);
        if (e.Installment == ThirteenthInstallment.Second || e.Parameters.FirstAvos != ThirteenthFirstAvos.ProjectedYear || e.LastMonth >= 12)
        {
            return months;
        }

        var projected = ThirteenthMonths(e.Year, 12, e.Admission, e.Termination, [], [], 0, e.Parameters).Where(m => m.Month > e.LastMonth).Select(m => m with { Projected = true });
        return [.. months, .. projected];
    }

    /// <summary>"mar/2026: 31 dias no vínculo − 2 faltas = 29 dias trabalhados → conta".</summary>
    public static string MonthText(int year, ThirteenthMonth m, int minDays)
    {
        ArgumentNullException.ThrowIfNull(m);
        var label = new DateOnly(year, m.Month, 1).ToString("MMM'/'yyyy", PtBr).Replace(".", string.Empty, StringComparison.Ordinal);
        if (m.EmployedDays == 0)
        {
            return $"{label}: fora do vínculo → não conta.";
        }

        var parts = $"{m.EmployedDays} {(m.EmployedDays == 1 ? "dia" : "dias")} no vínculo"
            + (m.AbsenceDays > 0 ? $" − {m.AbsenceDays} {(m.AbsenceDays == 1 ? "falta" : "faltas")}" : string.Empty)
            + (m.LeaveDays > 0 ? $" − {m.LeaveDays} {(m.LeaveDays == 1 ? "dia" : "dias")} pela previdência" : string.Empty);
        return $"{label}{(m.Projected ? " (projetado)" : string.Empty)}: {parts} = {m.WorkedDays} {(m.WorkedDays == 1 ? "dia trabalhado" : "dias trabalhados")} → {(m.Counts ? "conta" : $"não conta (menos de {minDays} dias)")}.";
    }

    /// <summary>
    /// Remuneração de referência: o salário base mais o adicional fixo do mês cheio (insalubridade ou periculosidade, sem acúmulo salvo o
    /// parâmetro), pela mesma regra da folha mensal.
    /// </summary>
    public static (decimal Amount, IReadOnlyList<string> Memory) Remuneration(ThirteenthSnapshot e, IReadOnlyList<PayrollCalcTable> tables)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(tables);
        var prm = e.Monthly;
        var memory = new List<string> { $"Salário base {PayrollCalculator.Brl(e.BaseSalary)}." };
        var insalubrity = 0m;
        if (e.InsalubrityPct is { } pct and > 0)
        {
            var minimum = tables.FirstOrDefault(t => t.Kind == PayrollTaxTableKind.MinimumWage)?.Content.MinimumWage?.Monthly ?? 0m;
            var basis = prm.InsalubrityBase == InsalubrityBase.MinimumWage ? minimum : e.BaseSalary;
            insalubrity = Money(basis * pct / 100m);
        }

        var hazard = e.HazardPay ? Money(e.BaseSalary * prm.HazardPayPct / 100m) : 0m;
        if (insalubrity > 0m && hazard > 0m && !prm.AllowancesAccumulate)
        {
            if (hazard >= insalubrity)
            {
                insalubrity = 0m;
            }
            else
            {
                hazard = 0m;
            }
        }

        if (insalubrity > 0m)
        {
            memory.Add($"Mais a insalubridade de {e.InsalubrityPct}%: {PayrollCalculator.Brl(insalubrity)} (parâmetro hr.insalubrity_base).");
        }

        if (hazard > 0m)
        {
            memory.Add($"Mais a periculosidade: {PayrollCalculator.Brl(hazard)} (parâmetro hr.hazard_pay_pct).");
        }

        var total = e.BaseSalary + insalubrity + hazard;
        memory.Add($"Remuneração de referência: {PayrollCalculator.Brl(total)}.");
        return (total, memory);
    }

    /// <summary>1ª parcela (CA 1): metade da remuneração × avos ÷ 12, menos o adiantamento já pago com as férias (FLH-03).</summary>
    public static (decimal Amount, IReadOnlyList<string> Memory) ThirteenthAdvance(decimal remuneration, int avos, decimal vacationAdvance)
    {
        var full = Money(remuneration * avos / 12m);
        var half = Money(full / 2m);
        var memory = new List<string> { $"{PayrollCalculator.Brl(remuneration)} × {avos}/12 avos = {PayrollCalculator.Brl(full)}; a 1ª parcela é a metade: {PayrollCalculator.Brl(half)}." };
        if (vacationAdvance > 0m)
        {
            var left = Math.Max(half - vacationAdvance, 0m);
            memory.Add($"Menos o adiantamento do 13º já pago com as férias ({PayrollCalculator.Brl(vacationAdvance)}): {PayrollCalculator.Brl(left)}.");
            half = left;
        }

        memory.Add("Sem INSS e sem IRRF: os dois incidem sobre o 13º inteiro, na 2ª parcela.");
        return (half, memory);
    }

    /// <summary>Médias das variáveis (CA 2, <c>hr.thirteenth_averages</c>): soma dos meses com avos ÷ avos = média; média × avos ÷ 12.</summary>
    public static (decimal Amount, IReadOnlyList<string> Memory) ThirteenthAverages(IReadOnlyList<ThirteenthVariable> variables, IReadOnlyList<ThirteenthMonth> months, ThirteenthAveragesRule rule)
    {
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(months);
        var counted = months.Where(m => m.Counts).Select(m => m.Month).ToHashSet();
        if (rule == ThirteenthAveragesRule.None || counted.Count == 0)
        {
            return (0m, []);
        }

        var used = variables.Where(v => counted.Contains(v.Month) && v.Amount > 0m).GroupBy(v => v.Month).Select(g => (Month: g.Key, Amount: g.Sum(v => v.Amount))).OrderBy(v => v.Month).ToList();
        var sum = used.Sum(v => v.Amount);
        if (sum <= 0m)
        {
            return (0m, []);
        }

        var avos = counted.Count;
        var average = sum / avos;
        var amount = Money(average * avos / 12m);
        return (amount,
        [
            $"Variáveis das folhas Mensais fechadas nos meses com avos: {string.Join(", ", used.Select(v => $"{MonthLabel(v.Month)} {PayrollCalculator.Brl(v.Amount)}"))}.",
            $"Média: {PayrollCalculator.Brl(sum)} ÷ {avos} {(avos == 1 ? "mês" : "meses")} = {PayrollCalculator.Brl(Money(average))} (parâmetro hr.thirteenth_averages).",
            $"{PayrollCalculator.Brl(Money(average))} × {avos}/12 avos = {PayrollCalculator.Brl(amount)}.",
        ]);
    }

    /// <summary>
    /// O 13º do funcionário na parcela do retrato (CA 1 a 3). Mesmas rubricas e bases da folha (as marcas da rubrica): na 1ª parcela, só o
    /// adiantamento e o FGTS (parâmetro); na 2ª, o 13º, as médias, o desconto do adiantamento, o INSS e o IRRF sobre o 13º inteiro (IRRF
    /// exclusivo na fonte, separado do salário do mês), o FGTS da diferença e os encargos da empresa.
    /// </summary>
    public static PayrollCalcResult Calculate(ThirteenthInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var e = input.Employee;
        var issues = new List<PayrollCalcIssue>();
        var tableIds = input.Tables.OrderBy(t => t.Kind).Select(t => t.Id).ToList();
        PayrollCalcResult Blocked(string code, string message) =>
            new([], new PayslipTotals(0m, 0m, 0m), 0m, 0m, 0m, 0m, 0m, 0m, 0m, [], [.. issues, new PayrollCalcIssue(code, message, true)], tableIds);

        var tables = input.Tables.ToDictionary(t => t.Kind);
        var period = new DateOnly(e.Year, e.Installment == ThirteenthInstallment.Second ? 12 : Math.Max(e.LastMonth, 1), 1);
        foreach (var kind in Enum.GetValues<PayrollTaxTableKind>())
        {
            if (!tables.ContainsKey(kind))
            {
                return Blocked("table_missing", PayrollTaxRules.MissingMessage(kind, period));
            }
        }

        if (e.BaseSalary <= 0m)
        {
            return Blocked("no_salary", "Sem salário: informe o salário base no cadastro do funcionário.");
        }

        var months = MonthsOf(e);
        var avos = months.Count(m => m.Counts);
        if (avos == 0)
        {
            return Blocked(NoAvosCode, $"Sem avos no ano: nenhum mês com {e.Parameters.MonthMinDays} dias ou mais trabalhados.");
        }

        var lines = new List<PayrollCalcLine>();
        void Add(PayrollCalculation calc, decimal amount, string? reference, List<string> memory)
        {
            if (amount <= 0m)
            {
                return;
            }

            if (input.Items.FirstOrDefault(i => i.Calculation == calc) is not { } item)
            {
                issues.Add(new PayrollCalcIssue("item_missing", $"falta a rubrica com a regra {PayrollCalculator.Label(calc)} (RH → Rubricas): a linha não saiu.", false));
                return;
            }

            lines.Add(new PayrollCalcLine(item.Id, item.Code, item.Name, item.Kind, calc, reference, amount, true, memory, null));
        }

        var (remuneration, remunerationMemory) = Remuneration(e, input.Tables);
        var avosMemory = new List<string> { $"Avos de {e.Year} (mês com {e.Parameters.MonthMinDays} dias ou mais trabalhados conta, parâmetro hr.thirteenth_month_min_days):" };
        avosMemory.AddRange(months.Select(m => MonthText(e.Year, m, e.Parameters.MonthMinDays)));
        avosMemory.Add($"Total: {avos}/12 avos.");
        var reference = $"{avos}/12 avos";
        var fgtsTable = tables[PayrollTaxTableKind.Fgts];
        var fgtsRate = fgtsTable.Content.Fgts?.MonthlyRate ?? 0m;
        decimal Base(Func<PayrollCalcItem, bool> marked) => lines.Sum(l =>
        {
            var item = input.Items.First(i => i.Id == l.ItemId);
            return !marked(item) || l.Type == PayrollItemKind.Information ? 0m : l.Type == PayrollItemKind.Earning ? l.Amount : -l.Amount;
        });

        if (e.Installment == ThirteenthInstallment.First)
        {
            var (advance, memory) = ThirteenthAdvance(remuneration, avos, e.VacationAdvance);
            if (advance <= 0m && e.VacationAdvance > 0m)
            {
                issues.Add(new PayrollCalcIssue("paid_with_vacation", "a 1ª parcela já foi paga com as férias.", false));
            }

            Add(PayrollCalculation.ThirteenthAdvance, advance, reference, [.. remunerationMemory, .. avosMemory, .. memory]);
            var fgtsBase = Math.Max(Base(i => i.FgtsBase), 0m);
            var fgts = e.Parameters.FirstWithFgts ? PayrollTaxRules.Round(fgtsBase * fgtsRate / 100m, fgtsTable.Rounding) : 0m;
            Add(PayrollCalculation.Fgts, fgts, Pct(fgtsRate), [
                $"Base do FGTS: {PayrollCalculator.Brl(fgtsBase)} (a 1ª parcela, parâmetro hr.thirteenth_rules).",
                $"{PayrollCalculator.Brl(fgtsBase)} × {Pct(fgtsRate)} = {PayrollCalculator.Brl(fgts)} (informativo: depósito da empresa).",
                $"Tabela FGTS de {PayrollTaxRules.MonthLabel(fgtsTable.ValidFrom)} ({PayrollCalculator.StatusLabel(fgtsTable.Status)}).",
            ]);
            var totals1 = PayrollRules.Totals(lines.Select(l => new PayslipLineValue(l.Type, l.Amount, l.Reference)));
            return new PayrollCalcResult(lines, totals1, 0m, 0m, e.Parameters.FirstWithFgts ? fgtsBase : 0m, fgts, 0m, 0m, 0m,
                ["Os encargos da empresa sobre o 13º saem na 2ª parcela, sobre o 13º inteiro."], issues, tableIds);
        }

        // 2ª parcela: o 13º do ano pela remuneração de dezembro, as médias e o adiantamento já pago.
        var final = Money(remuneration * avos / 12m);
        Add(PayrollCalculation.ThirteenthFinal, final, reference, [.. remunerationMemory, .. avosMemory, $"{PayrollCalculator.Brl(remuneration)} × {avos}/12 avos = {PayrollCalculator.Brl(final)}."]);
        var (averages, averagesMemory) = ThirteenthAverages(e.Variables, months, e.Parameters.Averages);
        Add(PayrollCalculation.ThirteenthAverages, averages, null, [.. averagesMemory]);
        var gross = final + averages;
        var paid = e.AdvancePaid + e.VacationAdvance;
        var deduction = Math.Min(paid, gross);
        var deductionMemory = new List<string>();
        if (e.AdvancePaid > 0m)
        {
            deductionMemory.Add($"1ª parcela paga: {PayrollCalculator.Brl(e.AdvancePaid)}.");
        }

        if (e.VacationAdvance > 0m)
        {
            deductionMemory.Add($"Adiantamento do 13º pago com as férias: {PayrollCalculator.Brl(e.VacationAdvance)}.");
        }

        deductionMemory.Add($"Desconto do adiantamento: {PayrollCalculator.Brl(deduction)}.");
        Add(PayrollCalculation.ThirteenthAdvanceDeduction, deduction, null, deductionMemory);
        if (paid <= 0m)
        {
            issues.Add(new PayrollCalcIssue("no_advance", "sem 1ª parcela paga no ano: a 2ª saiu sem adiantamento.", false));
        }

        // INSS e IRRF sobre o 13º inteiro, separados do salário do mês (CA 2). As bases saem das linhas antes dos impostos.
        var inssBase = Math.Max(Base(i => i.InssBase), 0m);
        var irrfIncome = Math.Max(Base(i => i.IrrfBase), 0m);
        var inssTable = tables[PayrollTaxTableKind.Inss];
        var inssResult = PayrollTaxRules.Inss(inssTable.Content.Inss!, inssTable.Rounding, inssBase);
        var inss = inssResult.Amount;
        Add(PayrollCalculation.Inss, inss, inssBase > 0m ? Pct(decimal.Round(inss / inssBase * 100m, 2, MidpointRounding.AwayFromZero)) : null,
            [$"INSS sobre o 13º inteiro, separado do salário do mês: base {PayrollCalculator.Brl(inssBase)} (linhas marcadas \"Base do INSS\").", .. inssResult.Memory,
                $"Tabela INSS de {PayrollTaxRules.MonthLabel(inssTable.ValidFrom)} ({PayrollCalculator.StatusLabel(inssTable.Status)})."]);

        var irrfTable = tables[PayrollTaxTableKind.Irrf];
        var irrfContent = irrfTable.Content.Irrf!;
        var dependents = e.Dependents.Count(d => d.IrDependent);
        var legal = inss + (dependents * irrfContent.DependentDeduction);
        var byLegal = PayrollCalculator.IrrfOption(irrfContent, irrfTable.Rounding, irrfIncome, legal, e.Monthly.ReductionIncome);
        var bySimplified = PayrollCalculator.IrrfOption(irrfContent, irrfTable.Rounding, irrfIncome, irrfContent.SimplifiedDiscount, e.Monthly.ReductionIncome);
        var simplified = bySimplified.Final < byLegal.Final;
        var chosen = simplified ? bySimplified : byLegal;
        var other = simplified ? byLegal : bySimplified;
        var legalText = $"dedução legal: INSS do 13º {PayrollCalculator.Brl(inss)} + {dependents} {(dependents == 1 ? "dependente" : "dependentes")} × {PayrollCalculator.Brl(irrfContent.DependentDeduction)} = {PayrollCalculator.Brl(legal)}";
        var simplifiedText = $"desconto simplificado de {PayrollCalculator.Brl(irrfContent.SimplifiedDiscount)}";
        Add(PayrollCalculation.Irrf, chosen.Final, null,
        [
            $"IRRF exclusivo na fonte sobre o 13º, separado do salário do mês. Rendimento: {PayrollCalculator.Brl(irrfIncome)} (linhas marcadas \"Base do IRRF\").",
            $"Escolhido: {(simplified ? simplifiedText : legalText)}, o que dá o menor imposto.",
            .. chosen.Memory,
            $"Pela outra opção ({(simplified ? legalText : simplifiedText)}), o imposto seria {PayrollCalculator.Brl(other.Final)}.",
            $"Tabela IRRF de {PayrollTaxRules.MonthLabel(irrfTable.ValidFrom)} ({PayrollCalculator.StatusLabel(irrfTable.Status)}).",
        ]);

        // FGTS: sobre o 13º menos o que já teve FGTS (a 1ª parcela e o adiantamento das férias), pelas marcas das rubricas.
        var fgtsBase2 = Math.Max(Base(i => i.FgtsBase), 0m);
        var fgts2 = PayrollTaxRules.Round(fgtsBase2 * fgtsRate / 100m, fgtsTable.Rounding);
        Add(PayrollCalculation.Fgts, fgts2, Pct(fgtsRate), [
            $"Base do FGTS: {PayrollCalculator.Brl(fgtsBase2)} (o 13º menos o adiantamento, que já teve o FGTS).",
            $"{PayrollCalculator.Brl(fgtsBase2)} × {Pct(fgtsRate)} = {PayrollCalculator.Brl(fgts2)} (informativo: depósito da empresa).",
            $"Tabela FGTS de {PayrollTaxRules.MonthLabel(fgtsTable.ValidFrom)} ({PayrollCalculator.StatusLabel(fgtsTable.Status)}).",
        ]);
        var chargesTable = tables[PayrollTaxTableKind.EmployerCharges];
        var charges = PayrollTaxRules.EmployerCharges(chargesTable.Content.EmployerCharges!, chargesTable.Rounding, inssBase);
        var totals = PayrollRules.Totals(lines.Select(l => new PayslipLineValue(l.Type, l.Amount, l.Reference)));
        return new PayrollCalcResult(lines, totals, inssBase, chosen.Base, fgtsBase2, fgts2, inss, chosen.Final, charges.Amount,
            [.. charges.Memory, $"Tabela de encargos de {PayrollTaxRules.MonthLabel(chargesTable.ValidFrom)} ({PayrollCalculator.StatusLabel(chargesTable.Status)})."], issues, tableIds);
    }

    /// <summary>
    /// Estimativa do 13º na folha prevista (CA 7): para cada ano, cada pessoa recebe as duas parcelas nos meses das datas limite pelo salário
    /// atual × avos projetados do ano ÷ 12 (metade em cada), sem a parcela cuja folha do ano já fechou; desligado antes do mês sai (a rescisão
    /// paga o proporcional, FLH-05).
    /// </summary>
    public static IReadOnlyList<ForecastLine> ForecastLines(ThirteenthForecast forecast, IEnumerable<int> years, IReadOnlyCollection<(PayrollRunType Type, int Year)> closed)
    {
        ArgumentNullException.ThrowIfNull(forecast);
        ArgumentNullException.ThrowIfNull(years);
        ArgumentNullException.ThrowIfNull(closed);
        var parameters = ThirteenthParameters.Default with { MonthMinDays = forecast.MonthMinDays };
        var lines = new List<ForecastLine>();
        foreach (var year in years.Distinct().Order())
        {
            foreach (var e in forecast.Employees.Where(x => x.BaseSalary > 0m))
            {
                var avos = ThirteenthMonths(year, 12, e.Admission, e.Termination, [], [], 0, parameters).Count(m => m.Counts);
                if (avos == 0)
                {
                    continue;
                }

                var total = Money(e.BaseSalary * avos / 12m);
                var first = Money(total / 2m);
                foreach (var (kind, type, month, amount) in new[]
                {
                    (PayrollForecastKind.Thirteenth1, PayrollRunType.Thirteenth1, forecast.FirstMonth, first),
                    (PayrollForecastKind.Thirteenth2, PayrollRunType.Thirteenth2, forecast.SecondMonth, total - first),
                })
                {
                    var period = new DateOnly(year, Math.Clamp(month, 1, 12), 1);
                    if (amount > 0m && !closed.Contains((type, year)) && (e.Termination is not { } end || end >= period))
                    {
                        lines.Add(new ForecastLine(period, kind, e.EmployeeId, null, amount, null));
                    }
                }
            }
        }

        return lines;
    }

    /// <summary>"Estimativa do 13º 1ª parcela de 2026 — M001" (CA 7): sem valor nem dado pessoal além da matrícula.</summary>
    public static string ForecastDescription(PayrollRunType type, int year, string? registration, string name) =>
        $"Estimativa do 13º {(type == PayrollRunType.Thirteenth2 ? "2ª" : "1ª")} parcela de {year} — {(string.IsNullOrWhiteSpace(registration) ? name : registration)}";

    public static string WriteSnapshot(ThirteenthSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Json);

    public static ThirteenthSnapshot? ReadSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ThirteenthSnapshot>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string MonthLabel(int month) => new DateOnly(2000, Math.Clamp(month, 1, 12), 1).ToString("MMM", PtBr).Replace(".", string.Empty, StringComparison.Ordinal);

    private static string Pct(decimal value) => value.ToString("0.####", PtBr) + "%";

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
