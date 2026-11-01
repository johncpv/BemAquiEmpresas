using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BemAqui.HR.Domain;

/// <summary>
/// Regra da rubrica na folha calculada (FLH-02a, <c>hr.payroll_items.calculation</c>): <c>None</c> é a rubrica sem regra (lançamento
/// avulso: prêmio, adiantamento, pensão, consignado); as demais são refeitas pelo motor a cada cálculo, na ordem fixa do motor.
/// <c>TransportVoucherSupplied</c> é avulsa informativa: o valor do benefício do mês, que limita o desconto do vale-transporte (CA 7).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PayrollCalculation>))]
public enum PayrollCalculation
{
    None,
    BaseSalary,
    SickLeave,
    Insalubrity,
    HazardPay,
    Absence,
    Lateness,
    FamilyAllowance,
    TransportVoucher,
    TransportVoucherSupplied,
    Inss,
    Irrf,
    Fgts,

    // FLH-02b: horas do espelho fechado (extras por classe do dia, adicional noturno, DSR sobre as variáveis, perda do DSR e o banco de horas).
    OvertimeWorkday,
    OvertimeRestDay,
    OvertimeHoliday,
    NightShift,
    Dsr,
    DsrLoss,
    HourBankPayout,
    HourBankDiscount,
    // FLH-03: folha de férias (VacationCalculator).
    VacationPay,
    VacationAverages,
    VacationThird,
    VacationSale,
    VacationSaleThird,
}

/// <summary>Proporcionalidade do salário no mês de admissão ou desligamento (<c>hr.salary_proration</c>, CA 2).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalaryProration>))]
public enum SalaryProration
{
    /// <summary>Mês comercial de 30 dias (pré-configurado).</summary>
    CommercialMonth = 1,

    /// <summary>Dias corridos do mês.</summary>
    CalendarDays = 2,
}

/// <summary>Base da insalubridade (<c>hr.insalubrity_base</c>, CA 4).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<InsalubrityBase>))]
public enum InsalubrityBase
{
    /// <summary>Salário mínimo da tabela da FLH-01 (pré-configurado).</summary>
    MinimumWage = 1,

    BaseSalary = 2,
}

/// <summary>
/// Rendimento usado na faixa e na fórmula da redução adicional do IRRF (<c>hr.irrf_reduction_income</c>, ponto em aberto 1): os exemplos
/// oficiais da Receita usam o rendimento tributável bruto do mês (pré-configurado); a alternativa é a base de cálculo, depois das deduções.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<IrrfReductionIncome>))]
public enum IrrfReductionIncome
{
    GrossTaxable = 1,
    CalculationBase = 2,
}

/// <summary>Rubrica como o motor enxerga: tipo, regra e as marcas de base (RH-02a) e "Deduz da base do IRRF" (pensão judicial, CA 9).</summary>
public sealed record PayrollCalcItem(Guid Id, string Code, string Name, PayrollItemKind Kind, PayrollCalculation Calculation, bool InssBase, bool FgtsBase, bool IrrfBase, bool DeductsIrrfBase);

/// <summary>Dependente no retrato (sem nome nem CPF): nascimento e as marcas de IR, salário-família e documentos entregues (CA 6).</summary>
public sealed record PayrollCalcDependent(DateOnly BirthDate, bool IrDependent, bool FamilyAllowance, bool FamilyAllowanceDocsOk);

/// <summary>Afastamento no retrato (CAD-04c1): início, fim (inclusive) e o tipo; só doença e acidente mudam o cálculo (CA 3).</summary>
public sealed record PayrollCalcLeave(DateOnly Start, DateOnly End, AbsenceReason? Reason);

/// <summary>Lançamento avulso (CA 13): rubrica sem regra, referência, valor e o motivo obrigatório.</summary>
public sealed record PayrollCalcManualLine(Guid ItemId, string? Reference, decimal Amount, string Note);

/// <summary>Parâmetros da folha calculada (<c>hr.parameters</c>, "A confirmar com a contadora"), lidos de uma vez.</summary>
public sealed record PayrollCalcParameters(
    SalaryProration Proration,
    int SickLeaveCompanyDays,
    bool AccidentKeepsFgts,
    InsalubrityBase InsalubrityBase,
    decimal HazardPayPct,
    bool AllowancesAccumulate,
    decimal HourValueDivisor,
    int FamilyAllowanceMaxAge,
    IrrfReductionIncome ReductionIncome,
    PayrollHourParameters? Hours = null)
{
    /// <summary>Os valores pré-configurados (PO 21/10/2026, revisão pré-Fases 4 a 8, tabela 3.4 pontos 7 a 15).</summary>
    public static PayrollCalcParameters Default { get; } =
        new(SalaryProration.CommercialMonth, 15, true, InsalubrityBase.MinimumWage, 30m, false, 0m, 14, IrrfReductionIncome.GrossTaxable, PayrollHourParameters.Default);

    /// <summary>As regras das horas (FLH-02b); o retrato da 1.0.0 não tem: vale o pré-configurado.</summary>
    [JsonIgnore]
    public PayrollHourParameters HourRules => Hours ?? PayrollHourParameters.Default;
}

/// <summary>
/// Retrato das entradas de um funcionário (ADR 0039 §2, <c>inputs_snapshot</c>, <c>[PersonalData]</c>): o que o motor usou, sem nome nem
/// documento. Recalcular o mesmo retrato com a mesma versão do motor e as mesmas tabelas dá o mesmo resultado (CA 12).
/// </summary>
public sealed record PayrollCalcSnapshot(
    decimal BaseSalary,
    DateOnly Admission,
    DateOnly? Termination,
    bool HasPosition,
    decimal MonthlyHours,
    int? InsalubrityPct,
    bool HazardPay,
    bool UsesTransportVoucher,
    IReadOnlyList<PayrollCalcDependent> Dependents,
    IReadOnlyList<PayrollCalcLeave> Leaves,
    bool TimesheetClosed,
    int AbsenceMinutes,
    int LateMinutes,
    IReadOnlyList<PayrollCalcManualLine> Manual,
    PayrollCalcParameters Parameters,
    IReadOnlyList<PayrollCalcHourDay>? HourDays = null,
    PayrollCalcMonthCalendar? Calendar = null,
    PayrollCalcHourBank? HourBank = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PayrollCalcVacation>? Vacations = null);

/// <summary>
/// Férias no mês (FLH-03 CA 10): os dias de gozo dentro da competência (saem do salário), a base do INSS e o INSS já retidos no recibo de
/// férias e a base do FGTS da parte do mês (<c>hr.vacation_taxation</c>). Sem cálculo de férias ainda, só os dias (<c>Calculated</c> falso).
/// </summary>
public sealed record PayrollCalcVacation(DateOnly Start, DateOnly End, int Days, decimal InssBase, decimal InssWithheld, decimal FgtsBase, bool Calculated);

/// <summary>Versão de tabela legal usada (FLH-01): id, tipo, vigência, situação, arredondamento e o conteúdo.</summary>
public sealed record PayrollCalcTable(Guid Id, PayrollTaxTableKind Kind, DateOnly ValidFrom, PayrollTaxTableStatus Status, PayrollRounding Rounding, PayrollTaxContent Content);

public sealed record PayrollCalcInput(DateOnly Period, PayrollCalcSnapshot Employee, IReadOnlyList<PayrollCalcItem> Items, IReadOnlyList<PayrollCalcTable> Tables);

/// <summary>Linha calculada (ou avulsa) com a memória de cálculo (CA 12).</summary>
public sealed record PayrollCalcLine(Guid ItemId, string Code, string Name, PayrollItemKind Type, PayrollCalculation Calculation, string? Reference, decimal Amount, bool Calculated, IReadOnlyList<string> Memory, string? Note);

/// <summary>Aviso ou erro do funcionário (sem valor): o que trava (<c>Blocking</c>, "Sem jornada") deixa o holerite sem linha calculada.</summary>
public sealed record PayrollCalcIssue(string Code, string Message, bool Blocking);

/// <summary>Resultado do funcionário: linhas, totais, bases, FGTS do mês, impostos, encargos da empresa, avisos e as tabelas usadas.</summary>
public sealed record PayrollCalcResult(
    IReadOnlyList<PayrollCalcLine> Lines,
    PayslipTotals Totals,
    decimal InssBase,
    decimal IrrfBase,
    decimal FgtsBase,
    decimal FgtsAmount,
    decimal Inss,
    decimal Irrf,
    decimal EmployerCharges,
    IReadOnlyList<string> EmployerMemory,
    IReadOnlyList<PayrollCalcIssue> Issues,
    IReadOnlyList<Guid> TableIds);

/// <summary>
/// Motor da folha mensal calculada (FLH-02a, ADR 0039 §2): função pura do Domain, sem banco, sem relógio, sem aleatoriedade e sem log.
/// Ordem fixa: salário proporcional (CA 2), afastamento pela previdência (CA 3), insalubridade e periculosidade sem acúmulo (CA 4), faltas
/// e atrasos do espelho fechado (CA 5), avulsos (CA 13), vale-transporte (CA 7), salário-família (CA 6), INSS progressivo (CA 8), IRRF com
/// a melhor dedução (CA 9), FGTS do mês e encargos da empresa (CA 10). Nenhum valor legal no código: tudo vem das tabelas (FLH-01) e dos
/// parâmetros. Cada valor em 2 casas; os impostos pela regra de arredondamento da versão da tabela.
/// </summary>
public static class PayrollCalculator
{
    /// <summary>Versão semântica do motor (ADR 0039 §2): muda a cada mudança de regra; o holerite calculado guarda a que usou.</summary>
    public const string Version = "1.2.0";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Brl(decimal value) => "R$ " + value.ToString("N2", PtBr);

    private static string Pct(decimal value) => value.ToString("0.####", PtBr) + "%";

    private static string Date(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>Minutos como "8:30 h" (referência da falta e do atraso, CA 5).</summary>
    public static string Hours(int minutes) => $"{minutes / 60}:{minutes % 60:00} h";

    public static string WriteSnapshot(PayrollCalcSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Json);

    public static PayrollCalcSnapshot? ReadSnapshot(string? json) => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<PayrollCalcSnapshot>(json, Json);

    public static string WriteMemory(IReadOnlyList<string> memory) => JsonSerializer.Serialize(memory, Json);

    public static IReadOnlyList<string> ReadMemory(string? json) => string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<string>>(json, Json) ?? [];

    public static string WriteIssues(IReadOnlyList<PayrollCalcIssue> issues) => JsonSerializer.Serialize(issues, Json);

    public static IReadOnlyList<PayrollCalcIssue> ReadIssues(string? json) => string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<PayrollCalcIssue>>(json, Json) ?? [];

    /// <summary>A rubrica tem regra do motor (linha refeita a cada cálculo e nunca editada à mão).</summary>
    public static bool HasRule(PayrollCalculation calculation) => calculation is not (PayrollCalculation.None or PayrollCalculation.TransportVoucherSupplied);

    public static PayrollCalcResult Calculate(PayrollCalcInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var e = input.Employee;
        var prm = e.Parameters;
        var issues = new List<PayrollCalcIssue>();
        var tableIds = input.Tables.OrderBy(t => t.Kind).Select(t => t.Id).ToList();
        PayrollCalcResult Blocked(string code, string message) =>
            new([], new PayslipTotals(0m, 0m, 0m), 0m, 0m, 0m, 0m, 0m, 0m, 0m, [], [.. issues, new PayrollCalcIssue(code, message, true)], tableIds);

        var start = new DateOnly(input.Period.Year, input.Period.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var tables = input.Tables.ToDictionary(t => t.Kind);
        foreach (var kind in Enum.GetValues<PayrollTaxTableKind>())
        {
            if (!tables.ContainsKey(kind))
            {
                return Blocked("table_missing", PayrollTaxRules.MissingMessage(kind, start));
            }
        }

        if (!e.HasPosition)
        {
            return Blocked("no_position", "Sem cargo: informe o cargo no cadastro do funcionário.");
        }

        if (e.BaseSalary <= 0m)
        {
            return Blocked("no_salary", "Sem salário: informe o salário base no cadastro do funcionário.");
        }

        var hourRules = prm.HourRules;
        var hourDays = e.HourDays ?? [];
        var bankDecision = e.HourBank?.DecisionMinutes ?? 0;
        var needsHour = e.AbsenceMinutes + e.LateMinutes > 0 || bankDecision != 0 || hourDays.Any(d => d.OvertimeMinutes > 0 || (hourRules.NightEnabled && d.NightMinutes > 0));
        if (needsHour && HourDivisor(e) <= 0m)
        {
            return Blocked("no_schedule", "Sem jornada: informe as horas mensais do cargo para calcular faltas, atrasos e horas extras.");
        }

        var from = e.Admission > start ? e.Admission : start;
        var to = e.Termination is { } t && t < end ? t : end;
        if (to < from)
        {
            return Blocked("not_in_period", "Fora da competência: admitido depois ou desligado antes do mês.");
        }

        if (!e.TimesheetClosed)
        {
            issues.Add(new PayrollCalcIssue("timesheet_missing", "sem espelho de ponto fechado no mês: faltas e atrasos ficaram em zero.", false));
        }

        var commercial = prm.Proration == SalaryProration.CommercialMonth;
        var divisor = commercial ? 30 : end.Day;
        int Days(DateOnly a, DateOnly b) => a > b ? 0
            : commercial ? Math.Max(0, (b == end ? 30 : Math.Min(b.Day, 30)) - Math.Min(a.Day, 30) + 1)
            : b.DayNumber - a.DayNumber + 1;
        var employedDays = Math.Min(Days(from, to), divisor);
        var partial = from > start || to < end;

        // CA 3: doença e acidente de trabalho. A empresa paga os primeiros dias (parâmetro); do dia seguinte em diante, a previdência.
        int sickDays = 0, accidentDays = 0;
        foreach (var leave in e.Leaves.Where(l => l.Reason is AbsenceReason.MedicalCertificate or AbsenceReason.WorkAccident))
        {
            var inssFrom = leave.Start.AddDays(Math.Max(prm.SickLeaveCompanyDays, 0));
            var a = inssFrom > from ? inssFrom : from;
            var b = leave.End < to ? leave.End : to;
            var days = Days(a, b);
            if (leave.Reason == AbsenceReason.WorkAccident)
            {
                accidentDays += days;
            }
            else
            {
                sickDays += days;
            }
        }

        var inssDays = Math.Min(sickDays + accidentDays, employedDays);
        accidentDays = Math.Min(accidentDays, inssDays);
        // FLH-03 CA 10: os dias de férias do mês foram pagos no recibo de férias e não entram como salário de novo.
        var vacations = e.Vacations ?? [];
        var vacationDays = Math.Min(vacations.Sum(v => v.Days), employedDays - inssDays);
        var paidDays = employedDays - inssDays - vacationDays;
        var salary = e.BaseSalary;
        var lines = new List<PayrollCalcLine>();
        PayrollCalcItem? ItemOf(PayrollCalculation calc) => input.Items.FirstOrDefault(i => i.Calculation == calc);
        bool AddNamed(PayrollCalculation calc, string? name, decimal amount, string? reference, List<string> memory)
        {
            if (amount <= 0m)
            {
                return false;
            }

            if (ItemOf(calc) is not { } item)
            {
                issues.Add(new PayrollCalcIssue("item_missing", $"falta a rubrica com a regra {Label(calc)} (RH → Rubricas): a linha não saiu.", false));
                return false;
            }

            lines.Add(new PayrollCalcLine(item.Id, item.Code, name ?? item.Name, item.Kind, calc, reference, amount, true, memory, null));
            return true;
        }

        void Add(PayrollCalculation calc, decimal amount, string? reference, List<string> memory) => AddNamed(calc, null, amount, reference, memory);

        // CA 2: salário proporcional.
        var salaryAmount = Money(salary * paidDays / divisor);
        var salaryMemory = new List<string> { $"Salário base {Brl(salary)}." };
        if (partial)
        {
            salaryMemory.Add($"Vínculo no mês de {Date(from)} a {Date(to)}: {employedDays} de {divisor} dias ({ProrationLabel(prm.Proration)}, parâmetro hr.salary_proration).");
        }

        if (inssDays > 0)
        {
            salaryMemory.Add($"Menos {inssDays} {(inssDays == 1 ? "dia" : "dias")} de afastamento pagos pela previdência.");
        }

        if (vacationDays > 0)
        {
            salaryMemory.Add($"Menos {vacationDays} {(vacationDays == 1 ? "dia" : "dias")} de férias, pagos no recibo de férias.");
        }

        if (vacations.Any(v => !v.Calculated))
        {
            issues.Add(new PayrollCalcIssue("vacation_not_calculated", "férias no mês sem cálculo: os dias saíram do salário; calcule as férias para o INSS do mês bater.", false));
        }

        salaryMemory.Add($"{Brl(salary)} × {paidDays}/{divisor} = {Brl(salaryAmount)}.");
        Add(PayrollCalculation.BaseSalary, salaryAmount, $"{paidDays} dias", salaryMemory);

        var fgtsExtra = 0m;
        if (inssDays > 0)
        {
            var leaveAmount = Money(salary * inssDays / divisor);
            var memory = new List<string>
            {
                $"Afastamento: {inssDays} {(inssDays == 1 ? "dia" : "dias")} pela previdência (a empresa paga os primeiros {prm.SickLeaveCompanyDays} dias, parâmetro hr.sick_leave_company_days).",
                $"{Brl(salary)} × {inssDays}/{divisor} = {Brl(leaveAmount)}, fora do salário do mês (informativa).",
            };
            if (accidentDays > 0 && prm.AccidentKeepsFgts)
            {
                fgtsExtra = Money(salary * accidentDays / divisor);
                memory.Add($"FGTS mantido sobre {accidentDays} {(accidentDays == 1 ? "dia" : "dias")}: {Brl(fgtsExtra)} entra na base do FGTS (parâmetro hr.accident_keeps_fgts).");
            }

            Add(PayrollCalculation.SickLeave, leaveAmount, $"{inssDays} dias", memory);
        }

        // CA 4: insalubridade × periculosidade, sem acúmulo (vale o maior) salvo o parâmetro.
        var insalubrity = 0m;
        var insalubrityMemory = new List<string>();
        if (e.InsalubrityPct is { } pct and > 0)
        {
            var minimum = tables[PayrollTaxTableKind.MinimumWage].Content.MinimumWage?.Monthly ?? 0m;
            var basis = prm.InsalubrityBase == InsalubrityBase.MinimumWage ? minimum : salary;
            insalubrity = Money(basis * pct / 100m * paidDays / divisor);
            insalubrityMemory.Add(prm.InsalubrityBase == InsalubrityBase.MinimumWage
                ? $"Base: salário mínimo de {Brl(minimum)} (tabela de {PayrollTaxRules.MonthLabel(tables[PayrollTaxTableKind.MinimumWage].ValidFrom)}, parâmetro hr.insalubrity_base)."
                : $"Base: salário base de {Brl(salary)} (parâmetro hr.insalubrity_base).");
            insalubrityMemory.Add($"{Brl(basis)} × {pct}% × {paidDays}/{divisor} = {Brl(insalubrity)}.");
        }

        var hazard = 0m;
        var hazardMemory = new List<string>();
        if (e.HazardPay)
        {
            hazard = Money(salary * prm.HazardPayPct / 100m * paidDays / divisor);
            hazardMemory.Add($"{Brl(salary)} × {Pct(prm.HazardPayPct)} × {paidDays}/{divisor} = {Brl(hazard)} (parâmetro hr.hazard_pay_pct).");
        }

        if (insalubrity > 0m && hazard > 0m && !prm.AllowancesAccumulate)
        {
            var hazardWins = hazard >= insalubrity;
            var note = $"Sem acúmulo (parâmetro hr.allowances_accumulate): insalubridade {Brl(insalubrity)} × periculosidade {Brl(hazard)}; vale o maior, a {(hazardWins ? "periculosidade" : "insalubridade")}.";
            if (hazardWins)
            {
                hazardMemory.Add(note);
                insalubrity = 0m;
            }
            else
            {
                insalubrityMemory.Add(note);
                hazard = 0m;
            }
        }

        Add(PayrollCalculation.Insalubrity, insalubrity, e.InsalubrityPct is { } p ? $"{p}%" : null, insalubrityMemory);
        Add(PayrollCalculation.HazardPay, hazard, Pct(prm.HazardPayPct), hazardMemory);

        // CA 5: faltas e atrasos não abonados do espelho fechado, em horas × valor da hora. A perda do DSR é da FLH-02b.
        if (e.AbsenceMinutes + e.LateMinutes > 0)
        {
            var hours = HourDivisor(e);
            var source = prm.HourValueDivisor > 0m ? "parâmetro hr.hour_value_divisor" : "horas mensais do cargo";
            foreach (var (calc, minutes, label) in new[] { (PayrollCalculation.Absence, e.AbsenceMinutes, "Faltas"), (PayrollCalculation.Lateness, e.LateMinutes, "Atrasos") })
            {
                if (minutes <= 0)
                {
                    continue;
                }

                var amount = Money(salary * minutes / (hours * 60m));
                Add(calc, amount, Hours(minutes), [
                    $"{label} não abonados do espelho de ponto fechado: {Hours(minutes)}.",
                    $"Valor da hora: {Brl(salary)} ÷ {hours.ToString("0.##", PtBr)} h ({source}).",
                    $"{Brl(salary)} × {minutes} min ÷ ({hours.ToString("0.##", PtBr)} h × 60) = {Brl(amount)}.",
                ]);
            }
        }

        // FLH-02b: as horas do espelho fechado (extras, adicional noturno, banco de horas, DSR e perda do DSR).
        HourLines(input, e, tables, salary, insalubrity, hazard, divisor, AddNamed, issues);

        // CA 13: avulsos (rubrica sem regra), com o motivo; entram nas bases conforme a rubrica.
        var supplied = 0m;
        foreach (var manual in e.Manual)
        {
            if (input.Items.FirstOrDefault(i => i.Id == manual.ItemId) is not { } item)
            {
                issues.Add(new PayrollCalcIssue("manual_item_missing", "um lançamento avulso usa uma rubrica que não existe mais.", false));
                continue;
            }

            if (item.Calculation == PayrollCalculation.TransportVoucherSupplied)
            {
                supplied += manual.Amount;
            }

            lines.Add(new PayrollCalcLine(item.Id, item.Code, item.Name, item.Kind, item.Calculation, manual.Reference, manual.Amount, false, [$"Lançamento avulso: {manual.Note}"], manual.Note));
        }

        // Bases (RH-02a: marcas da rubrica; o desconto marcado subtrai). A pensão marcada "Deduz da base do IRRF" vai para a dedução legal.
        decimal inssBase = 0m, fgtsBase = 0m, irrfIncome = 0m, legalExtra = 0m;
        foreach (var line in lines)
        {
            var item = input.Items.First(i => i.Id == line.ItemId);
            if (line.Calculation is PayrollCalculation.SickLeave or PayrollCalculation.TransportVoucherSupplied || line.Type == PayrollItemKind.Information)
            {
                continue;
            }

            var sign = line.Type == PayrollItemKind.Earning ? 1m : -1m;
            if (item.InssBase)
            {
                inssBase += sign * line.Amount;
            }

            if (item.FgtsBase)
            {
                fgtsBase += sign * line.Amount;
            }

            if (item.DeductsIrrfBase && line.Type == PayrollItemKind.Deduction)
            {
                legalExtra += line.Amount;
            }
            else if (item.IrrfBase)
            {
                irrfIncome += sign * line.Amount;
            }
        }

        inssBase = Math.Max(inssBase, 0m);
        irrfIncome = Math.Max(irrfIncome, 0m);
        fgtsBase = Math.Max(fgtsBase, 0m) + fgtsExtra;

        // CA 7: vale-transporte, percentual da tabela sobre o salário base proporcional, limitado ao benefício fornecido no mês.
        if (e.UsesTransportVoucher)
        {
            var vtTable = tables[PayrollTaxTableKind.TransportVoucher];
            var rate = vtTable.Content.TransportVoucher?.MaxDiscountRate ?? 0m;
            var vt = PayrollTaxRules.Round(salaryAmount * rate / 100m, vtTable.Rounding);
            var memory = new List<string> { $"{Brl(salaryAmount)} (salário do mês) × {Pct(rate)} = {Brl(vt)} (tabela de {PayrollTaxRules.MonthLabel(vtTable.ValidFrom)}, {StatusLabel(vtTable.Status)})." };
            if (supplied > 0m && vt > supplied)
            {
                memory.Add($"Limitado ao benefício fornecido no mês: {Brl(supplied)}.");
                vt = supplied;
            }

            Add(PayrollCalculation.TransportVoucher, vt, Pct(rate), memory);
        }

        // CA 6: salário-família, uma cota por filho com o direito, dentro da idade e com os documentos entregues.
        if (e.Dependents.Any(d => d.FamilyAllowance))
        {
            var faTable = tables[PayrollTaxTableKind.FamilyAllowance];
            var fa = faTable.Content.FamilyAllowance!;
            var eligible = e.Dependents.Where(d => d.FamilyAllowance && d.BirthDate.AddYears(Math.Max(prm.FamilyAllowanceMaxAge, 0)) >= start).ToList();
            var withDocs = eligible.Count(d => d.FamilyAllowanceDocsOk);
            if (withDocs < eligible.Count)
            {
                issues.Add(new PayrollCalcIssue("family_allowance_docs", "documentos do salário-família pendentes.", false));
            }

            if (withDocs > 0 && inssBase <= fa.MaxRemuneration)
            {
                var amount = PayrollTaxRules.Round(withDocs * fa.QuotaPerChild * (partial ? (decimal)employedDays / divisor : 1m), faTable.Rounding);
                var memory = new List<string>
                {
                    $"Remuneração do mês (base do INSS) {Brl(inssBase)}, até o limite de {Brl(fa.MaxRemuneration)}.",
                    $"{withDocs} {(withDocs == 1 ? "filho" : "filhos")} até {prm.FamilyAllowanceMaxAge} anos com os documentos entregues × {Brl(fa.QuotaPerChild)}" + (partial ? $" × {employedDays}/{divisor} dias" : string.Empty) + $" = {Brl(amount)}.",
                    $"Tabela de {PayrollTaxRules.MonthLabel(faTable.ValidFrom)} ({StatusLabel(faTable.Status)}).",
                };
                Add(PayrollCalculation.FamilyAllowance, amount, $"{withDocs} {(withDocs == 1 ? "cota" : "cotas")}", memory);
            }
        }

        // FLH-03 CA 10 (hr.vacation_taxation): a parte das férias do mês entra na base do INSS e do FGTS junto com o salário; o INSS já
        // retido no recibo de férias é compensado. O IRRF das férias foi em separado no pagamento: fica fora do IRRF do mês.
        var vacationInssBase = vacations.Sum(v => v.InssBase);
        var vacationWithheld = vacations.Sum(v => v.InssWithheld);
        inssBase += vacationInssBase;
        fgtsBase += vacations.Sum(v => v.FgtsBase);

        // CA 8: INSS progressivo, limitado ao teto, arredondado pela regra da tabela.
        var inssTable = tables[PayrollTaxTableKind.Inss];
        var inssResult = PayrollTaxRules.Inss(inssTable.Content.Inss!, inssTable.Rounding, inssBase);
        var inss = Math.Max(inssResult.Amount - vacationWithheld, 0m);
        List<string> inssMemory = [$"Base do INSS: {Brl(inssBase)} (linhas marcadas \"Base do INSS\"" + (vacationInssBase > 0m ? $", com {Brl(vacationInssBase)} das férias do mês" : string.Empty) + ").", .. inssResult.Memory];
        if (vacationWithheld > 0m)
        {
            inssMemory.Add($"Menos {Brl(vacationWithheld)} já descontados no recibo de férias: {Brl(inssResult.Amount)} − {Brl(vacationWithheld)} = {Brl(inss)} (parâmetro hr.vacation_taxation).");
        }

        inssMemory.Add($"Tabela INSS de {PayrollTaxRules.MonthLabel(inssTable.ValidFrom)} ({StatusLabel(inssTable.Status)}).");
        Add(PayrollCalculation.Inss, inss, inssBase > 0m ? Pct(decimal.Round(inssResult.Amount / inssBase * 100m, 2, MidpointRounding.AwayFromZero)) : null, inssMemory);

        // CA 9: IRRF com a melhor dedução (legal × simplificado), faixa, parcela a deduzir e a redução adicional; nunca negativo.
        var irrfTable = tables[PayrollTaxTableKind.Irrf];
        var irrfContent = irrfTable.Content.Irrf!;
        var dependents = e.Dependents.Count(d => d.IrDependent);
        var legal = inss + (dependents * irrfContent.DependentDeduction) + legalExtra;
        var byLegal = IrrfOption(irrfContent, irrfTable.Rounding, irrfIncome, legal, prm.ReductionIncome);
        var bySimplified = IrrfOption(irrfContent, irrfTable.Rounding, irrfIncome, irrfContent.SimplifiedDiscount, prm.ReductionIncome);
        var simplified = bySimplified.Final < byLegal.Final;
        var chosen = simplified ? bySimplified : byLegal;
        var other = simplified ? byLegal : bySimplified;
        var legalText = $"dedução legal: INSS {Brl(inss)} + {dependents} {(dependents == 1 ? "dependente" : "dependentes")} × {Brl(irrfContent.DependentDeduction)}" + (legalExtra > 0m ? $" + {Brl(legalExtra)} de rubricas que deduzem da base" : string.Empty) + $" = {Brl(legal)}";
        var simplifiedText = $"desconto simplificado de {Brl(irrfContent.SimplifiedDiscount)}";
        List<string> irrfMemory =
        [
            $"Rendimento tributável: {Brl(irrfIncome)} (linhas marcadas \"Base do IRRF\").",
            $"Escolhido: {(simplified ? simplifiedText : legalText)}, o que dá o menor imposto.",
            .. chosen.Memory,
            $"Pela outra opção ({(simplified ? legalText : simplifiedText)}), o imposto seria {Brl(other.Final)}.",
            $"Tabela IRRF de {PayrollTaxRules.MonthLabel(irrfTable.ValidFrom)} ({StatusLabel(irrfTable.Status)}).",
        ];
        var irrf = chosen.Final;
        Add(PayrollCalculation.Irrf, irrf, null, irrfMemory);

        // CA 10: FGTS do mês (informativo) e os encargos da empresa da tabela.
        var fgtsTable = tables[PayrollTaxTableKind.Fgts];
        var fgtsRate = fgtsTable.Content.Fgts?.MonthlyRate ?? 0m;
        var fgts = PayrollTaxRules.Round(fgtsBase * fgtsRate / 100m, fgtsTable.Rounding);
        Add(PayrollCalculation.Fgts, fgts, Pct(fgtsRate), [
            $"Base do FGTS: {Brl(fgtsBase)} (linhas marcadas \"Base do FGTS\"" + (fgtsExtra > 0m ? $", com {Brl(fgtsExtra)} do afastamento por acidente" : string.Empty) + ").",
            $"{Brl(fgtsBase)} × {Pct(fgtsRate)} = {Brl(fgts)} (informativo: depósito da empresa).",
            $"Tabela FGTS de {PayrollTaxRules.MonthLabel(fgtsTable.ValidFrom)} ({StatusLabel(fgtsTable.Status)}).",
        ]);
        var chargesTable = tables[PayrollTaxTableKind.EmployerCharges];
        var charges = PayrollTaxRules.EmployerCharges(chargesTable.Content.EmployerCharges!, chargesTable.Rounding, inssBase);

        var ordered = lines.Select((l, i) => (Line: l, Index: i)).OrderBy(x => x.Line.Calculated ? Order(x.Line.Calculation) : ManualOrder).ThenBy(x => x.Index).Select(x => x.Line).ToList();
        var totals = PayrollRules.Totals(ordered.Select(l => new PayslipLineValue(l.Type, l.Amount, l.Reference)));
        if (totals.Net < 0m)
        {
            issues.Add(new PayrollCalcIssue("negative_net", "líquido negativo: confira os avulsos e as faltas antes de fechar.", false));
        }

        return new PayrollCalcResult(ordered, totals, inssBase, chosen.Base, fgtsBase, fgts, inss, irrf, charges.Amount,
            [.. charges.Memory, $"Tabela de encargos de {PayrollTaxRules.MonthLabel(chargesTable.ValidFrom)} ({StatusLabel(chargesTable.Status)})."], issues, tableIds);
    }

    /// <summary>
    /// FLH-02b: as linhas das horas do espelho fechado, na ordem: horas extras por percentual (CA 1; o feriado sem rubrica própria usa a de
    /// domingo ou folga), adicional noturno (CA 3, só ligado), banco de horas pago ou descontado pela decisão do RH (CA 2), DSR sobre as
    /// variáveis (CA 4: extras, adicional noturno e o banco pago) e a perda do DSR por semana com falta injustificada (CA 5). O valor da hora
    /// das extras é (salário + adicionais do mês, pelo parâmetro) ÷ o divisor; o desconto do banco usa só o salário.
    /// </summary>
    private static void HourLines(PayrollCalcInput input, PayrollCalcSnapshot e, Dictionary<PayrollTaxTableKind, PayrollCalcTable> tables, decimal salary, decimal insalubrity, decimal hazard,
        int divisor, Func<PayrollCalculation, string?, decimal, string?, List<string>, bool> add, List<PayrollCalcIssue> issues)
    {
        var prm = e.Parameters;
        var rules = prm.HourRules;
        var days = e.HourDays ?? [];
        var bank = e.HourBank;
        var nightMinutes = rules.NightEnabled ? days.Sum(d => d.NightMinutes) : 0;
        if (!days.Any(d => d.OvertimeMinutes > 0 || d.UnjustifiedAbsence) && nightMinutes == 0 && (bank?.DecisionMinutes ?? 0) == 0)
        {
            return;
        }

        var hourDivisor = HourDivisor(e);
        var source = prm.HourValueDivisor > 0m ? "parâmetro hr.hour_value_divisor" : "horas mensais do cargo";
        var allowances = new List<(string Label, decimal Amount)>();
        if (rules.OvertimeIncludesAllowances)
        {
            if (insalubrity > 0m && e.InsalubrityPct is { } pct)
            {
                var basis = prm.InsalubrityBase == InsalubrityBase.MinimumWage ? tables[PayrollTaxTableKind.MinimumWage].Content.MinimumWage?.Monthly ?? 0m : salary;
                allowances.Add(("insalubridade", Money(basis * pct / 100m)));
            }

            if (hazard > 0m)
            {
                allowances.Add(("periculosidade", Money(salary * prm.HazardPayPct / 100m)));
            }
        }

        var hourBase = salary + allowances.Sum(a => a.Amount);
        var divisorText = hourDivisor.ToString("0.##", PtBr) + " h";
        string HourValue(decimal basis, bool withAllowances) => withAllowances && allowances.Count > 0
            ? $"Valor da hora: ({Brl(salary)} + {string.Join(" + ", allowances.Select(a => $"{a.Label} {Brl(a.Amount)}"))}) ÷ {divisorText} ({source}; adicionais pelo parâmetro hr.overtime_base_includes_allowances)."
            : $"Valor da hora: {Brl(basis)} ÷ {divisorText} ({source}).";
        decimal Amount(decimal basis, decimal minutes, decimal pct) => Money(basis * minutes * pct / (hourDivisor * 60m * 100m));
        var variables = 0m;

        // CA 1: extras por classe do dia, cada percentual numa linha "Horas extras {percentual}%".
        var buckets = new List<(PayrollCalculation Calc, decimal Pct, List<PayrollCalcHourDay> Days)>();
        foreach (var kind in new[] { PayrollDayKind.Workday, PayrollDayKind.RestDay, PayrollDayKind.Holiday })
        {
            var ofKind = days.Where(d => d.Kind == kind && d.OvertimeMinutes > 0).ToList();
            if (ofKind.Count == 0)
            {
                continue;
            }

            var (calc, pct) = kind switch
            {
                PayrollDayKind.Workday => (PayrollCalculation.OvertimeWorkday, rules.OvertimeWorkdayPct),
                PayrollDayKind.RestDay => (PayrollCalculation.OvertimeRestDay, rules.OvertimeRestDayPct),
                _ => (input.Items.Any(i => i.Calculation == PayrollCalculation.OvertimeHoliday) ? PayrollCalculation.OvertimeHoliday : PayrollCalculation.OvertimeRestDay, rules.OvertimeHolidayPct),
            };
            var index = buckets.FindIndex(b => b.Calc == calc && b.Pct == pct);
            if (index >= 0)
            {
                buckets[index].Days.AddRange(ofKind);
            }
            else
            {
                buckets.Add((calc, pct, ofKind));
            }
        }

        foreach (var (calc, pct, list) in buckets)
        {
            var minutes = list.Sum(d => d.OvertimeMinutes);
            var amount = Amount(hourBase, minutes, 100m + pct);
            var kinds = list.Select(d => d.Kind).Distinct().ToList();
            List<string> memory =
            [
                $"Horas extras do espelho de ponto fechado: {Hours(minutes)} em {string.Join(", ", list.OrderBy(d => d.Day).Select(d => $"{PayrollHours.DayText(d.Day, d.OvertimeMinutes)} {PayrollHours.KindLabel(d.Kind)}"))}.",
                $"Percentual de {Pct(pct)} em {string.Join(" e ", kinds.Select(PayrollHours.KindLabel))} (parâmetro {string.Join(", ", kinds.Select(RateKey))}).",
                HourValue(hourBase, true),
                $"{Brl(hourBase)} × {minutes} min ÷ ({divisorText} × 60) × (1 + {Pct(pct)}) = {Brl(amount)}.",
            ];
            if (add(calc, $"Horas extras {Pct(pct)}", amount, Hours(minutes), memory))
            {
                variables += amount;
            }
        }

        // CA 3: adicional noturno (ligado pelo parâmetro), com a hora reduzida de 52 min 30 s.
        if (nightMinutes > 0)
        {
            var counted = rules.NightReducedHour ? nightMinutes * 60m / 52.5m : nightMinutes;
            var countedMinutes = (int)decimal.Round(counted, 0, MidpointRounding.AwayFromZero);
            var amount = Amount(hourBase, counted, rules.NightPct);
            List<string> memory =
            [
                $"Horas no horário noturno ({rules.NightStartHour} h às {rules.NightEndHour} h, parâmetro hr.night_shift.start e hr.night_shift.end): {Hours(nightMinutes)} em {string.Join(", ", days.Where(d => d.NightMinutes > 0).Select(d => PayrollHours.DayText(d.Day, d.NightMinutes)))}.",
                rules.NightReducedHour
                    ? $"Hora noturna reduzida de 52 min 30 s (parâmetro hr.night_shift.reduced_hour): {nightMinutes} min × 60 ÷ 52,5 = {Hours(countedMinutes)}."
                    : "Sem a hora reduzida (parâmetro hr.night_shift.reduced_hour desligado).",
                HourValue(hourBase, true),
                $"{Brl(hourBase)} × {counted.ToString("0.##", PtBr)} min ÷ ({divisorText} × 60) × {Pct(rules.NightPct)} = {Brl(amount)} (parâmetro hr.night_shift.pct).",
            ];
            if (add(PayrollCalculation.NightShift, null, amount, Hours(countedMinutes), memory))
            {
                variables += amount;
            }
        }

        // CA 2: banco de horas sem vencimento: só sai pela decisão do RH na folha (pagar como extra de dia útil ou descontar pelo valor da hora).
        if (bank is { DecisionMinutes: not 0 } decision)
        {
            var minutes = Math.Abs(decision.DecisionMinutes);
            var balance = $"saldo do espelho fechado {TimesheetRules.WithSign(decision.BalanceMinutes)}";
            if (decision.DecisionMinutes > 0)
            {
                var amount = Amount(hourBase, minutes, 100m + rules.OvertimeWorkdayPct);
                if (add(PayrollCalculation.HourBankPayout, null, amount, Hours(minutes), [
                        $"Decisão do RH na folha: pagar {Hours(minutes)} do banco de horas ({balance}). Motivo: {decision.DecisionNote}",
                        $"Pagas como horas extras de dia útil, {Pct(rules.OvertimeWorkdayPct)} (parâmetro hr.overtime_rates.weekday).",
                        HourValue(hourBase, true),
                        $"{Brl(hourBase)} × {minutes} min ÷ ({divisorText} × 60) × (1 + {Pct(rules.OvertimeWorkdayPct)}) = {Brl(amount)}.",
                    ]))
                {
                    variables += amount;
                }
            }
            else
            {
                var amount = Amount(salary, minutes, 100m);
                add(PayrollCalculation.HourBankDiscount, null, amount, Hours(minutes), [
                    $"Decisão do RH na folha: descontar {Hours(minutes)} do banco de horas ({balance}). Motivo: {decision.DecisionNote}",
                    HourValue(salary, false),
                    $"{Brl(salary)} × {minutes} min ÷ ({divisorText} × 60) = {Brl(amount)}.",
                ]);
            }
        }

        // CA 4: DSR sobre as variáveis = variáveis ÷ dias úteis × (domingos + feriados), com os dias do calendário padrão.
        var start = new DateOnly(input.Period.Year, input.Period.Month, 1);
        if (variables > 0m)
        {
            var (useful, rest) = PayrollHours.DsrDays(start, e.Calendar, rules.DsrFormula);
            if (useful <= 0 || rest <= 0)
            {
                issues.Add(new PayrollCalcIssue("dsr_days", "o mês não tem dias úteis e de descanso pela fórmula do DSR: o DSR sobre variáveis não saiu.", false));
            }
            else
            {
                var dsr = Money(variables * rest / useful);
                var holidays = (e.Calendar?.Holidays ?? []).Where(h => h.Year == start.Year && h.Month == start.Month).Order().ToList();
                List<string> memory =
                [
                    $"Variáveis do mês (horas extras, adicional noturno e banco de horas pago): {Brl(variables)}.",
                    $"Dias do mês: {useful} úteis e {rest} de descanso ({PayrollHours.FormulaLabel(rules.DsrFormula)}, parâmetro hr.dsr_formula).",
                    holidays.Count > 0 ? $"Feriados e paradas do calendário padrão no mês: {string.Join(", ", holidays.Select(Date))}." : "Sem feriado no calendário padrão do mês.",
                    $"{Brl(variables)} ÷ {useful} × {rest} = {Brl(dsr)}.",
                ];
                add(PayrollCalculation.Dsr, "DSR sobre variáveis", dsr, $"{rest} dias", memory);
            }
        }

        // CA 5: a falta injustificada (sem abono aprovado) tira o repouso da semana, um dia de salário por semana.
        if (rules.DsrLossOnAbsence)
        {
            foreach (var week in days.Where(d => d.UnjustifiedAbsence).GroupBy(d => PayrollHours.WeekStart(d.Day)).OrderBy(g => g.Key))
            {
                var amount = Money(salary / divisor);
                add(PayrollCalculation.DsrLoss, $"DSR da semana de {Date(week.Key)}", amount, "1 dia", [
                    $"Falta injustificada na semana de {Date(week.Key)} a {Date(week.Key.AddDays(6))}: {string.Join(", ", week.Select(d => Date(d.Day)))} (espelho fechado, sem abono aprovado).",
                    "Sem a semana inteira trabalhada, o repouso da semana não é pago (Lei 605/1949, art. 6º; parâmetro hr.dsr_loss_on_absence).",
                    $"Valor do dia: {Brl(salary)} ÷ {divisor} = {Brl(amount)}.",
                ]);
            }
        }
    }

    private static string RateKey(PayrollDayKind kind) => kind switch
    {
        PayrollDayKind.Workday => "hr.overtime_rates.weekday",
        PayrollDayKind.RestDay => "hr.overtime_rates.rest_day",
        _ => "hr.overtime_rates.holiday",
    };

    /// <summary>Uma opção do IRRF (CA 9): base, imposto da faixa, a redução adicional pelo rendimento do parâmetro e o arredondamento.</summary>
    public static (decimal Base, decimal Final, IReadOnlyList<string> Memory) IrrfOption(IrrfContent irrf, PayrollRounding rounding, decimal income, decimal deductions, IrrfReductionIncome mode)
    {
        ArgumentNullException.ThrowIfNull(irrf);
        var memory = new List<string>();
        var basis = Math.Max(income - deductions, 0m);
        memory.Add($"Base de cálculo: {Brl(income)} − {Brl(deductions)} = {Brl(basis)}.");
        var index = 0;
        while (index < irrf.Brackets.Count - 1 && irrf.Brackets[index].UpTo is { } upTo && basis > upTo)
        {
            index++;
        }

        var bracket = irrf.Brackets[index];
        var tax = Math.Max((basis * bracket.Rate / 100m) - bracket.Deduction, 0m);
        memory.Add($"Faixa {index + 1}: {Brl(basis)} × {Pct(bracket.Rate)} − parcela a deduzir {Brl(bracket.Deduction)} = {Brl(decimal.Round(tax, 2, MidpointRounding.AwayFromZero))}.");
        var reductionIncome = mode == IrrfReductionIncome.GrossTaxable ? income : basis;
        var reduction = 0m;
        foreach (var r in irrf.Reductions ?? [])
        {
            if (r.UpTo is null || reductionIncome <= r.UpTo)
            {
                reduction = Math.Clamp(r.FixedAmount - (r.Rate / 100m * reductionIncome), 0m, tax);
                memory.Add((r.Rate == 0m
                    ? $"Redução adicional (rendimento de {Brl(reductionIncome)}, até {Brl(r.UpTo ?? reductionIncome)}): até {Brl(r.FixedAmount)}"
                    : $"Redução adicional: {Brl(r.FixedAmount)} − {Pct(r.Rate)} × {Brl(reductionIncome)}") + $", limitada ao imposto → {Brl(decimal.Round(reduction, 2, MidpointRounding.AwayFromZero))}.");
                break;
            }
        }

        var final = Math.Max(PayrollTaxRules.Round(tax - reduction, rounding), 0m);
        memory.Add($"Imposto: {PayrollTaxRules.RoundingLabel(rounding)} → {Brl(final)}.");
        return (basis, final, memory);
    }

    public static string Label(PayrollCalculation calculation) => calculation switch
    {
        PayrollCalculation.BaseSalary => "Salário",
        PayrollCalculation.SickLeave => "Afastamento pela previdência",
        PayrollCalculation.Insalubrity => "Insalubridade",
        PayrollCalculation.HazardPay => "Periculosidade",
        PayrollCalculation.Absence => "Faltas",
        PayrollCalculation.Lateness => "Atrasos",
        PayrollCalculation.FamilyAllowance => "Salário-família",
        PayrollCalculation.TransportVoucher => "Vale-transporte (desconto)",
        PayrollCalculation.TransportVoucherSupplied => "Vale-transporte fornecido",
        PayrollCalculation.Inss => "INSS",
        PayrollCalculation.Irrf => "IRRF",
        PayrollCalculation.Fgts => "FGTS",
        PayrollCalculation.OvertimeWorkday => "Horas extras em dia útil",
        PayrollCalculation.OvertimeRestDay => "Horas extras em domingo ou folga",
        PayrollCalculation.OvertimeHoliday => "Horas extras em feriado",
        PayrollCalculation.NightShift => "Adicional noturno",
        PayrollCalculation.Dsr => "DSR sobre variáveis",
        PayrollCalculation.DsrLoss => "Perda do DSR",
        PayrollCalculation.HourBankPayout => "Banco de horas pago",
        PayrollCalculation.HourBankDiscount => "Banco de horas descontado",
        PayrollCalculation.VacationPay => "Férias",
        PayrollCalculation.VacationAverages => "Médias de férias",
        PayrollCalculation.VacationThird => "1/3 de férias",
        PayrollCalculation.VacationSale => "Abono pecuniário",
        PayrollCalculation.VacationSaleThird => "1/3 do abono pecuniário",
        _ => "Sem regra (avulso)",
    };

    public static string StatusLabel(PayrollTaxTableStatus status) => status switch
    {
        PayrollTaxTableStatus.Active => "conferida",
        PayrollTaxTableStatus.Superseded => "substituída",
        _ => "a confirmar com a contadora",
    };

    private static string ProrationLabel(SalaryProration proration) => proration == SalaryProration.CommercialMonth ? "mês comercial de 30 dias" : "dias do mês";

    private static decimal HourDivisor(PayrollCalcSnapshot e) => e.Parameters.HourValueDivisor > 0m ? e.Parameters.HourValueDivisor : e.MonthlyHours;

    private static int Order(PayrollCalculation calculation) => calculation switch
    {
        PayrollCalculation.BaseSalary => 0,
        PayrollCalculation.SickLeave => 1,
        PayrollCalculation.Insalubrity => 2,
        PayrollCalculation.HazardPay => 3,
        PayrollCalculation.OvertimeWorkday => 4,
        PayrollCalculation.OvertimeRestDay => 5,
        PayrollCalculation.OvertimeHoliday => 6,
        PayrollCalculation.NightShift => 7,
        PayrollCalculation.HourBankPayout => 8,
        PayrollCalculation.Dsr => 9,
        PayrollCalculation.Absence => 10,
        PayrollCalculation.Lateness => 11,
        PayrollCalculation.DsrLoss => 12,
        PayrollCalculation.HourBankDiscount => 13,
        PayrollCalculation.FamilyAllowance => 15,
        PayrollCalculation.TransportVoucher => 16,
        PayrollCalculation.Inss => 17,
        PayrollCalculation.Irrf => 18,
        PayrollCalculation.Fgts => 19,
        _ => ManualOrder,
    };

    /// <summary>Os avulsos ficam depois dos descontos do ponto e antes do salário-família.</summary>
    private const int ManualOrder = 14;

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Mensagens e códigos da folha calculada (FLH-02a), sem banco.</summary>
public static class PayrollCalculationRules
{
    public const string TimesheetOpenCode = "payroll_timesheet_open";
    public const string NotCalculatedCode = "payroll_run_not_calculated";
    public const string NotSupportedCode = "payroll_calculation_not_supported";
    public const string ItemMissingCode = "payroll_calculation_item_missing";
    public const string SimulationCode = "payroll_calculated_simulation";
    public const string UnconfirmedTablesCode = "payroll_tables_unconfirmed";
    public const string CalculatedLineCode = "payslip_calculated_line";
    public const string CalculatingCode = "payroll_calculating";
    public const string NothingToRectifyCode = "payroll_nothing_to_rectify";
    public const string RectificationOpenCode = "payroll_rectification_open";
    public const string NotClosedCode = "payroll_run_not_closed";

    // FLH-02b: percentuais das extras apagados bloqueiam o cálculo; decisão do banco de horas na folha.
    public const string OvertimeRatesMissingCode = "payroll_overtime_rates_missing";
    public const string OvertimeRatesMissingMessage = "Cadastre os percentuais de horas extras em Configurações → Parâmetros → RH.";
    public const string HourBankInvalidCode = "payroll_hour_bank_invalid";

    /// <summary>Com o saldo positivo acima disso, a seção do banco sugere "Pagar {n} h do banco" (Propositivo, sem decidir).</summary>
    public const int HourBankSuggestionMinutes = 40 * 60;

    public const string SimulationMessage = "Folha calculada em conferência paralela: feche a folha lançada.";
    public const string CalculatedLineMessage = "Na folha calculada, a linha calculada não é editada à mão: lance um avulso com o motivo e clique em \"Recalcular\".";

    /// <summary>Acima disso, o cálculo vai para o segundo plano (CA 17), como o lote de holerites (RH-02b).</summary>
    public const int InlineLimit = 20;

    public const int NoteMin = 5;
    public const int NoteMax = 200;

    public static string TimesheetOpenMessage(DateOnly period) => $"Feche o ponto de {PayrollRules.PeriodLabel(period)} antes de calcular.";

    public static string UnconfirmedTablesMessage(IEnumerable<string> tables) => $"Confirme com a contadora as tabelas {string.Join(", ", tables)} antes de fechar.";

    public static string StaleMessage(int employees) => $"Cadastro ou tabela mudou depois do cálculo de {employees} {(employees == 1 ? "funcionário" : "funcionários")}.";

    /// <summary>
    /// Diferença por rubrica da retificação (CA 14): o que aumentou entra com o mesmo tipo; o provento que diminuiu vira desconto (e o
    /// desconto que diminuiu, provento), com o valor da diferença; a informativa sai com o valor absoluto.
    /// </summary>
    public static (PayrollItemKind Type, decimal Amount, bool Reduced)? Difference(PayrollItemKind kind, decimal before, decimal after)
    {
        var diff = after - before;
        if (diff == 0m)
        {
            return null;
        }

        if (kind == PayrollItemKind.Information || diff > 0m)
        {
            return (kind, Math.Abs(diff), diff < 0m);
        }

        return (kind == PayrollItemKind.Earning ? PayrollItemKind.Deduction : PayrollItemKind.Earning, -diff, true);
    }
}
