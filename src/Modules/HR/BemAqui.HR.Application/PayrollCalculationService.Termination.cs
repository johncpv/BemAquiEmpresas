using System.Text.Json;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>As entradas da rescisão montadas do banco: o retrato, as rubricas e as tabelas, os parâmetros usados, o modo oficial e o banco de horas.</summary>
public sealed record TerminationInputs(TerminationCalcInput Input, IReadOnlyList<Guid> ParameterIds, bool Official, bool TimesheetClosed, int HourBankBalance);

/// <summary>Pedido da rescisão para o motor: tipo, aviso, data do desligamento, fim da experiência e o saldo do FGTS do extrato.</summary>
public sealed record TerminationRequest(TerminationKind Kind, TerminationNoticeKind NoticeKind, DateOnly TerminationDate, DateOnly? ContractEndDate, decimal? FgtsBalance, DateOnly? FgtsStatementDate);

/// <summary>
/// Retrato da rescisão (FLH-05, ADR 0039 §2) com as mesmas peças das outras folhas calculadas: o contexto (tabelas da competência do
/// desligamento, rubricas, parâmetros e o modo oficial, FLH-06), o retrato do mês pela folha mensal (FLH-02a/02b), os afastamentos, as faltas
/// e as férias já programadas (FLH-03), as variáveis dos últimos meses e o retrato do 13º do ano (FLH-04). Nada é gravado aqui.
/// </summary>
public sealed partial class PayrollCalculationService
{
    internal async Task<Result<TerminationInputs>> TerminationInputsAsync(Guid employeeId, TerminationRequest request, Guid? runId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var period = new DateOnly(request.TerminationDate.Year, request.TerminationDate.Month, 1);
        var context = await ContextAsync(new PayrollRun { Period = period, Type = PayrollRunType.Termination, Mode = PayrollRunMode.Calculated }, ct);
        if (context.IsFailure)
        {
            return context.Error!;
        }

        var c = context.Value;
        if (TerminationCalculator.Required.FirstOrDefault(r => c.Items.All(i => i.Calculation != r)) is var missing && missing != PayrollCalculation.None)
        {
            return Error.Validation(PayrollCalculationRules.ItemMissingCode, $"Falta a rubrica com a regra {PayrollCalculator.Label(missing)}: confira em RH → Rubricas.") with
            {
                Data = new Dictionary<string, object?> { ["link"] = "/app/rh/rubricas" },
            };
        }

        var (vacation, vacationIds) = await parameters.VacationParametersAsync(ct);
        var (termination, terminationIds) = await parameters.TerminationParametersAsync(ct);
        var manual = runId is { } id ? await ManualAsync(id, ct) : new Dictionary<Guid, List<PayrollCalcManualLine>>();
        var month = (await SnapshotsAsync(runId ?? Guid.Empty, period, [employeeId], manual, c.Parameters, ct))[employeeId];

        // HourBankOnTermination: o saldo do último espelho fechado; o positivo é pago como extra de dia útil (a regra da FLH-02b), o negativo só
        // descontado quando o acordo permite (hr.hour_bank_termination). A decisão vai para o holerite e baixa o saldo no fechamento.
        var sheet = await db.Timesheets.AsNoTracking().Where(t => t.EmployeeId == employeeId && t.Status == TimesheetStatus.Closed && t.Period <= period && t.BankBalanceMinutes != null)
            .OrderByDescending(t => t.Period).FirstOrDefaultAsync(ct);
        var balance = sheet is null ? 0 : (sheet.BankBalanceMinutes ?? 0) - (sheet.Period == period ? 0 : sheet.BankPayoutMinutes);
        var decision = balance > 0 || (balance < 0 && termination.DiscountNegativeHourBank) ? balance : 0;
        month = month with
        {
            HourBank = sheet is null ? null : new PayrollCalcHourBank(balance, decision, "acerto do saldo do banco de horas na rescisão (parâmetro hr.hour_bank_termination)."),
        };

        var leaves = await db.EmployeeHistory.AsNoTracking().Where(h => h.EmployeeId == employeeId && h.Kind == EmployeeHistoryKind.Leave && h.CancelledAt == null)
            .OrderBy(h => h.EffectiveDate).ThenBy(h => h.LeaveEndDate).Select(h => new VacationLeaveInput(h.EffectiveDate, h.LeaveEndDate, h.LeaveType)).ToListAsync(ct);
        var sheets = await db.Timesheets.AsNoTracking().Where(t => t.EmployeeId == employeeId && t.Status == TimesheetStatus.Closed && t.DaysSnapshot != null).Select(t => t.DaysSnapshot!).ToListAsync(ct);
        var absences = sheets.SelectMany(json => JsonSerializer.Deserialize<List<TimesheetDayResult>>(json, TimeClock.TimesheetService.SnapshotJson) ?? [])
            .Where(d => d.Status == TimesheetDayStatus.Absent && d.Day <= request.TerminationDate).Select(d => d.Day).Distinct().Order().ToList();
        var taken = await (from s in db.VacationSchedules.AsNoTracking()
                           join p in db.VacationPeriods.AsNoTracking() on s.PeriodId equals p.Id
                           where s.EmployeeId == employeeId && s.Status != VacationScheduleStatus.Cancelled && s.StartsOn <= request.TerminationDate
                           group s by p.Sequence into g
                           orderby g.Key
                           select new TerminationVacationTaken(g.Key, g.Sum(x => x.Days), g.Sum(x => x.SoldDays))).ToListAsync(ct);
        var variables = await vacations.VariablesAsync(employeeId, request.TerminationDate, vacation.AverageMonths, ct);
        var (thirteenth, thirteenthIds) = await TerminationThirteenthAsync(employeeId, request.TerminationDate, c.Parameters, ct);
        var snapshot = new TerminationCalcSnapshot(request.Kind, request.NoticeKind, request.TerminationDate, request.ContractEndDate, request.FgtsBalance, request.FgtsStatementDate,
            month, leaves, absences, taken, variables, thirteenth, vacation, termination);
        var ids = c.ParameterIds.Concat(vacationIds).Concat(terminationIds).Concat(thirteenthIds).Distinct().ToList();
        return new TerminationInputs(new TerminationCalcInput(snapshot, c.Items, c.Tables), ids, c.Official, month.TimesheetClosed, balance);
    }

    /// <summary>A rescisão do pedido gravado (o tipo, o aviso, as datas e o saldo do FGTS).</summary>
    internal static TerminationRequest RequestOf(EmployeeTermination t) => new(t.Kind, t.NoticeKind, t.TerminationDate, t.ContractEndDate, t.FgtsBalance, t.FgtsStatementDate);

    /// <summary>
    /// "Calcular rescisão" (CA 3, 4 e 12): roda o <see cref="TerminationCalculator"/> e grava, numa transação, a folha <c>Termination</c>/
    /// <c>Calculated</c> da rescisão (uma por rescisão), o holerite com as linhas, a memória e o retrato, a decisão do banco de horas, as guias
    /// (INSS e IRRF retidos com os encargos, pelo vencimento das guias; o FGTS rescisório e a multa, no prazo do pagamento), o histórico do
    /// cálculo e a rescisão <c>Calculated</c>. Antes da virada e do caso de rescisão conferido (FLH-06), a folha é simulação.
    /// </summary>
    internal async Task<Result<(PayrollRun Run, Payslip Payslip, TerminationCalcResult Result, TerminationInputs Inputs)>> CalculateTerminationAsync(EmployeeTermination termination, bool isNew,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(termination);
        var inputs = await TerminationInputsAsync(termination.EmployeeId, RequestOf(termination), termination.PayrollRunId, ct);
        if (inputs.IsFailure)
        {
            return inputs.Error!;
        }

        var result = TerminationCalculator.Calculate(inputs.Value.Input);
        var r = result.Payslip;
        if (r.Issues.FirstOrDefault(i => i.Blocking) is { } blocking)
        {
            return Error.Validation("termination_calculation_blocked", Capitalize(blocking.Message));
        }

        var period = new DateOnly(termination.TerminationDate.Year, termination.TerminationDate.Month, 1);
        var run = termination.PayrollRunId is { } runId ? await db.PayrollRuns.FirstOrDefaultAsync(x => x.Id == runId, ct) : null;
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var action = run?.CalculatedAt is null ? PayrollCalculationAction.Calculated : PayrollCalculationAction.Recalculated;
        if (run is null)
        {
            run = new PayrollRun { Period = period, Type = PayrollRunType.Termination, Mode = PayrollRunMode.Calculated, CreatedAt = now, CreatedBy = currentUser.UserId };
            db.PayrollRuns.Add(run);
        }

        if (isNew)
        {
            db.EmployeeTerminations.Add(termination);
        }

        var payslip = await db.Payslips.FirstOrDefaultAsync(p => p.PayrollRunId == run.Id && p.EmployeeId == termination.EmployeeId, ct);
        (decimal Gross, decimal Net)? before = payslip is null || action == PayrollCalculationAction.Calculated ? null : (payslip.Gross, payslip.Net);
        if (payslip is null)
        {
            payslip = new Payslip { PayrollRunId = run.Id, EmployeeId = termination.EmployeeId, CreatedAt = now, UpdatedAt = now };
            db.Payslips.Add(payslip);
        }
        else
        {
            db.PayslipLines.RemoveRange(await db.PayslipLines.Where(l => l.PayslipId == payslip.Id).ToListAsync(ct));
        }

        db.PayslipLines.AddRange(r.Lines.Select((l, i) => ToLine(payslip.Id, l, i)));
        var snapshot = inputs.Value.Input.Employee;
        payslip.ApplyTotals(r.Totals);
        payslip.InssBase = r.InssBase;
        payslip.IrrfBase = r.IrrfBase;
        payslip.FgtsBase = r.FgtsBase;
        payslip.FgtsAmount = r.FgtsAmount;
        payslip.EmployerCharges = r.EmployerCharges;
        payslip.EmployerChargesMemory = PayrollCalculator.WriteMemory(r.EmployerMemory);
        payslip.CalculationIssues = PayrollCalculator.WriteIssues(r.Issues);
        payslip.InputsSnapshot = TerminationCalculator.WriteSnapshot(snapshot);
        payslip.TaxTableIds = [.. r.TableIds];
        payslip.ParameterIds = [.. inputs.Value.ParameterIds];
        payslip.CalculatorVersion = TerminationCalculator.Version;
        payslip.CalculatedAt = now;
        payslip.UpdatedAt = now;
        payslip.PdfAttachmentId = null;
        // O saldo do banco de horas acertado na rescisão baixa o espelho no fechamento (a mesma baixa da FLH-02b).
        payslip.HourBankDecisionMinutes = snapshot.Month.HourBank?.DecisionMinutes ?? 0;
        payslip.HourBankDecisionNote = payslip.HourBankDecisionMinutes == 0 ? null : "Acerto do saldo do banco de horas na rescisão.";
        await TerminationChargesAsync(run, r, result.Summary, ct);
        db.PayrollCalculationLogs.Add(new PayrollCalculationLog
        {
            PayrollRunId = run.Id, Action = action, At = now, By = currentUser.UserId, CalculatorVersion = TerminationCalculator.Version, TaxTableIds = [.. r.TableIds],
            ParameterIds = [.. inputs.Value.ParameterIds], Employees = 1, GrossBefore = before?.Gross, NetBefore = before?.Net, GrossAfter = r.Totals.Gross, NetAfter = r.Totals.Net,
        });
        run.PaymentDate = result.Summary.PaymentDeadline;
        run.CalculatedAt = now;
        run.CalculatedBy = currentUser.UserId;
        run.CalculatorVersion = TerminationCalculator.Version;
        run.IsSimulation = !inputs.Value.Official;
        run.CalculatingSince = null;
        termination.NoticeDays = result.Summary.NoticeDays;
        termination.PayrollRunId = run.Id;
        termination.Status = TerminationStatus.Calculated;
        termination.CalculatedAt = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return (run, payslip, result, inputs.Value);
    }

    /// <summary>"Recalcular" na folha de rescisão (e depois de um avulso): refaz pela rescisão ligada a ela.</summary>
    internal async Task<Result<PayrollCalculationStart>> RecalculateTerminationRunAsync(Guid runId, CancellationToken ct)
    {
        if (!await CanSensitiveAsync(ct))
        {
            return Forbidden();
        }

        var termination = await db.EmployeeTerminations.FirstOrDefaultAsync(t => t.PayrollRunId == runId && t.Status != TerminationStatus.Cancelled, ct);
        if (termination is null)
        {
            return Error.Validation(PayrollCalculationRules.NotSupportedCode, "Esta folha de rescisão não tem rescisão ligada: calcule pela aba Rescisão do funcionário.");
        }

        var done = await CalculateTerminationAsync(termination, false, ct);
        return done.IsFailure ? done.Error! : new PayrollCalculationStart((await runs.GetAsync(runId, ct)).Value, false, 1, "Rescisão calculada.");
    }

    /// <summary>
    /// Guias da folha de rescisão (refeitas a cada cálculo): INSS (o retido do mês e do 13º e os encargos da empresa) e IRRF retido no
    /// vencimento padrão das guias; o FGTS rescisório (o do mês e o das verbas) e a multa do FGTS no prazo do pagamento (CA 8), com o órgão
    /// padrão de cada guia (ADR 0036). O órgão trocado à mão fica; o vencimento segue o desligamento.
    /// </summary>
    private async Task TerminationChargesAsync(PayrollRun run, PayrollCalcResult r, TerminationSummary summary, CancellationToken ct)
    {
        var old = await db.PayrollRunCharges.Where(c => c.PayrollRunId == run.Id && c.Calculated).ToListAsync(ct);
        var keep = old.GroupBy(c => c.Description).ToDictionary(g => g.Key, g => g.First());
        db.PayrollRunCharges.RemoveRange(old);
        var defaults = (await parameters.ChargeDefaultsAsync(run.Period, ct)).ToDictionary(d => d.Kind);
        var label = PayrollRules.PeriodLabel(run.Period);
        var now = clock.GetUtcNow();
        foreach (var (kind, amount, description, due) in new[]
        {
            (PayrollChargeKind.Inss, r.Inss + r.EmployerCharges, $"INSS rescisão {label}", defaults[PayrollChargeKind.Inss].DueDate),
            (PayrollChargeKind.Irrf, r.Irrf, $"IRRF retido rescisão {label}", defaults[PayrollChargeKind.Irrf].DueDate),
            (PayrollChargeKind.Fgts, summary.FgtsMonth + summary.FgtsTermination, $"FGTS rescisório {label}", (DateOnly?)summary.PaymentDeadline),
            (PayrollChargeKind.Fgts, summary.Fine, $"Multa do FGTS rescisão {label}", (DateOnly?)summary.PaymentDeadline),
        })
        {
            if (amount <= 0m)
            {
                continue;
            }

            var previous = keep.GetValueOrDefault(description);
            db.PayrollRunCharges.Add(new PayrollRunCharge
            {
                PayrollRunId = run.Id, Kind = kind, Description = description, Amount = amount,
                DueDate = due ?? PayrollRules.DueDateNextMonth(run.Period, HrParametersService.DefaultDueDay),
                PayeePartyId = previous?.PayeePartyId ?? defaults[kind].PayeePartyId, CreatedAt = now, Calculated = true,
            });
        }
    }

    /// <summary>
    /// CA 9: quem tem a rescisão fechada no mês sai da folha Mensal calculada da competência (o saldo de salário foi pago na rescisão); o
    /// holerite em rascunho dele sai com as linhas.
    /// </summary>
    internal async Task RemoveFromMonthlyAsync(Guid employeeId, DateOnly period, CancellationToken ct)
    {
        var slips = await (from p in db.Payslips
                           join r in db.PayrollRuns on p.PayrollRunId equals r.Id
                           where p.EmployeeId == employeeId && r.Period == period && r.Type == PayrollRunType.Monthly && r.Mode == PayrollRunMode.Calculated && r.Status == PayrollRunStatus.Draft
                               && r.RectifiesRunId == null
                           select p).ToListAsync(ct);
        var ids = slips.Select(p => p.Id).ToList();
        db.PayslipLines.RemoveRange(await db.PayslipLines.Where(l => ids.Contains(l.PayslipId)).ToListAsync(ct));
        db.Payslips.RemoveRange(slips);
    }

    /// <summary>Quem tem a rescisão fechada com o desligamento na competência (fora da Mensal calculada, CA 9).</summary>
    internal async Task<List<Guid>> SettledInPeriodAsync(DateOnly period, CancellationToken ct)
    {
        var end = PayrollRules.PeriodEnd(period);
        return await db.EmployeeTerminations.AsNoTracking().Where(t => t.Status == TerminationStatus.Closed && t.TerminationDate >= period && t.TerminationDate <= end)
            .Select(t => t.EmployeeId).ToListAsync(ct);
    }
}
