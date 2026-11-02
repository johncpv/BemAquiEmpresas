namespace BemAqui.HR.Domain;

/// <summary>
/// Retrato do 13º do ano do desligamento (FLH-04, <c>ThirteenthSnapshot</c> sem o que já está no retrato do mês): os afastamentos e as faltas
/// do ano, as variáveis por mês, a parte do 13º já paga (1ª parcela, ou a 2ª inteira) e o adiantamento pago com as férias, e os parâmetros do 13º.
/// </summary>
public sealed record TerminationThirteenthSnapshot(
    IReadOnlyList<PayrollCalcLeave> Leaves,
    IReadOnlyList<ThirteenthAbsence> Absences,
    IReadOnlyList<ThirteenthVariable> Variables,
    decimal AdvancePaid,
    decimal VacationAdvance,
    ThirteenthParameters Parameters);

public static partial class TerminationCalculator
{
    /// <summary>
    /// ThirteenthOnTermination (CA 3): o 13º proporcional pelas regras do <see cref="ThirteenthCalculator"/> (FLH-04): os avos do ano até o fim
    /// projetado (o mês com 15 dias ou mais trabalhados conta), a remuneração de referência, as médias, menos o 13º já pago no ano; o INSS e
    /// o IRRF do 13º em separado do salário do mês (exclusivo na fonte) e os encargos da empresa sobre ele.
    /// </summary>
    private static ThirteenthPart Thirteenth(TerminationCalcSnapshot e, DateOnly projected, IReadOnlyList<PayrollCalcItem> items, IReadOnlyList<PayrollCalcTable> tables, List<PayrollCalcIssue> issues)
    {
        var empty = new ThirteenthPart([], 0m, 0m, 0m, 0m, 0m, []);
        if (e.Thirteenth is not { } t)
        {
            issues.Add(new PayrollCalcIssue("thirteenth_missing", "sem o retrato do 13º do ano: o 13º proporcional não saiu.", false));
            return empty;
        }

        var year = e.TerminationDate.Year;
        var end = projected.Year > year ? new DateOnly(year, 12, 31) : projected;
        if (projected.Year > year)
        {
            issues.Add(new PayrollCalcIssue("thirteenth_next_year", $"a projeção do aviso passa para {year + 1}: o avo de {year + 1} não saiu; confira com a contadora.", false));
        }

        var m = e.Month;
        var snapshot = new ThirteenthSnapshot(year, ThirteenthInstallment.Second, end.Month, m.BaseSalary, m.Admission, end, m.InsalubrityPct, m.HazardPay, m.Dependents, t.Leaves, t.Absences,
            t.Variables, t.AdvancePaid, t.VacationAdvance, m.Parameters, t.Parameters);
        var months = ThirteenthCalculator.ThirteenthMonths(year, end.Month, m.Admission, end, t.Leaves, t.Absences, m.Parameters.SickLeaveCompanyDays, t.Parameters);
        var avos = months.Count(x => x.Counts);
        if (avos == 0)
        {
            issues.Add(new PayrollCalcIssue(ThirteenthCalculator.NoAvosCode, $"sem avos de 13º em {year}: nenhum mês com {t.Parameters.MonthMinDays} dias ou mais trabalhados.", false));
            return empty;
        }

        var lines = new List<PayrollCalcLine>();
        void Add(PayrollCalculation calc, string? name, decimal amount, string? reference, List<string> memory)
        {
            if (amount <= 0m)
            {
                return;
            }

            if (items.FirstOrDefault(i => i.Calculation == calc) is not { } item)
            {
                issues.Add(new PayrollCalcIssue("item_missing", $"falta a rubrica com a regra {PayrollCalculator.Label(calc)} (RH → Rubricas): a linha não saiu.", false));
                return;
            }

            lines.Add(new PayrollCalcLine(item.Id, item.Code, name ?? item.Name, item.Kind, calc, reference, amount, true, memory, null));
        }

        var (remuneration, remunerationMemory) = ThirteenthCalculator.Remuneration(snapshot, tables);
        var gross = Money(remuneration * avos / 12m);
        List<string> memory = [$"13º proporcional de {year} até {Date(end)}" + (end > e.TerminationDate ? " (com a projeção do aviso indenizado)." : "."), .. remunerationMemory,
            $"Avos de {year} (mês com {t.Parameters.MonthMinDays} dias ou mais trabalhados conta, parâmetro hr.thirteenth_month_min_days):"];
        memory.AddRange(months.Select(x => ThirteenthCalculator.MonthText(year, x, t.Parameters.MonthMinDays)));
        memory.Add($"{Brl(remuneration)} × {avos}/12 avos = {Brl(gross)}.");
        Add(PayrollCalculation.ThirteenthFinal, "13º salário proporcional", gross, $"{avos}/12 avos", memory);
        var (averages, averagesMemory) = ThirteenthCalculator.ThirteenthAverages(t.Variables, months, t.Parameters.Averages);
        Add(PayrollCalculation.ThirteenthAverages, null, averages, null, [.. averagesMemory]);
        var paid = t.AdvancePaid + t.VacationAdvance;
        var deduction = Math.Min(paid, gross + averages);
        Add(PayrollCalculation.ThirteenthAdvanceDeduction, null, deduction, null, [
            .. t.AdvancePaid > 0m ? [$"13º já pago no ano: {Brl(t.AdvancePaid)}."] : Array.Empty<string>(),
            .. t.VacationAdvance > 0m ? [$"Adiantamento do 13º pago com as férias: {Brl(t.VacationAdvance)}."] : Array.Empty<string>(),
            $"Desconto do adiantamento: {Brl(deduction)}.",
        ]);

        decimal Base(Func<PayrollCalcItem, bool> marked) => Math.Max(lines.Sum(l =>
        {
            var item = items.First(i => i.Id == l.ItemId);
            return !marked(item) || l.Type == PayrollItemKind.Information ? 0m : l.Type == PayrollItemKind.Earning ? l.Amount : -l.Amount;
        }), 0m);

        var byKind = tables.ToDictionary(x => x.Kind);
        var inssBase = Base(i => i.InssBase);
        var irrfIncome = Base(i => i.IrrfBase);
        var inssTable = byKind[PayrollTaxTableKind.Inss];
        var inssResult = PayrollTaxRules.Inss(inssTable.Content.Inss!, inssTable.Rounding, inssBase);
        Add(PayrollCalculation.Inss, "INSS sobre o 13º salário", inssResult.Amount, null,
            [$"INSS sobre o 13º, separado do salário do mês: base {Brl(inssBase)} (linhas marcadas \"Base do INSS\").", .. inssResult.Memory,
                $"Tabela INSS de {PayrollTaxRules.MonthLabel(inssTable.ValidFrom)} ({PayrollCalculator.StatusLabel(inssTable.Status)})."]);

        var irrfTable = byKind[PayrollTaxTableKind.Irrf];
        var irrf = irrfTable.Content.Irrf!;
        var dependents = m.Dependents.Count(d => d.IrDependent);
        var legal = inssResult.Amount + (dependents * irrf.DependentDeduction);
        var byLegal = PayrollCalculator.IrrfOption(irrf, irrfTable.Rounding, irrfIncome, legal, m.Parameters.ReductionIncome);
        var bySimplified = PayrollCalculator.IrrfOption(irrf, irrfTable.Rounding, irrfIncome, irrf.SimplifiedDiscount, m.Parameters.ReductionIncome);
        var simplified = bySimplified.Final < byLegal.Final;
        var chosen = simplified ? bySimplified : byLegal;
        Add(PayrollCalculation.Irrf, "IRRF sobre o 13º salário", chosen.Final, null,
        [
            $"IRRF exclusivo na fonte sobre o 13º, separado do salário do mês. Rendimento: {Brl(irrfIncome)} (linhas marcadas \"Base do IRRF\").",
            simplified ? $"Escolhido: desconto simplificado de {Brl(irrf.SimplifiedDiscount)}, o que dá o menor imposto."
                : $"Escolhido: dedução legal (INSS do 13º {Brl(inssResult.Amount)} + {dependents} × {Brl(irrf.DependentDeduction)} = {Brl(legal)}), o que dá o menor imposto.",
            .. chosen.Memory,
            $"Tabela IRRF de {PayrollTaxRules.MonthLabel(irrfTable.ValidFrom)} ({PayrollCalculator.StatusLabel(irrfTable.Status)}).",
        ]);

        var chargesTable = byKind[PayrollTaxTableKind.EmployerCharges];
        var charges = PayrollTaxRules.EmployerCharges(chargesTable.Content.EmployerCharges!, chargesTable.Rounding, inssBase);
        return new ThirteenthPart(lines, inssBase, inssResult.Amount, chosen.Final, chosen.Base, charges.Amount, ["Sobre o 13º proporcional:", .. charges.Memory]);
    }
}
