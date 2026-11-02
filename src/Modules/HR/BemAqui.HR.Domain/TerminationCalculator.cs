using System.Globalization;
using System.Text.Json;

namespace BemAqui.HR.Domain;

/// <summary>Férias já programadas de um período aquisitivo (pela ordem desde a admissão): dias de gozo e vendidos.</summary>
public sealed record TerminationVacationTaken(int Sequence, int Days, int SoldDays);

/// <summary>
/// Retrato das entradas da rescisão (ADR 0039 §2, <c>inputs_snapshot</c>, <c>[PersonalData]</c>): o tipo, o aviso, a data do desligamento, o
/// fim previsto da experiência, o saldo do FGTS digitado e a data do extrato, o retrato do mês do desligamento (o da folha mensal, FLH-02a,
/// com o banco de horas da rescisão), os afastamentos e as faltas injustificadas desde a admissão (períodos de férias, FLH-03), as férias já
/// programadas, as variáveis dos últimos meses, o retrato do 13º do ano (FLH-04) e os parâmetros. Sem nome nem documento.
/// </summary>
public sealed record TerminationCalcSnapshot(
    TerminationKind Kind,
    TerminationNoticeKind NoticeKind,
    DateOnly TerminationDate,
    DateOnly? ContractEndDate,
    decimal? FgtsBalance,
    DateOnly? FgtsStatementDate,
    PayrollCalcSnapshot Month,
    IReadOnlyList<VacationLeaveInput> Leaves,
    IReadOnlyList<DateOnly> AbsenceDays,
    IReadOnlyList<TerminationVacationTaken> VacationTaken,
    IReadOnlyList<VacationCalcVariable> Variables,
    TerminationThirteenthSnapshot? Thirteenth,
    VacationParameters Vacation,
    TerminationParameters Termination);

/// <summary>Entrada do cálculo: o retrato, as rubricas e as tabelas da competência do desligamento (FLH-01).</summary>
public sealed record TerminationCalcInput(TerminationCalcSnapshot Employee, IReadOnlyList<PayrollCalcItem> Items, IReadOnlyList<PayrollCalcTable> Tables);

/// <summary>
/// Resumo da rescisão (CA 1, 4 e 8): os dias do aviso e os pagos, o fim projetado, o FGTS do mês e o rescisório, a multa (percentual, base e
/// valor, com a memória), os encargos da empresa, o custo total para a empresa (proventos + FGTS + multa + encargos) e o prazo do pagamento.
/// </summary>
public sealed record TerminationSummary(
    int NoticeDays,
    decimal PaidNoticeDays,
    DateOnly ProjectedEnd,
    decimal FgtsMonth,
    decimal FgtsTermination,
    decimal FinePct,
    decimal FineBase,
    decimal Fine,
    decimal EmployerCharges,
    decimal TotalCost,
    DateOnly PaymentDeadline,
    IReadOnlyList<string> FineMemory);

public sealed record TerminationCalcResult(PayrollCalcResult Payslip, TerminationSummary Summary);

/// <summary>
/// Motor da rescisão (FLH-05, ADR 0039 §2): função pura do Domain, sem banco nem relógio. Reusa o motor: o mês do desligamento é a folha
/// mensal (<see cref="PayrollCalculator"/>, FLH-02a/02b: o saldo de salário, os adicionais, as horas do espelho fechado, os avulsos, o INSS, o
/// IRRF e o FGTS do mês, e o saldo do banco de horas como a decisão da rescisão); as férias seguem a <see cref="VacationPolicy"/> e a
/// remuneração do <see cref="VacationCalculator"/> (FLH-03); o 13º proporcional, o <c>ThirteenthCalculator</c> (FLH-04). Por cima, a matriz
/// do tipo (<see cref="TerminationParameters"/>): o aviso indenizado (<c>NoticePay</c>) ou o desconto do não cumprido, a projeção do aviso nos
/// avos, as férias vencidas e proporcionais com 1/3 (<c>VacationOnTermination</c>), as indenizações da experiência, o FGTS rescisório e a
/// multa sobre o saldo digitado (<c>FgtsFine</c>). As incidências seguem as marcas das rubricas. Cada valor em 2 casas (AwayFromZero).
/// </summary>
public static partial class TerminationCalculator
{
    /// <summary>Versão do motor da rescisão (gravada no holerite, na folha e no histórico, ADR 0039 §2).</summary>
    public const string Version = "1.0.0";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>As regras que a rescisão exige com rubrica ativa (além das da folha mensal).</summary>
    public static readonly PayrollCalculation[] Required =
    [
        PayrollCalculation.BaseSalary, PayrollCalculation.Inss, PayrollCalculation.Irrf, PayrollCalculation.Fgts, PayrollCalculation.TerminationNotice, PayrollCalculation.TerminationVacation,
        PayrollCalculation.TerminationVacationProrated, PayrollCalculation.TerminationVacationThird, PayrollCalculation.FgtsFine,
    ];

    private static readonly PayrollCalculation[] TerminationOrder =
    [
        PayrollCalculation.TerminationNotice, PayrollCalculation.TerminationIndemnity, PayrollCalculation.TerminationVacation, PayrollCalculation.TerminationVacationProrated,
        PayrollCalculation.TerminationVacationThird,
    ];

    public static string WriteSnapshot(TerminationCalcSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Json);

    public static TerminationCalcSnapshot? ReadSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TerminationCalcSnapshot>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Brl(decimal value) => PayrollCalculator.Brl(value);

    private static string Pct(decimal value) => value.ToString("0.####", PtBr) + "%";

    private static string Date(DateOnly d) => TerminationPolicy.Date(d);

    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>A parte do 13º proporcional (FLH-04): as linhas, as bases e os impostos do 13º, separados do salário do mês.</summary>
    private sealed record ThirteenthPart(IReadOnlyList<PayrollCalcLine> Lines, decimal InssBase, decimal Inss, decimal Irrf, decimal IrrfBase, decimal EmployerCharges, IReadOnlyList<string> EmployerMemory);

    public static TerminationCalcResult Calculate(TerminationCalcInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var e = input.Employee;
        var p = e.Termination;
        var row = p.Row(e.Kind);
        var month = new DateOnly(e.TerminationDate.Year, e.TerminationDate.Month, 1);
        var monthSnapshot = e.Month with { Termination = e.TerminationDate };
        var monthResult = PayrollCalculator.Calculate(new PayrollCalcInput(month, monthSnapshot, input.Items, input.Tables));
        var noticeDays = TerminationPolicy.NoticeDays(monthSnapshot.Admission, e.TerminationDate, p);
        var paidNotice = TerminationPolicy.PaidNoticeDays(row.Notice, e.NoticeKind, noticeDays);
        var projected = TerminationPolicy.ProjectedEnd(e.TerminationDate, paidNotice);
        var deadline = TerminationPolicy.PaymentDeadline(e.TerminationDate, p);
        if (monthResult.Issues.Any(i => i.Blocking))
        {
            return new TerminationCalcResult(monthResult, new TerminationSummary(noticeDays, paidNotice, projected, 0m, 0m, row.FgtsFinePct, 0m, 0m, 0m, 0m, deadline, []));
        }

        var issues = monthResult.Issues.ToList();
        var tables = input.Tables.ToDictionary(t => t.Kind);
        var lines = new List<PayrollCalcLine>();
        void Add(PayrollCalculation calc, string? name, decimal amount, string? reference, List<string> memory)
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

            lines.Add(new PayrollCalcLine(item.Id, item.Code, name ?? item.Name, item.Kind, calc, reference, amount, true, memory, null));
        }

        // Remuneração da rescisão: o salário e os adicionais fixos do mês (a regra das férias, FLH-03) e a média das variáveis dos últimos
        // meses (hr.vacation_averages), que integram o aviso e as férias.
        var prm = monthSnapshot.Parameters;
        var salary = monthSnapshot.BaseSalary;
        var minimum = tables.GetValueOrDefault(PayrollTaxTableKind.MinimumWage)?.Content.MinimumWage?.Monthly ?? 0m;
        var (fixedAllowances, fixedMemory) = VacationCalculator.FixedAllowances(salary, monthSnapshot.InsalubrityPct, monthSnapshot.HazardPay, minimum, prm);
        var months12 = Math.Max(e.Vacation.AverageMonths, 1);
        var variables = e.Variables.Where(v => v.Amount > 0m).OrderBy(v => v.Month).ToList();
        var average = Money(variables.Sum(v => v.Amount) / months12);
        var remuneration = salary + fixedAllowances + average;
        List<string> RemunerationMemory()
        {
            var memory = new List<string> { $"Remuneração: salário {Brl(salary)}" + (fixedAllowances > 0m ? $" + adicionais fixos {Brl(fixedAllowances)}" : string.Empty)
                + (average > 0m ? $" + média das variáveis {Brl(average)}" : string.Empty) + $" = {Brl(remuneration)}." };
            memory.AddRange(fixedMemory);
            if (average > 0m)
            {
                memory.Add($"Média: {Brl(variables.Sum(v => v.Amount))} ÷ {months12} meses (parâmetro hr.vacation_averages; {string.Join(", ", variables.Select(v => $"{PayrollRules.PeriodLabel(v.Month)} {Brl(v.Amount)}"))}).");
            }

            return memory;
        }

        var kindText = $"{TerminationPolicy.KindLabel(e.Kind)} em {Date(e.TerminationDate)} (matriz hr.termination_matrix).";

        // NoticePay: o aviso indenizado pela matriz (integral, ou a metade no acordo), com os dias da Lei 12.506/2011; no pedido de demissão,
        // o aviso não cumprido é descontado pela base (os dias proporcionais são só do empregado).
        var years = TerminationPolicy.CompletedYears(monthSnapshot.Admission, e.TerminationDate);
        var noticeRule = $"Aviso de {noticeDays} dias: {p.NoticeBaseDays} + {p.NoticeDaysPerYear} × {years} {(years == 1 ? "ano completo" : "anos completos")}, até {p.NoticeMaxDays} (parâmetro hr.notice_days_rule, Lei 12.506/2011).";
        if (paidNotice > 0m)
        {
            var amount = Money(remuneration * paidNotice / 30m);
            List<string> memory = [kindText, noticeRule];
            if (row.Notice == TerminationNoticeRule.Half)
            {
                memory.Add($"Acordo entre as partes: metade do aviso indenizado, {TerminationPolicy.Days(paidNotice)} (CLT art. 484-A, I, a).");
            }

            memory.AddRange(RemunerationMemory());
            memory.Add($"{Brl(remuneration)} × {paidNotice.ToString("0.#", PtBr)}/30 = {Brl(amount)}.");
            memory.Add($"Projeção do aviso: o contrato vai até {Date(projected)} nos avos de férias e de 13º (OJ 82 da SDI-1 do TST).");
            Add(PayrollCalculation.TerminationNotice, null, amount, TerminationPolicy.Days(paidNotice), memory);
        }
        else if (row.Notice == TerminationNoticeRule.Deduction && e.NoticeKind == TerminationNoticeKind.Indemnified)
        {
            var days = p.NoticeBaseDays;
            var amount = Money((salary + fixedAllowances) * days / 30m);
            Add(PayrollCalculation.TerminationNoticeDeduction, null, amount, TerminationPolicy.Days(days), [
                kindText,
                $"Aviso prévio não cumprido pelo empregado: desconto de {days} dias (CLT art. 487 §2º; os dias proporcionais da Lei 12.506/2011 são só a favor do empregado).",
                $"({Brl(salary)}" + (fixedAllowances > 0m ? $" + {Brl(fixedAllowances)}" : string.Empty) + $") × {days}/30 = {Brl(amount)}.",
            ]);
        }

        // Contrato de experiência terminado antes do prazo: metade da remuneração dos dias restantes (CLT arts. 479 e 480).
        if (TerminationPolicy.NeedsContractEnd(e.Kind))
        {
            var remaining = e.ContractEndDate is { } end && end > e.TerminationDate ? end.DayNumber - e.TerminationDate.DayNumber : 0;
            if (remaining <= 0)
            {
                issues.Add(new PayrollCalcIssue("contract_end_missing", "sem o fim previsto do contrato de experiência depois do desligamento: a indenização dos dias restantes não saiu.", false));
            }
            else
            {
                var basis = salary + fixedAllowances;
                var amount = Money(basis / 30m * remaining / 2m);
                List<string> memory =
                [
                    kindText,
                    $"Dias restantes do contrato de experiência: de {Date(e.TerminationDate.AddDays(1))} a {Date(e.ContractEndDate!.Value)}, {remaining} dias.",
                    $"Metade da remuneração dos dias restantes: {Brl(basis)} ÷ 30 × {remaining} ÷ 2 = {Brl(amount)} (CLT art. {(e.Kind == TerminationKind.EarlyEndByEmployer ? "479" : "480, até o prejuízo da empresa")}).",
                ];
                if (e.Kind == TerminationKind.EarlyEndByEmployer)
                {
                    Add(PayrollCalculation.TerminationIndemnity, null, amount, TerminationPolicy.Days(remaining), memory);
                }
                else
                {
                    Add(PayrollCalculation.TerminationEarlyEndDeduction, null, amount, TerminationPolicy.Days(remaining), memory);
                }
            }
        }

        // VacationOnTermination: os períodos pela VacationPolicy (FLH-03) até o fim projetado; vencidas (o saldo dos períodos completos, em dobro
        // depois do concessivo, CLT art. 137) e proporcionais (avos do período em aquisição, art. 146), com 1/3.
        decimal expired = 0m, proportional = 0m;
        var expiredMemory = new List<string> { kindText };
        var proportionalMemory = new List<string> { kindText };
        var plans = VacationPolicy.Plan(new VacationPlanInput(monthSnapshot.Admission, projected, projected, e.Leaves, e.AbsenceDays.ToHashSet(), e.Vacation));
        foreach (var plan in plans)
        {
            if (plan.RestartReason is not null || plan.EntitledDays <= 0)
            {
                continue;
            }

            if (plan.End <= projected)
            {
                var taken = e.VacationTaken.Where(t => t.Sequence == plan.Sequence).ToList();
                var balance = Math.Max(plan.EntitledDays - taken.Sum(t => t.Days) - taken.Sum(t => t.SoldDays), 0);
                if (balance == 0)
                {
                    continue;
                }

                var doubled = plan.ConcessionEnd < e.TerminationDate;
                var amount = Money(remuneration * balance / 30m * (doubled ? 2m : 1m));
                expired += amount;
                expiredMemory.Add($"Período de {Date(plan.Start)} a {Date(plan.End)}: {plan.EntitledDays} dias de direito, saldo de {balance} dias"
                    + (doubled ? $", depois do fim do concessivo ({Date(plan.ConcessionEnd)}): em dobro (CLT art. 137)" : string.Empty)
                    + $"; {Brl(remuneration)} × {balance}/30{(doubled ? " × 2" : string.Empty)} = {Brl(amount)}.");
            }
            else if (row.Vacation == TerminationVacationRule.ExpiredAndProportional)
            {
                var avos = TerminationPolicy.Avos(plan.Start, projected);
                var amount = Money(remuneration * plan.EntitledDays / 30m * avos / 12m);
                proportional += amount;
                proportionalMemory.Add($"Período em aquisição desde {Date(plan.Start)}: {avos}/12 avos até {Date(projected)} (a fração de {TerminationPolicy.AvoMinDays} dias ou mais conta, CLT art. 146), "
                    + $"{plan.EntitledDays} dias de direito pelas faltas (parâmetro hr.vacation_absence_brackets).");
                proportionalMemory.Add($"{Brl(remuneration)} × {plan.EntitledDays}/30 × {avos}/12 = {Brl(amount)}.");
            }
            else
            {
                proportionalMemory.Add($"Período em aquisição desde {Date(plan.Start)}: sem férias proporcionais neste tipo de desligamento (matriz hr.termination_matrix).");
            }
        }

        var vacationRemuneration = RemunerationMemory();
        Add(PayrollCalculation.TerminationVacation, null, expired, null, [.. expiredMemory, .. vacationRemuneration]);
        Add(PayrollCalculation.TerminationVacationProrated, null, proportional, null, [.. proportionalMemory, .. vacationRemuneration]);
        var third = Money((expired + proportional) / 3m);
        Add(PayrollCalculation.TerminationVacationThird, null, third, "1/3", [$"1/3 constitucional: ({Brl(expired)} + {Brl(proportional)}) ÷ 3 = {Brl(third)}."]);

        // ThirteenthOnTermination (FLH-04): os avos do ano até o fim projetado, abatido o adiantamento; o INSS e o IRRF do 13º em separado.
        var thirteenth = row.Thirteenth
            ? Thirteenth(e, projected, input.Items, input.Tables, issues)
            : new ThirteenthPart([], 0m, 0m, 0m, 0m, 0m, []);
        if (!row.Thirteenth)
        {
            issues.Add(new PayrollCalcIssue("no_thirteenth", "sem 13º proporcional neste tipo de desligamento (matriz hr.termination_matrix).", false));
        }

        // FGTS rescisório: sobre as verbas da rescisão marcadas "Base do FGTS" (o aviso indenizado e o 13º), além do FGTS do mês.
        var terminationLines = lines.Concat(thirteenth.Lines.Where(l => l.Calculation is not (PayrollCalculation.Inss or PayrollCalculation.Irrf))).ToList();
        var fgtsBase = Math.Max(terminationLines.Sum(l =>
        {
            var item = input.Items.First(i => i.Id == l.ItemId);
            return !item.FgtsBase || l.Type == PayrollItemKind.Information ? 0m : l.Type == PayrollItemKind.Earning ? l.Amount : -l.Amount;
        }), 0m);
        var fgtsTable = tables[PayrollTaxTableKind.Fgts];
        var fgtsRate = fgtsTable.Content.Fgts?.MonthlyRate ?? 0m;
        var fgtsTermination = PayrollTaxRules.Round(fgtsBase * fgtsRate / 100m, fgtsTable.Rounding);
        var fgtsLines = new List<PayrollCalcLine>();
        if (fgtsTermination > 0m && input.Items.FirstOrDefault(i => i.Calculation == PayrollCalculation.Fgts) is { } fgtsItem)
        {
            fgtsLines.Add(new PayrollCalcLine(fgtsItem.Id, fgtsItem.Code, "FGTS rescisório", fgtsItem.Kind, PayrollCalculation.Fgts, Pct(fgtsRate), fgtsTermination, true,
            [
                $"Base do FGTS rescisório: {Brl(fgtsBase)} (verbas da rescisão marcadas \"Base do FGTS\": aviso indenizado e 13º, Súmula 305 do TST).",
                $"{Brl(fgtsBase)} × {Pct(fgtsRate)} = {Brl(fgtsTermination)} (informativo: depósito da empresa na guia rescisória).",
                $"Tabela FGTS de {PayrollTaxRules.MonthLabel(fgtsTable.ValidFrom)} ({PayrollCalculator.StatusLabel(fgtsTable.Status)}).",
            ], null));
        }

        // FgtsFine: o percentual da matriz sobre o saldo do extrato digitado pelo RH, mais os depósitos desta rescisão (o do mês e o rescisório),
        // que ainda não estão no extrato (Lei 8.036/1990, art. 18 §1º e §2º).
        var fgtsMonth = monthResult.FgtsAmount;
        decimal fineBase = 0m, fine = 0m;
        var fineMemory = new List<string>();
        if (row.FgtsFinePct > 0m)
        {
            if (e.FgtsBalance is not { } balance)
            {
                issues.Add(new PayrollCalcIssue(TerminationPolicy.FgtsBalanceCode, "sem o saldo do FGTS do extrato: a multa não saiu.", false));
            }
            else
            {
                fineBase = balance + fgtsMonth + fgtsTermination;
                fine = Money(fineBase * row.FgtsFinePct / 100m);
                fineMemory.Add(kindText);
                fineMemory.Add($"Saldo do FGTS para fins rescisórios digitado do extrato{(e.FgtsStatementDate is { } at ? $" de {Date(at)}" : string.Empty)}: {Brl(balance)}.");
                fineMemory.Add($"Mais os depósitos desta rescisão: FGTS do mês {Brl(fgtsMonth)} + FGTS rescisório {Brl(fgtsTermination)}; base {Brl(fineBase)}.");
                fineMemory.Add($"{Brl(fineBase)} × {Pct(row.FgtsFinePct)} = {Brl(fine)} (informativo: guia da empresa, não sai do líquido).");
                Add(PayrollCalculation.FgtsFine, null, fine, Pct(row.FgtsFinePct), fineMemory);
            }
        }

        // O mês (saldo de salário e o resto da folha mensal), as verbas da rescisão, os impostos e os informativos, nessa ordem.
        var monthLines = monthResult.Lines.Select(l => l.Calculation == PayrollCalculation.BaseSalary && l.Calculated
            ? l with { Name = "Saldo de salário", Memory = [$"Saldo de salário (TerminationSalaryBalance): os dias trabalhados no mês até o desligamento ({Date(e.TerminationDate)}).", .. l.Memory] }
            : l).ToList();
        bool Tax(PayrollCalcLine l) => l.Calculation is PayrollCalculation.Inss or PayrollCalculation.Irrf or PayrollCalculation.Fgts;
        var ordered = new List<PayrollCalcLine>();
        ordered.AddRange(monthLines.Where(l => !Tax(l)));
        ordered.AddRange(lines.Where(l => l.Type == PayrollItemKind.Earning).OrderBy(l => Array.IndexOf(TerminationOrder, l.Calculation)));
        ordered.AddRange(thirteenth.Lines.Where(l => l.Type == PayrollItemKind.Earning));
        ordered.AddRange(lines.Where(l => l.Type == PayrollItemKind.Deduction));
        ordered.AddRange(thirteenth.Lines.Where(l => l.Type == PayrollItemKind.Deduction && l.Calculation is not (PayrollCalculation.Inss or PayrollCalculation.Irrf)));
        ordered.AddRange(monthLines.Where(l => l.Calculation is PayrollCalculation.Inss or PayrollCalculation.Irrf));
        ordered.AddRange(thirteenth.Lines.Where(l => l.Calculation is PayrollCalculation.Inss or PayrollCalculation.Irrf));
        ordered.AddRange(monthLines.Where(l => l.Calculation == PayrollCalculation.Fgts));
        ordered.AddRange(fgtsLines);
        ordered.AddRange(lines.Where(l => l.Type == PayrollItemKind.Information));
        var totals = PayrollRules.Totals(ordered.Select(l => new PayslipLineValue(l.Type, l.Amount, l.Reference)));
        if (totals.Net < 0m)
        {
            issues.Add(new PayrollCalcIssue("negative_net", "líquido negativo: o desconto da rescisão é limitado a um mês de remuneração (CLT art. 477 §5º); confira com a contadora.", false));
        }

        var employer = monthResult.EmployerCharges + thirteenth.EmployerCharges;
        var fgtsTotal = fgtsMonth + fgtsTermination;
        var payslip = new PayrollCalcResult(ordered, totals, monthResult.InssBase + thirteenth.InssBase, monthResult.IrrfBase + thirteenth.IrrfBase, monthResult.FgtsBase + fgtsBase, fgtsTotal,
            monthResult.Inss + thirteenth.Inss, monthResult.Irrf + thirteenth.Irrf, employer, [.. monthResult.EmployerMemory, .. thirteenth.EmployerMemory], issues, monthResult.TableIds);
        var summary = new TerminationSummary(noticeDays, paidNotice, projected, fgtsMonth, fgtsTermination, row.FgtsFinePct, fineBase, fine, employer, totals.Gross + fgtsTotal + fine + employer,
            deadline, fineMemory);
        return new TerminationCalcResult(payslip, summary);
    }
}
