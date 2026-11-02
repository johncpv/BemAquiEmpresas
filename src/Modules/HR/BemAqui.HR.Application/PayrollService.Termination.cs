using System.Globalization;
using BemAqui.HR.Domain;

namespace BemAqui.HR.Application;

public sealed partial class PayrollService
{
    /// <summary>O cálculo do holerite de rescisão (FLH-05 CA 3 e 12): o retrato da rescisão (tipo, aviso, datas, FGTS) e os parâmetros da matriz.</summary>
    private static PayslipCalculationInfo TerminationInfo(PayrollRun run, Payslip payslip, IReadOnlyList<PayrollCalcTableRef> tables)
    {
        var snapshot = TerminationCalculator.ReadSnapshot(payslip.InputsSnapshot);
        var inputs = new List<PayrollCalcInputView>();
        var parameterViews = new List<PayrollCalcInputView>();
        if (snapshot is not null)
        {
            var pt = CultureInfo.GetCultureInfo("pt-BR");
            var p = snapshot.Termination;
            var row = p.Row(snapshot.Kind);
            inputs.Add(new("Tipo de desligamento", TerminationPolicy.KindLabel(snapshot.Kind)));
            inputs.Add(new("Aviso prévio", TerminationPolicy.NoticeLabel(snapshot.NoticeKind)));
            inputs.Add(new("Desligamento", TerminationPolicy.Date(snapshot.TerminationDate)));
            inputs.Add(new("Admissão", TerminationPolicy.Date(snapshot.Month.Admission)));
            inputs.Add(new("Salário base", PayrollCalculator.Brl(snapshot.Month.BaseSalary)));
            if (snapshot.ContractEndDate is { } end)
            {
                inputs.Add(new("Fim previsto da experiência", TerminationPolicy.Date(end)));
            }

            inputs.Add(new("Saldo do FGTS do extrato", snapshot.FgtsBalance is { } b ? PayrollCalculator.Brl(b) + (snapshot.FgtsStatementDate is { } at ? $" ({TerminationPolicy.Date(at)})" : string.Empty) : "Não informado"));
            inputs.Add(new("Espelho de ponto do mês", snapshot.Month.TimesheetClosed ? "Fechado" : "Sem espelho fechado"));
            if (snapshot.Month.HourBank is { } bank)
            {
                inputs.Add(new("Banco de horas (saldo)", TimesheetRules.WithSign(bank.BalanceMinutes)));
            }

            inputs.Add(new("Férias já programadas (dias)", snapshot.VacationTaken.Sum(t => t.Days).ToString(pt)));
            inputs.Add(new("Meses com variáveis", snapshot.Variables.Count.ToString(pt)));
            parameterViews.Add(new("Matriz do tipo (hr.termination_matrix)", $"aviso: {NoticeRule(row.Notice)}; férias: {(row.Vacation == TerminationVacationRule.ExpiredOnly ? "só vencidas" : "vencidas e proporcionais")}; 13º: {(row.Thirteenth ? "sim" : "não")}; multa do FGTS: {row.FgtsFinePct.ToString("0.##", pt)}%"));
            parameterViews.Add(new("Aviso proporcional (hr.notice_days_rule)", $"{p.NoticeBaseDays} dias + {p.NoticeDaysPerYear} por ano completo, até {p.NoticeMaxDays}"));
            parameterViews.Add(new("Prazo do pagamento (hr.termination_payment_days)", $"{p.PaymentDays} dias corridos"));
            parameterViews.Add(new("Banco de horas negativo (hr.hour_bank_termination)", p.DiscountNegativeHourBank ? "Descontado" : "Não descontado"));
            parameterViews.Add(new("Média das variáveis", $"{snapshot.Vacation.AverageMonths} meses"));
        }

        return new PayslipCalculationInfo(payslip.CalculatorVersion, payslip.CalculatedAt, run.IsSimulation, tables, parameterViews, inputs,
            PayrollCalculator.ReadIssues(payslip.CalculationIssues).Select(i => Capitalize(i.Message)).ToList(), payslip.EmployerCharges, PayrollCalculator.ReadMemory(payslip.EmployerChargesMemory));
    }

    private static string NoticeRule(TerminationNoticeRule rule) => rule switch
    {
        TerminationNoticeRule.Full => "integral",
        TerminationNoticeRule.Half => "metade",
        TerminationNoticeRule.Deduction => "desconto do não cumprido",
        _ => "sem aviso",
    };
}
