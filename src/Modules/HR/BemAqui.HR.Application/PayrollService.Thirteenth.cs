using System.Globalization;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Folha de 13º calculada (FLH-04): a competência é o mês da data limite da parcela (<c>hr.thirteenth_first_due</c> e
/// <c>hr.thirteenth_second_due</c>); uma calculada por tipo e ano (CA 8, 409); a 2ª sem a 1ª fechada pede a confirmação; e o painel da
/// memória do holerite com os avos mês a mês.
/// </summary>
public sealed partial class PayrollService
{
    /// <summary>A competência da folha de 13º no ano: o primeiro dia do mês da data limite da parcela.</summary>
    internal async Task<DateOnly> ThirteenthPeriodAsync(PayrollRunType type, int year, CancellationToken ct)
    {
        var due = await parameters.DueParametersAsync(ct);
        return new DateOnly(year, type == PayrollRunType.Thirteenth2 ? due.ThirteenthSecondMonth : due.ThirteenthFirstMonth, 1);
    }

    /// <summary>
    /// CA 8 na criação da calculada: a competência vira o mês da data limite; já existe a do tipo no ano, 409; a 2ª sem a 1ª fechada (lançada
    /// ou calculada) sem a confirmação, 409 com "A 1ª parcela não foi paga: a 2ª será calculada sem adiantamento.".
    /// </summary>
    private async Task<Result<DateOnly>> ThirteenthCreateCheckAsync(PayrollRunType type, DateOnly period, bool confirmWithoutAdvance, CancellationToken ct)
    {
        var year = period.Year;
        var jan = new DateOnly(year, 1, 1);
        var dec = new DateOnly(year, 12, 31);
        if (await db.PayrollRuns.AnyAsync(r => r.Type == type && r.Mode == PayrollRunMode.Calculated && r.RectifiesRunId == null && r.Period >= jan && r.Period <= dec, ct))
        {
            return Error.Conflict(PayrollRules.DuplicateCode, $"Já existe a folha {PayrollRules.TypeLabel(type)} calculada de {year}.");
        }

        if (type == PayrollRunType.Thirteenth2 && !confirmWithoutAdvance
            && !await db.PayrollRuns.AnyAsync(r => r.Type == PayrollRunType.Thirteenth1 && r.Status != PayrollRunStatus.Draft && r.Period >= jan && r.Period <= dec, ct))
        {
            return Error.Conflict(ThirteenthCalculator.FirstOpenCode, ThirteenthCalculator.FirstOpenMessage);
        }

        return await ThirteenthPeriodAsync(type, year, ct);
    }

    /// <summary>A memória do holerite do 13º (CA 2 e 3): o retrato do 13º, os avos mês a mês e os parâmetros do 13º.</summary>
    private async Task<PayslipCalculationInfo> ThirteenthCalculationInfoAsync(PayrollRun run, Payslip payslip, CancellationToken ct)
    {
        var snapshot = ThirteenthCalculator.ReadSnapshot(payslip.InputsSnapshot);
        var tables = await TableRefsAsync(payslip.TaxTableIds, ct);
        var inputs = new List<PayrollCalcInputView>();
        var parameterViews = new List<PayrollCalcInputView>();
        if (snapshot is not null)
        {
            var pt = CultureInfo.GetCultureInfo("pt-BR");
            var months = ThirteenthCalculator.MonthsOf(snapshot);
            inputs.Add(new("Parcela", snapshot.Installment == ThirteenthInstallment.First ? $"1ª parcela de {snapshot.Year}" : $"2ª parcela de {snapshot.Year}"));
            inputs.Add(new(snapshot.Installment == ThirteenthInstallment.First ? "Salário do mês anterior" : "Salário de dezembro", PayrollCalculator.Brl(snapshot.BaseSalary)));
            inputs.Add(new("Admissão", snapshot.Admission.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)));
            if (snapshot.Termination is { } t)
            {
                inputs.Add(new("Desligamento", t.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)));
            }

            inputs.Add(new("Avos", $"{months.Count(m => m.Counts)}/12"));
            inputs.AddRange(months.Select(m => new PayrollCalcInputView(ThirteenthCalculator.MonthLabel(m.Month),
                m.EmployedDays == 0 ? "Fora do vínculo" : m.Projected ? $"Projetado: {(m.Counts ? "conta" : "não conta")}" : $"{m.WorkedDays} dias trabalhados: {(m.Counts ? "conta" : "não conta")}")));
            inputs.Add(new("Insalubridade", snapshot.InsalubrityPct is { } i and > 0 ? $"{i}%" : "Não"));
            inputs.Add(new("Periculosidade", snapshot.HazardPay ? "Sim" : "Não"));
            inputs.Add(new("Dependentes de IR", snapshot.Dependents.Count(d => d.IrDependent).ToString(pt)));
            inputs.Add(new("Meses com horas extras, adicional noturno ou DSR", snapshot.Variables.Count.ToString(pt)));
            inputs.Add(new("1ª parcela paga", PayrollCalculator.Brl(snapshot.AdvancePaid)));
            inputs.Add(new("Adiantamento pago com as férias", PayrollCalculator.Brl(snapshot.VacationAdvance)));
            var prm = snapshot.Parameters;
            parameterViews.Add(new("Avos da 1ª parcela", prm.FirstAvos == ThirteenthFirstAvos.ProjectedYear ? "Avos do ano projetados" : "Só os avos até o mês da parcela"));
            parameterViews.Add(new("Dias para contar o avo", prm.MonthMinDays.ToString(pt)));
            parameterViews.Add(new("Afastamento nos avos", prm.LeaveRule == ThirteenthLeaveRule.InssLeaves ? "Doença e acidente de trabalho" : "Só doença"));
            parameterViews.Add(new("Médias no 13º", prm.Averages == ThirteenthAveragesRule.MonthsWithAvos ? "Média dos meses com avos" : "Sem médias"));
            parameterViews.Add(new("Dias de afastamento pagos pela empresa", snapshot.Monthly.SickLeaveCompanyDays.ToString(pt)));
        }

        return new PayslipCalculationInfo(payslip.CalculatorVersion, payslip.CalculatedAt, run.IsSimulation, tables, parameterViews, inputs,
            PayrollCalculator.ReadIssues(payslip.CalculationIssues).Select(i => Capitalize(i.Message)).ToList(), payslip.EmployerCharges, PayrollCalculator.ReadMemory(payslip.EmployerChargesMemory));
    }
}
