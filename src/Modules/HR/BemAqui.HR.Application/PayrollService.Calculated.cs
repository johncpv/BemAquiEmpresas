using System.Globalization;
using BemAqui.HR.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Folha calculada na leitura (FLH-02a CA 12): as versões de tabela usadas com o rótulo e a situação, e o cálculo do holerite para o painel
/// de memória (versão do motor, tabelas, parâmetros, entradas do retrato sem o tipo do afastamento, avisos e encargos da empresa).
/// </summary>
public sealed partial class PayrollService
{
    internal async Task<List<PayrollCalcTableRef>> TableRefsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0)
        {
            return [];
        }

        return (await db.PayrollTaxTables.AsNoTracking().Where(t => list.Contains(t.Id)).ToListAsync(ct)).OrderBy(t => t.Kind)
            .Select(t => new PayrollCalcTableRef(t.Id, t.Kind, $"{PayrollTaxRules.KindLabel(t.Kind)} de {PayrollTaxRules.MonthLabel(t.ValidFrom)}", t.ValidFrom, t.Status)).ToList();
    }

    /// <summary>O cálculo do holerite para a memória (CA 12): só o do próprio funcionário, sem o tipo do afastamento.</summary>
    private async Task<PayslipCalculationInfo?> CalculationInfoAsync(PayrollRun run, Payslip payslip, CancellationToken ct)
    {
        if (run.Mode != PayrollRunMode.Calculated || payslip.CalculatedAt is null)
        {
            return null;
        }

        var tables = await TableRefsAsync(payslip.TaxTableIds, ct);
        if (run.Type == PayrollRunType.Vacation)
        {
            return VacationInfo(run, payslip, tables);
        }

        var snapshot = PayrollCalculator.ReadSnapshot(payslip.InputsSnapshot);
        var inputs = new List<PayrollCalcInputView>();
        var parameterViews = new List<PayrollCalcInputView>();
        if (snapshot is not null)
        {
            var pt = CultureInfo.GetCultureInfo("pt-BR");
            inputs.Add(new("Salário base", PayrollCalculator.Brl(snapshot.BaseSalary)));
            inputs.Add(new("Admissão", snapshot.Admission.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)));
            if (snapshot.Termination is { } t)
            {
                inputs.Add(new("Desligamento", t.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)));
            }

            inputs.Add(new("Horas mensais do cargo", snapshot.MonthlyHours.ToString("0.##", pt)));
            inputs.Add(new("Insalubridade", snapshot.InsalubrityPct is { } i and > 0 ? $"{i}%" : "Não"));
            inputs.Add(new("Periculosidade", snapshot.HazardPay ? "Sim" : "Não"));
            inputs.Add(new("Vale-transporte", snapshot.UsesTransportVoucher ? "Usa" : "Não usa"));
            inputs.Add(new("Dependentes de IR", snapshot.Dependents.Count(d => d.IrDependent).ToString(pt)));
            inputs.Add(new("Filhos com salário-família", $"{snapshot.Dependents.Count(d => d.FamilyAllowance)} ({snapshot.Dependents.Count(d => d.FamilyAllowance && d.FamilyAllowanceDocsOk)} com os documentos entregues)"));
            inputs.Add(new("Afastamentos no mês", snapshot.Leaves.Count.ToString(pt)));
            inputs.Add(new("Espelho de ponto", snapshot.TimesheetClosed ? "Fechado" : "Sem espelho fechado"));
            inputs.Add(new("Faltas não abonadas", PayrollCalculator.Hours(snapshot.AbsenceMinutes)));
            inputs.Add(new("Atrasos não abonados", PayrollCalculator.Hours(snapshot.LateMinutes)));
            inputs.Add(new("Lançamentos avulsos", snapshot.Manual.Count.ToString(pt)));
            if (snapshot.Vacations is { Count: > 0 } vacations)
            {
                inputs.Add(new("Dias de férias no mês", vacations.Sum(v => v.Days).ToString(pt)));
            }
            var prm = snapshot.Parameters;
            parameterViews.Add(new("Salário proporcional", prm.Proration == SalaryProration.CommercialMonth ? "Mês comercial de 30 dias" : "Dias do mês"));
            parameterViews.Add(new("Dias de afastamento pagos pela empresa", prm.SickLeaveCompanyDays.ToString(pt)));
            parameterViews.Add(new("FGTS no afastamento por acidente", prm.AccidentKeepsFgts ? "Ligado" : "Desligado"));
            parameterViews.Add(new("Base da insalubridade", prm.InsalubrityBase == InsalubrityBase.MinimumWage ? "Salário mínimo" : "Salário base"));
            parameterViews.Add(new("Percentual da periculosidade", prm.HazardPayPct.ToString("0.##", pt) + "%"));
            parameterViews.Add(new("Insalubridade e periculosidade acumulam", prm.AllowancesAccumulate ? "Ligado" : "Desligado"));
            parameterViews.Add(new("Horas do valor da hora", prm.HourValueDivisor > 0m ? prm.HourValueDivisor.ToString("0.##", pt) : "Horas mensais do cargo"));
            parameterViews.Add(new("Idade do salário-família", prm.FamilyAllowanceMaxAge.ToString(pt) + " anos"));
            parameterViews.Add(new("Rendimento da redução do IRRF", prm.ReductionIncome == IrrfReductionIncome.GrossTaxable ? "Rendimento tributável bruto" : "Base de cálculo"));
        }

        return new PayslipCalculationInfo(payslip.CalculatorVersion, payslip.CalculatedAt, run.IsSimulation, tables, parameterViews, inputs,
            PayrollCalculator.ReadIssues(payslip.CalculationIssues).Select(i => Capitalize(i.Message)).ToList(), payslip.EmployerCharges, PayrollCalculator.ReadMemory(payslip.EmployerChargesMemory));
    }

    /// <summary>O cálculo do holerite de férias (FLH-03 CA 6): o retrato da programação, as variáveis e os parâmetros de férias.</summary>
    private static PayslipCalculationInfo VacationInfo(PayrollRun run, Payslip payslip, IReadOnlyList<PayrollCalcTableRef> tables)
    {
        var snapshot = VacationCalculator.ReadSnapshot(payslip.InputsSnapshot);
        var inputs = new List<PayrollCalcInputView>();
        var parameterViews = new List<PayrollCalcInputView>();
        if (snapshot is not null)
        {
            var pt = CultureInfo.GetCultureInfo("pt-BR");
            inputs.Add(new("Salário base", PayrollCalculator.Brl(snapshot.BaseSalary)));
            inputs.Add(new("Insalubridade", snapshot.InsalubrityPct is { } i and > 0 ? $"{i}%" : "Não"));
            inputs.Add(new("Periculosidade", snapshot.HazardPay ? "Sim" : "Não"));
            inputs.Add(new("Dependentes de IR", snapshot.IrDependents.ToString(pt)));
            inputs.Add(new("Período aquisitivo", $"{VacationPolicy.Date(snapshot.PeriodStart)} a {VacationPolicy.Date(snapshot.PeriodEnd)}"));
            inputs.Add(new("Fim do período concessivo", VacationPolicy.Date(snapshot.ConcessionEnd)));
            inputs.Add(new("Gozo", $"{VacationPolicy.Date(snapshot.Start)} a {VacationPolicy.Date(snapshot.Start.AddDays(snapshot.Days - 1))} ({snapshot.Days} dias)"));
            inputs.Add(new("Abono pecuniário", snapshot.SoldDays > 0 ? $"{snapshot.SoldDays} dias" : "Não"));
            inputs.Add(new("Adiantamento do 13º", snapshot.Advance13th ? "Pedido" : "Não"));
            inputs.Add(new("Pagamento até", VacationPolicy.Date(snapshot.PaymentDate)));
            inputs.Add(new("Meses com variáveis", snapshot.Variables.Count.ToString(pt)));
            var v = snapshot.Vacation;
            parameterViews.Add(new("Média das variáveis", $"{v.AverageMonths} meses"));
            parameterViews.Add(new("Tributação das férias", v.Taxation == VacationTaxation.SplitByMonth ? "INSS rateado nas competências do gozo" : "INSS inteiro na competência do início"));
            parameterViews.Add(new("Pagamento das férias", $"{v.PaymentDaysBefore} dias antes do início"));
            parameterViews.Add(new("Insalubridade e periculosidade acumulam", snapshot.Parameters.AllowancesAccumulate ? "Ligado" : "Desligado"));
            parameterViews.Add(new("Rendimento da redução do IRRF", snapshot.Parameters.ReductionIncome == IrrfReductionIncome.GrossTaxable ? "Rendimento tributável bruto" : "Base de cálculo"));
        }

        return new PayslipCalculationInfo(payslip.CalculatorVersion, payslip.CalculatedAt, run.IsSimulation, tables, parameterViews, inputs,
            PayrollCalculator.ReadIssues(payslip.CalculationIssues).Select(i => Capitalize(i.Message)).ToList(), payslip.EmployerCharges, PayrollCalculator.ReadMemory(payslip.EmployerChargesMemory));
    }

    internal static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.GetCultureInfo("pt-BR")) + text[1..];
}
