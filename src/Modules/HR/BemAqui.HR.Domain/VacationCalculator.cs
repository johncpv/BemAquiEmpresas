using System.Globalization;
using System.Text.Json;

namespace BemAqui.HR.Domain;

/// <summary>Variável de um mês do período aquisitivo (CA 6, <c>hr.vacation_averages</c>): a soma dos proventos variáveis da folha fechada.</summary>
public sealed record VacationCalcVariable(DateOnly Month, decimal Amount);

/// <summary>
/// Retrato das entradas da folha de férias (ADR 0039 §2, <c>inputs_snapshot</c>, <c>[PersonalData]</c>): salário e adicionais fixos, dependentes
/// do IR (só a contagem), o período aquisitivo e o concessivo, a programação, a data do pagamento, as variáveis do período e os parâmetros.
/// </summary>
public sealed record VacationCalcSnapshot(
    decimal BaseSalary,
    DateOnly Admission,
    int? InsalubrityPct,
    bool HazardPay,
    int IrDependents,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly ConcessionEnd,
    DateOnly Start,
    int Days,
    int SoldDays,
    bool Advance13th,
    DateOnly PaymentDate,
    IReadOnlyList<VacationCalcVariable> Variables,
    PayrollCalcParameters Parameters,
    VacationParameters Vacation);

/// <summary>
/// Entrada do cálculo: o retrato, as rubricas, as tabelas da competência do pagamento (IRRF, FGTS, salário mínimo) e a tabela do INSS de
/// cada competência do gozo.
/// </summary>
public sealed record VacationCalcInput(VacationCalcSnapshot Employee, IReadOnlyList<PayrollCalcItem> Items, IReadOnlyList<PayrollCalcTable> Tables, IReadOnlyDictionary<DateOnly, PayrollCalcTable> InssByMonth);

/// <summary>A parte das férias numa competência (CA 6 e 10): dias de gozo, base e INSS retido, base do FGTS.</summary>
public sealed record VacationMonthSplit(DateOnly Month, int Days, decimal InssBase, decimal Inss, decimal FgtsBase);

public sealed record VacationCalcResult(PayrollCalcResult Payslip, IReadOnlyList<VacationMonthSplit> Split);

/// <summary>
/// Motor da folha de férias (FLH-03 CA 6, ADR 0039 §2): função pura, como o <see cref="PayrollCalculator"/>, e com as mesmas regras de
/// imposto (INSS progressivo e IRRF com a melhor dedução e a redução, das tabelas da FLH-01). Remuneração dos dias (salário + adicionais fixos)
/// e a média das variáveis dos meses do período aquisitivo (<c>hr.vacation_averages</c>), em linhas separadas; o 1/3 constitucional sobre as
/// duas; o abono pecuniário e o seu 1/3; os dias gozados depois do fim do concessivo em dobro (CLT art. 137). As bases seguem as marcas da
/// rubrica (o abono e o seu 1/3 sem INSS, FGTS nem IRRF no seed). INSS rateado pelas competências do gozo (<c>hr.vacation_taxation</c>), com
/// a tabela de cada uma; o IRRF em separado, pela tabela da competência do pagamento; o FGTS e os encargos ficam informativos (entram na
/// folha mensal da competência, CA 10). Cada valor em 2 casas (AwayFromZero); os impostos pela regra da tabela.
/// </summary>
public static class VacationCalculator
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly PayrollCalculation[] LineOrder =
    [
        PayrollCalculation.VacationPay, PayrollCalculation.VacationAverages, PayrollCalculation.VacationThird, PayrollCalculation.VacationSale, PayrollCalculation.VacationSaleThird,
        PayrollCalculation.Inss, PayrollCalculation.Irrf, PayrollCalculation.Fgts,
    ];

    /// <summary>As regras que a folha de férias exige com rubrica ativa.</summary>
    public static readonly PayrollCalculation[] Required = [PayrollCalculation.VacationPay, PayrollCalculation.VacationThird, PayrollCalculation.Inss, PayrollCalculation.Irrf];

    public static string WriteSnapshot(VacationCalcSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Json);

    public static VacationCalcSnapshot? ReadSnapshot(string? json) => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<VacationCalcSnapshot>(json, Json);

    private static string Brl(decimal value) => PayrollCalculator.Brl(value);

    private static string Pct(decimal value) => value.ToString("0.####", PtBr) + "%";

    private static string Date(DateOnly d) => VacationPolicy.Date(d);

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Adicionais fixos do mês (insalubridade × periculosidade, sem acúmulo salvo o parâmetro), como na folha mensal (FLH-02a CA 4).</summary>
    public static (decimal Amount, IReadOnlyList<string> Memory) FixedAllowances(decimal salary, int? insalubrityPct, bool hazardPay, decimal minimumWage, PayrollCalcParameters prm)
    {
        ArgumentNullException.ThrowIfNull(prm);
        var memory = new List<string>();
        var insalubrity = 0m;
        if (insalubrityPct is { } pct and > 0)
        {
            var basis = prm.InsalubrityBase == InsalubrityBase.MinimumWage ? minimumWage : salary;
            insalubrity = Money(basis * pct / 100m);
            memory.Add($"Insalubridade: {Brl(basis)} × {pct}% = {Brl(insalubrity)} (parâmetro hr.insalubrity_base).");
        }

        var hazard = hazardPay ? Money(salary * prm.HazardPayPct / 100m) : 0m;
        if (hazard > 0m)
        {
            memory.Add($"Periculosidade: {Brl(salary)} × {Pct(prm.HazardPayPct)} = {Brl(hazard)} (parâmetro hr.hazard_pay_pct).");
        }

        if (insalubrity > 0m && hazard > 0m && !prm.AllowancesAccumulate)
        {
            memory.Add("Sem acúmulo (parâmetro hr.allowances_accumulate): vale o maior.");
            return (Math.Max(insalubrity, hazard), memory);
        }

        return (insalubrity + hazard, memory);
    }

    public static VacationCalcResult Calculate(VacationCalcInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var e = input.Employee;
        var prm = e.Parameters;
        var vp = e.Vacation;
        var issues = new List<PayrollCalcIssue>();
        var tableIds = input.Tables.OrderBy(t => t.Kind).Select(t => t.Id).Concat(input.InssByMonth.OrderBy(x => x.Key).Select(x => x.Value.Id)).Distinct().ToList();
        VacationCalcResult Blocked(string code, string message) =>
            new(new PayrollCalcResult([], new PayslipTotals(0m, 0m, 0m), 0m, 0m, 0m, 0m, 0m, 0m, 0m, [], [.. issues, new PayrollCalcIssue(code, message, true)], tableIds), []);

        var tables = input.Tables.ToDictionary(t => t.Kind);
        foreach (var kind in new[] { PayrollTaxTableKind.Irrf, PayrollTaxTableKind.Fgts, PayrollTaxTableKind.MinimumWage })
        {
            if (!tables.ContainsKey(kind))
            {
                return Blocked("table_missing", PayrollTaxRules.MissingMessage(kind, new DateOnly(e.PaymentDate.Year, e.PaymentDate.Month, 1)));
            }
        }

        if (e.BaseSalary <= 0m)
        {
            return Blocked("no_salary", "Sem salário: informe o salário base no cadastro do funcionário.");
        }

        if (e.Days < 1)
        {
            return Blocked("no_days", "A programação não tem dias de gozo.");
        }

        var end = e.Start.AddDays(e.Days - 1);
        var months = VacationPolicy.DaysByMonth(e.Start, end);
        if (months.FirstOrDefault(m => !input.InssByMonth.ContainsKey(m.Month)) is { Days: > 0 } missingMonth)
        {
            return Blocked("table_missing", PayrollTaxRules.MissingMessage(PayrollTaxTableKind.Inss, missingMonth.Month));
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

        // Remuneração do mês: salário + adicionais fixos; a média das variáveis dos meses do período aquisitivo.
        var minimum = tables[PayrollTaxTableKind.MinimumWage].Content.MinimumWage?.Monthly ?? 0m;
        var (fixedAllowances, fixedMemory) = FixedAllowances(e.BaseSalary, e.InsalubrityPct, e.HazardPay, minimum, prm);
        var monthly = e.BaseSalary + fixedAllowances;
        var months12 = Math.Max(vp.AverageMonths, 1);
        var variables = e.Variables.Where(v => v.Amount > 0m).OrderBy(v => v.Month).ToList();
        var average = Money(variables.Sum(v => v.Amount) / months12);

        // CLT art. 137: os dias gozados depois do fim do concessivo são pagos em dobro.
        var doubled = end > e.ConcessionEnd ? end.DayNumber - Math.Max(e.ConcessionEnd.DayNumber, e.Start.DayNumber - 1) : 0;
        var payDays = e.Days + doubled;
        var pay = Money(monthly * payDays / 30m);
        List<string> payMemory = [$"Salário base {Brl(e.BaseSalary)}" + (fixedAllowances > 0m ? $" + adicionais fixos {Brl(fixedAllowances)} = {Brl(monthly)}" : string.Empty) + ".", .. fixedMemory,
            $"Gozo de {Date(e.Start)} a {Date(end)}: {e.Days} dias (período aquisitivo de {Date(e.PeriodStart)} a {Date(e.PeriodEnd)})."];
        if (doubled > 0)
        {
            payMemory.Add($"{doubled} {(doubled == 1 ? "dia depois" : "dias depois")} do fim do período concessivo ({Date(e.ConcessionEnd)}): pagos em dobro (CLT art. 137).");
        }

        payMemory.Add($"{Brl(monthly)} × {payDays}/30 = {Brl(pay)}.");
        Add(PayrollCalculation.VacationPay, pay, $"{e.Days} dias", payMemory);

        var averagePay = Money(average * payDays / 30m);
        if (averagePay > 0m)
        {
            var averageMemory = new List<string> { $"Variáveis do período aquisitivo ({months12} meses, parâmetro hr.vacation_averages):" };
            averageMemory.AddRange(variables.Select(v => $"{PayrollRules.PeriodLabel(v.Month)}: {Brl(v.Amount)}."));
            averageMemory.Add($"Média: {Brl(variables.Sum(v => v.Amount))} ÷ {months12} = {Brl(average)}; × {payDays}/30 = {Brl(averagePay)}.");
            Add(PayrollCalculation.VacationAverages, averagePay, $"{months12} meses", averageMemory);
        }

        var third = Money((pay + averagePay) / 3m);
        Add(PayrollCalculation.VacationThird, third, "1/3", [$"1/3 constitucional: ({Brl(pay)} + {Brl(averagePay)}) ÷ 3 = {Brl(third)}."]);

        if (e.SoldDays > 0)
        {
            var sale = Money((monthly + average) * e.SoldDays / 30m);
            Add(PayrollCalculation.VacationSale, sale, $"{e.SoldDays} dias", [$"Abono pecuniário de {e.SoldDays} dias: ({Brl(monthly)} + média {Brl(average)}) × {e.SoldDays}/30 = {Brl(sale)}."]);
            var saleThird = Money(sale / 3m);
            Add(PayrollCalculation.VacationSaleThird, saleThird, "1/3", [$"1/3 do abono: {Brl(sale)} ÷ 3 = {Brl(saleThird)}."]);
        }

        if (e.Advance13th)
        {
            issues.Add(new PayrollCalcIssue("advance_13th", "adiantamento do 13º pedido: entra na folha do 13º (1ª parcela).", false));
        }

        // Bases pelas marcas da rubrica (como na folha mensal).
        decimal inssBase = 0m, fgtsBase = 0m, irrfIncome = 0m;
        foreach (var line in lines)
        {
            var item = input.Items.First(i => i.Id == line.ItemId);
            var sign = line.Type == PayrollItemKind.Earning ? 1m : line.Type == PayrollItemKind.Deduction ? -1m : 0m;
            inssBase += item.InssBase ? sign * line.Amount : 0m;
            fgtsBase += item.FgtsBase ? sign * line.Amount : 0m;
            irrfIncome += item.IrrfBase ? sign * line.Amount : 0m;
        }

        inssBase = Math.Max(inssBase, 0m);
        fgtsBase = Math.Max(fgtsBase, 0m);
        irrfIncome = Math.Max(irrfIncome, 0m);

        // INSS: rateado pelas competências do gozo (ou inteiro na do início), cada parte com a tabela da competência.
        List<(DateOnly Month, int Days)> parts = vp.Taxation == VacationTaxation.StartMonth ? [(months[0].Month, e.Days)] : [.. months];
        var split = new List<VacationMonthSplit>();
        var inssMemory = new List<string> { $"Base do INSS das férias: {Brl(inssBase)} (linhas marcadas \"Base do INSS\")." };
        decimal inss = 0m, inssUsed = 0m, fgtsUsed = 0m;
        for (var i = 0; i < parts.Count; i++)
        {
            var (month, days) = parts[i];
            var last = i == parts.Count - 1;
            var portion = last ? inssBase - inssUsed : Money(inssBase * days / e.Days);
            var fgtsPortion = last ? fgtsBase - fgtsUsed : Money(fgtsBase * days / e.Days);
            inssUsed += portion;
            fgtsUsed += fgtsPortion;
            var table = input.InssByMonth[month];
            var result = PayrollTaxRules.Inss(table.Content.Inss!, table.Rounding, portion);
            inss += result.Amount;
            split.Add(new VacationMonthSplit(month, days, portion, result.Amount, fgtsPortion));
            inssMemory.Add($"Competência {PayrollRules.PeriodLabel(month)}: {days} dias, base {Brl(portion)} → INSS {Brl(result.Amount)} (tabela INSS de {PayrollTaxRules.MonthLabel(table.ValidFrom)}, {PayrollCalculator.StatusLabel(table.Status)}).");
        }

        inssMemory.Add("Na folha mensal da competência, o INSS é refeito sobre o salário + a parte das férias e este valor é descontado (parâmetro hr.vacation_taxation).");
        Add(PayrollCalculation.Inss, inss, null, inssMemory);

        // IRRF em separado no pagamento, com a melhor dedução e a redução (as regras da folha mensal).
        var irrfTable = tables[PayrollTaxTableKind.Irrf];
        var irrfContent = irrfTable.Content.Irrf!;
        var legal = inss + (e.IrDependents * irrfContent.DependentDeduction);
        var byLegal = PayrollCalculator.IrrfOption(irrfContent, irrfTable.Rounding, irrfIncome, legal, prm.ReductionIncome);
        var bySimplified = PayrollCalculator.IrrfOption(irrfContent, irrfTable.Rounding, irrfIncome, irrfContent.SimplifiedDiscount, prm.ReductionIncome);
        var simplified = bySimplified.Final < byLegal.Final;
        var chosen = simplified ? bySimplified : byLegal;
        var other = simplified ? byLegal : bySimplified;
        var legalText = $"dedução legal: INSS {Brl(inss)} + {e.IrDependents} {(e.IrDependents == 1 ? "dependente" : "dependentes")} × {Brl(irrfContent.DependentDeduction)} = {Brl(legal)}";
        var simplifiedText = $"desconto simplificado de {Brl(irrfContent.SimplifiedDiscount)}";
        Add(PayrollCalculation.Irrf, chosen.Final, null,
        [
            $"IRRF em separado no pagamento, competência de {PayrollRules.PeriodLabel(new DateOnly(e.PaymentDate.Year, e.PaymentDate.Month, 1))} (parâmetro hr.vacation_taxation).",
            $"Rendimento tributável: {Brl(irrfIncome)} (linhas marcadas \"Base do IRRF\").",
            $"Escolhido: {(simplified ? simplifiedText : legalText)}, o que dá o menor imposto.",
            .. chosen.Memory,
            $"Pela outra opção ({(simplified ? legalText : simplifiedText)}), o imposto seria {Brl(other.Final)}.",
            $"Tabela IRRF de {PayrollTaxRules.MonthLabel(irrfTable.ValidFrom)} ({PayrollCalculator.StatusLabel(irrfTable.Status)}).",
        ]);

        var fgtsTable = tables[PayrollTaxTableKind.Fgts];
        var fgtsRate = fgtsTable.Content.Fgts?.MonthlyRate ?? 0m;
        var fgts = PayrollTaxRules.Round(fgtsBase * fgtsRate / 100m, fgtsTable.Rounding);
        Add(PayrollCalculation.Fgts, fgts, Pct(fgtsRate), [
            $"Base do FGTS: {Brl(fgtsBase)}; {Brl(fgtsBase)} × {Pct(fgtsRate)} = {Brl(fgts)} (informativo: depositado com a folha mensal da competência do gozo).",
        ]);

        var ordered = lines.OrderBy(l => Array.IndexOf(LineOrder, l.Calculation)).ToList();
        var totals = PayrollRules.Totals(ordered.Select(l => new PayslipLineValue(l.Type, l.Amount, l.Reference)));
        var payslip = new PayrollCalcResult(ordered, totals, inssBase, chosen.Base, fgtsBase, fgts, inss, chosen.Final, 0m,
            ["Os encargos da empresa sobre as férias entram na folha mensal da competência do gozo, junto com o salário."], issues, tableIds);
        return new VacationCalcResult(payslip, split);
    }
}
