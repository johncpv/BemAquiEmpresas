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

        var snapshot = PayrollCalculator.ReadSnapshot(payslip.InputsSnapshot);
        var tables = await TableRefsAsync(payslip.TaxTableIds, ct);
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
            // FLH-02b CA 7: as horas do espelho fechado que entraram no cálculo.
            var days = snapshot.HourDays ?? [];
            foreach (var kind in new[] { PayrollDayKind.Workday, PayrollDayKind.RestDay, PayrollDayKind.Holiday })
            {
                var minutes = days.Where(d => d.Kind == kind).Sum(d => d.OvertimeMinutes);
                if (minutes > 0)
                {
                    inputs.Add(new($"Horas extras em {PayrollHours.KindLabel(kind)}", PayrollCalculator.Hours(minutes)));
                }
            }

            if (days.Sum(d => d.NightMinutes) is var night and > 0)
            {
                inputs.Add(new("Horas no horário noturno", PayrollCalculator.Hours(night)));
            }

            if (days.Count(d => d.UnjustifiedAbsence) is var absent and > 0)
            {
                inputs.Add(new("Faltas injustificadas (dias)", absent.ToString(pt)));
            }

            if (snapshot.HourBank is { } bank)
            {
                inputs.Add(new("Banco de horas (saldo do espelho)", TimesheetRules.WithSign(bank.BalanceMinutes)));
                if (bank.DecisionMinutes != 0)
                {
                    inputs.Add(new(bank.DecisionMinutes > 0 ? "Pagar do banco" : "Descontar do banco", TimesheetRules.Duration(Math.Abs(bank.DecisionMinutes))));
                }
            }

            inputs.Add(new("Lançamentos avulsos", snapshot.Manual.Count.ToString(pt)));
            var prm = snapshot.Parameters;
            var h = prm.HourRules;
            parameterViews.Add(new("Salário proporcional", prm.Proration == SalaryProration.CommercialMonth ? "Mês comercial de 30 dias" : "Dias do mês"));
            parameterViews.Add(new("Dias de afastamento pagos pela empresa", prm.SickLeaveCompanyDays.ToString(pt)));
            parameterViews.Add(new("FGTS no afastamento por acidente", prm.AccidentKeepsFgts ? "Ligado" : "Desligado"));
            parameterViews.Add(new("Base da insalubridade", prm.InsalubrityBase == InsalubrityBase.MinimumWage ? "Salário mínimo" : "Salário base"));
            parameterViews.Add(new("Percentual da periculosidade", prm.HazardPayPct.ToString("0.##", pt) + "%"));
            parameterViews.Add(new("Insalubridade e periculosidade acumulam", prm.AllowancesAccumulate ? "Ligado" : "Desligado"));
            parameterViews.Add(new("Horas do valor da hora", prm.HourValueDivisor > 0m ? prm.HourValueDivisor.ToString("0.##", pt) : "Horas mensais do cargo"));
            parameterViews.Add(new("Idade do salário-família", prm.FamilyAllowanceMaxAge.ToString(pt) + " anos"));
            parameterViews.Add(new("Rendimento da redução do IRRF", prm.ReductionIncome == IrrfReductionIncome.GrossTaxable ? "Rendimento tributável bruto" : "Base de cálculo"));
            string P(decimal v) => v.ToString("0.##", pt) + "%";
            parameterViews.Add(new("Horas extras (dia útil, domingo ou folga, feriado)", $"{P(h.OvertimeWorkdayPct)}, {P(h.OvertimeRestDayPct)}, {P(h.OvertimeHolidayPct)}"));
            parameterViews.Add(new("Adicionais no valor da hora extra", h.OvertimeIncludesAllowances ? "Ligado" : "Desligado"));
            parameterViews.Add(new("Adicional noturno", h.NightEnabled ? $"{P(h.NightPct)}, {h.NightStartHour} h às {h.NightEndHour} h{(h.NightReducedHour ? ", hora reduzida" : string.Empty)}" : "Desligado"));
            parameterViews.Add(new("Fórmula do DSR", h.DsrFormula == DsrFormula.CompanyCalendar ? "Dias úteis pelo calendário padrão" : "Dias do mês − domingos − feriados"));
            parameterViews.Add(new("Perda do DSR por falta", h.DsrLossOnAbsence ? "Ligado" : "Desligado"));
        }

        return new PayslipCalculationInfo(payslip.CalculatorVersion, payslip.CalculatedAt, run.IsSimulation, tables, parameterViews, inputs,
            PayrollCalculator.ReadIssues(payslip.CalculationIssues).Select(i => Capitalize(i.Message)).ToList(), payslip.EmployerCharges, PayrollCalculator.ReadMemory(payslip.EmployerChargesMemory),
            $"/app/rh/ponto/espelho/{payslip.EmployeeId}?mes={run.Period.ToString("yyyy-MM", CultureInfo.InvariantCulture)}");
    }

    internal static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.GetCultureInfo("pt-BR")) + text[1..];
}
