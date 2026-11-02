using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Application.Documents;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Folha de férias calculada (FLH-03 CA 6 a 9): uma folha <c>Vacation</c>/<c>Calculated</c> por programação, com o holerite do funcionário pelo
/// <see cref="VacationCalculator"/> (puro, no Domain, com o INSS e o IRRF das tabelas da FLH-01), o retrato das entradas, a memória, a versão
/// do motor, as tabelas e os parâmetros usados, as guias do INSS e do IRRF retidos e a data de pagamento no vencimento legal. Antes da virada
/// e do primeiro caso de férias conferido (FLH-06), é simulação: não fecha e a prévia do recibo sai com a marca "Simulação". Nenhum valor vai
/// para log, evento ou argumento de job.
/// </summary>
public sealed partial class VacationService
{
    /// <summary>
    /// Regras de rubrica que contam como variáveis na média das férias (<c>hr.vacation_averages</c>), além dos proventos avulsos com base do
    /// INSS: as horas extras, o adicional noturno e o DSR sobre as variáveis da FLH-02b (calculados ou lançados à mão nessas rubricas).
    /// </summary>
    public static readonly IReadOnlySet<PayrollCalculation> VariableCalculations = new HashSet<PayrollCalculation>
    {
        PayrollCalculation.OvertimeWorkday, PayrollCalculation.OvertimeRestDay, PayrollCalculation.OvertimeHoliday, PayrollCalculation.NightShift, PayrollCalculation.Dsr,
    };

    /// <summary>Rubricas do seed que não são variáveis (salário, férias, 13º): ficam fora da média mesmo lançadas à mão.</summary>
    private static readonly HashSet<string> FixedSeedKeys = new(StringComparer.Ordinal)
    {
        PayrollItemService.SalarySeedKey, "ferias", "terco_ferias", "ferias_medias", "abono_pecuniario", "terco_abono", "decimo_terceiro", "insalubridade", "periculosidade", "salario_familia",
    };

    /// <summary>
    /// "Calcular férias" (CA 6): <c>hr.edit</c> na rota e <c>hr.view_sensitive</c> no serviço. A programação não pode estar cancelada nem com a
    /// folha de férias fechada (409 "Reabra a folha de férias antes."). Recalcular refaz o holerite da mesma folha em rascunho.
    /// </summary>
    public async Task<Result<VacationCalculationResult>> CalculateAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        if (!await permissions.HasPermissionAsync("hr", PositionService.SensitiveAction, cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso aos valores da folha. Fale com o administrador.");
        }

        var schedule = await db.VacationSchedules.FirstOrDefaultAsync(s => s.Id == scheduleId, cancellationToken);
        if (schedule is null)
        {
            return ScheduleNotFound();
        }

        if (schedule.Status == VacationScheduleStatus.Cancelled)
        {
            return Error.Conflict("vacation_schedule_cancelled", "A programação foi cancelada: programe as férias de novo.");
        }

        var run = schedule.PayrollRunId is { } runId ? await db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken) : null;
        if (run is not null && run.Status != PayrollRunStatus.Draft)
        {
            return Error.Conflict(VacationPolicy.RunClosedCode, VacationPolicy.RunClosedMessage);
        }

        var period = await db.VacationPeriods.AsNoTracking().FirstAsync(p => p.Id == schedule.PeriodId, cancellationToken);
        var employee = await db.Employees.IgnoreQueryFilters().AsNoTracking().FirstAsync(e => e.Id == schedule.EmployeeId, cancellationToken);
        var payMonth = new DateOnly(schedule.PaymentDueOn.Year, schedule.PaymentDueOn.Month, 1);
        var tables = new List<PayrollCalcTable>();
        foreach (var kind in Enum.GetValues<PayrollTaxTableKind>())
        {
            var found = await taxTables.ForCompetenceAsync(kind, payMonth, cancellationToken);
            if (found.IsFailure)
            {
                return found.Error!;
            }

            tables.Add(Table(found.Value));
        }

        var inssByMonth = new Dictionary<DateOnly, PayrollCalcTable>();
        foreach (var (month, _) in VacationPolicy.DaysByMonth(schedule.StartsOn, schedule.EndsOn))
        {
            var found = await taxTables.ForCompetenceAsync(PayrollTaxTableKind.Inss, month, cancellationToken);
            if (found.IsFailure)
            {
                return found.Error!;
            }

            inssByMonth[month] = Table(found.Value);
        }

        var all = await db.PayrollItems.AsNoTracking().OrderBy(i => i.SeedKey == null).ThenBy(i => i.Code).ToListAsync(cancellationToken);
        var items = all.Select(i => new PayrollCalcItem(i.Id, i.Code, i.Name, i.Kind, i.IsActive ? i.Calculation : PayrollCalculation.None, i.InssBase, i.FgtsBase, i.IrrfBase, i.DeductsIrrfBase)).ToList();
        if (VacationCalculator.Required.FirstOrDefault(r => items.All(i => i.Calculation != r)) is var missing && missing != PayrollCalculation.None)
        {
            return Error.Validation(PayrollCalculationRules.ItemMissingCode, $"Falta a rubrica com a regra {PayrollCalculator.Label(missing)}: confira em RH → Rubricas.") with
            {
                Data = new Dictionary<string, object?> { ["link"] = "/app/rh/rubricas" },
            };
        }

        var (calcPrm, calcIds) = await parameters.CalculationParametersAsync(cancellationToken);
        var (vacPrm, vacIds) = await parameters.VacationParametersAsync(cancellationToken);
        var salary = (await runs.SalariesAtAsync(new Dictionary<Guid, decimal> { [employee.Id] = employee.BaseSalary }, schedule.StartsOn, cancellationToken)).GetValueOrDefault(employee.Id);
        var dependents = await db.EmployeeDependents.AsNoTracking().CountAsync(d => d.EmployeeId == employee.Id && d.IrDependent, cancellationToken);
        var snapshot = new VacationCalcSnapshot(salary, employee.AdmissionDate, employee.InsalubrityPct, employee.HazardPay, dependents, period.StartsOn, period.EndsOn, period.ConcessionEndsOn,
            schedule.StartsOn, schedule.Days, schedule.SoldDays, schedule.Advance13th, schedule.PaymentDueOn, await VariablesAsync(employee.Id, period.EndsOn, vacPrm.AverageMonths, cancellationToken),
            calcPrm, vacPrm);
        var result = VacationCalculator.Calculate(new VacationCalcInput(snapshot, items, tables, inssByMonth));
        if (result.Payslip.Issues.FirstOrDefault(i => i.Blocking) is { } blocking)
        {
            return Error.Validation("vacation_calculation_blocked", PayrollService.Capitalize(blocking.Message));
        }

        // FLH-06: a única fonte do "a calculada é a oficial" (a Mensal virada e o tipo Férias virado, a partir da competência).
        var official = await parameters.CalculatedOfficialAsync(PayrollRunType.Vacation, new DateOnly(schedule.StartsOn.Year, schedule.StartsOn.Month, 1), cancellationToken);
        var now = clock.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var action = run?.CalculatedAt is null ? PayrollCalculationAction.Calculated : PayrollCalculationAction.Recalculated;
        if (run is null)
        {
            run = new PayrollRun
            {
                Period = new DateOnly(schedule.StartsOn.Year, schedule.StartsOn.Month, 1), Type = PayrollRunType.Vacation, Mode = PayrollRunMode.Calculated, CreatedAt = now,
                CreatedBy = currentUser.UserId,
            };
            db.PayrollRuns.Add(run);
        }

        var payslip = await db.Payslips.FirstOrDefaultAsync(p => p.PayrollRunId == run.Id && p.EmployeeId == employee.Id, cancellationToken);
        (decimal Gross, decimal Net)? before = payslip is null || action == PayrollCalculationAction.Calculated ? null : (payslip.Gross, payslip.Net);
        if (payslip is null)
        {
            payslip = new Payslip { PayrollRunId = run.Id, EmployeeId = employee.Id, CreatedAt = now, UpdatedAt = now };
            db.Payslips.Add(payslip);
        }
        else
        {
            db.PayslipLines.RemoveRange(await db.PayslipLines.Where(l => l.PayslipId == payslip.Id).ToListAsync(cancellationToken));
        }

        var r = result.Payslip;
        db.PayslipLines.AddRange(r.Lines.Select((l, i) => new PayslipLine
        {
            PayslipId = payslip.Id, PayrollItemId = l.ItemId, Code = l.Code, Description = l.Name, Type = l.Type, Reference = l.Reference, Amount = l.Amount, Sort = i, Calculated = true,
            Memory = PayrollCalculator.WriteMemory(l.Memory),
        }));
        var parameterIds = calcIds.Concat(vacIds).Distinct().ToList();
        payslip.ApplyTotals(r.Totals);
        payslip.InssBase = r.InssBase;
        payslip.IrrfBase = r.IrrfBase;
        payslip.FgtsBase = r.FgtsBase;
        payslip.FgtsAmount = r.FgtsAmount;
        payslip.EmployerCharges = 0m;
        payslip.EmployerChargesMemory = PayrollCalculator.WriteMemory(r.EmployerMemory);
        payslip.CalculationIssues = PayrollCalculator.WriteIssues(r.Issues);
        payslip.InputsSnapshot = VacationCalculator.WriteSnapshot(snapshot);
        payslip.TaxTableIds = [.. r.TableIds];
        payslip.ParameterIds = parameterIds;
        payslip.CalculatorVersion = PayrollCalculator.Version;
        payslip.CalculatedAt = now;
        payslip.UpdatedAt = now;
        payslip.PdfAttachmentId = null;
        await ChargesAsync(run, r.Inss, r.Irrf, payMonth, cancellationToken);
        db.PayrollCalculationLogs.Add(new PayrollCalculationLog
        {
            PayrollRunId = run.Id, Action = action, At = now, By = currentUser.UserId, CalculatorVersion = PayrollCalculator.Version, TaxTableIds = [.. r.TableIds], ParameterIds = parameterIds,
            Employees = 1, GrossBefore = before?.Gross, NetBefore = before?.Net, GrossAfter = r.Totals.Gross, NetAfter = r.Totals.Net,
        });
        run.PaymentDate = schedule.PaymentDueOn;
        run.CalculatedAt = now;
        run.CalculatedBy = currentUser.UserId;
        run.CalculatorVersion = PayrollCalculator.Version;
        run.IsSimulation = !official;
        run.CalculatingSince = null;
        schedule.PayrollRunId = run.Id;
        schedule.CalculatedSplit = VacationPolicy.WriteSplit(result.Split);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var summary = (await runs.GetAsync(run.Id, cancellationToken)).Value;
        var item = (await SchedulesAsync(s => s.Id == schedule.Id, cancellationToken))[0];
        List<PayrollNextStep> next = official
            ? [new("Fechar folha de férias", PayrollClosingService.RunLink(run.Id, "fechamento"))]
            : [new("Baixar prévia do recibo", $"/api/v1/hr/payroll-runs/{run.Id}/payslips/{payslip.Id}/pdf"), new("Ver folha de férias", PayrollClosingService.RunLink(run.Id, "holerites"))];
        return new VacationCalculationResult(item, summary, payslip.Id, official ? "Férias calculadas." : "Férias calculadas em simulação: a folha de férias oficial continua a lançada.", next);
    }

    /// <summary>"Recalcular" na folha de férias calculada (FLH-02a CA 13): refaz pela programação ligada a ela.</summary>
    public async Task<Result<PayrollCalculationStart>> RecalculateRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var scheduleId = await db.VacationSchedules.AsNoTracking().Where(s => s.PayrollRunId == runId).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(cancellationToken);
        if (scheduleId is not { } id)
        {
            return Error.Validation(PayrollCalculationRules.NotSupportedCode, "Esta folha de férias não tem programação: calcule pelas férias do funcionário (RH → Férias).");
        }

        var done = await CalculateAsync(id, cancellationToken);
        return done.IsFailure ? done.Error! : new PayrollCalculationStart(done.Value.Run, false, 1, done.Value.Message);
    }

    /// <summary>A data limite do pagamento das férias da folha (CA 9): o menor vencimento das programações ligadas, ou null.</summary>
    public async Task<DateOnly?> PaymentDeadlineAsync(Guid runId, CancellationToken cancellationToken = default) =>
        await db.VacationSchedules.AsNoTracking().Where(s => s.PayrollRunId == runId && s.Status != VacationScheduleStatus.Cancelled).MinAsync(s => (DateOnly?)s.PaymentDueOn, cancellationToken);

    /// <summary>Fechada a folha de férias, a programação fica paga; reaberta, volta a programada (CA 9 e 11).</summary>
    public async Task MarkPaidAsync(Guid runId, bool paid, CancellationToken cancellationToken = default)
    {
        var from = paid ? VacationScheduleStatus.Scheduled : VacationScheduleStatus.Paid;
        foreach (var s in await db.VacationSchedules.Where(s => s.PayrollRunId == runId && s.Status == from).ToListAsync(cancellationToken))
        {
            s.Status = paid ? VacationScheduleStatus.Paid : VacationScheduleStatus.Scheduled;
        }
    }

    /// <summary>"Gerar aviso de férias" (CA 5): o aviso em PDF pelo <c>IPdfRenderer</c>, sem valor; com <c>hr.view</c>.</summary>
    public async Task<Result<PayslipFile>> NoticeAsync(Guid scheduleId, CancellationToken cancellationToken = default)
    {
        var schedule = await db.VacationSchedules.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scheduleId && s.Status != VacationScheduleStatus.Cancelled, cancellationToken);
        if (schedule is null)
        {
            return ScheduleNotFound();
        }

        var period = await db.VacationPeriods.AsNoTracking().FirstAsync(p => p.Id == schedule.PeriodId, cancellationToken);
        var person = await (from e in db.Employees.IgnoreQueryFilters().AsNoTracking()
                            join pos in db.JobPositions.IgnoreQueryFilters().AsNoTracking() on e.PositionId equals pos.Id
                            where e.Id == schedule.EmployeeId
                            select new { e.Name, e.Registration, Position = pos.Name }).FirstAsync(cancellationToken);
        var model = new VacationNoticeModel(await documents.CompanyAsync(cancellationToken), person.Name, person.Registration, person.Position, period.StartsOn, period.EndsOn, schedule.StartsOn,
            schedule.EndsOn, schedule.Days, schedule.SoldDays, schedule.PaymentDueOn, BusinessTime.Today(clock));
        var bytes = await renderer.RenderAsync(new VacationNoticePdfDocument(model), cancellationToken);
        await audit.RecordReadAsync(nameof(VacationSchedule) + ".Notice", scheduleId.ToString(), cancellationToken);
        return new PayslipFile(new MemoryStream(bytes), $"aviso-ferias-{schedule.StartsOn:yyyy-MM-dd}.pdf", Documents.PayslipDocumentService.PdfContentType);
    }

    private static PayrollCalcTable Table(PayrollTaxTableInForce t) => new(t.Id, t.Kind, t.ValidFrom, t.Status, t.Rounding, t.Content);

    /// <summary>
    /// Variáveis dos últimos meses do período aquisitivo (<c>hr.vacation_averages</c>): os proventos das folhas mensais fechadas com regra de
    /// variável (<see cref="VariableCalculations"/>) ou avulsos com base do INSS; no mês com folha calculada fechada, só ela (senão a lançada).
    /// </summary>
    private async Task<List<VacationCalcVariable>> VariablesAsync(Guid employeeId, DateOnly periodEnd, int months, CancellationToken ct)
    {
        var last = new DateOnly(periodEnd.Year, periodEnd.Month, 1);
        var first = last.AddMonths(-(Math.Max(months, 1) - 1));
        var rows = await (from l in db.PayslipLines.AsNoTracking()
                          join p in db.Payslips.AsNoTracking() on l.PayslipId equals p.Id
                          join r in db.PayrollRuns.AsNoTracking() on p.PayrollRunId equals r.Id
                          join i in db.PayrollItems.IgnoreQueryFilters().AsNoTracking() on l.PayrollItemId equals i.Id
                          where p.EmployeeId == employeeId && r.Type == PayrollRunType.Monthly && r.RectifiesRunId == null && (r.Status == PayrollRunStatus.Closed || r.Status == PayrollRunStatus.Paid)
                              && r.Period >= first && r.Period <= last && l.Type == PayrollItemKind.Earning
                          select new { r.Period, r.Mode, l.Amount, i.Calculation, i.InssBase, i.SeedKey }).ToListAsync(ct);
        return rows.GroupBy(x => x.Period).Select(g =>
        {
            var mode = g.Any(x => x.Mode == PayrollRunMode.Calculated) ? PayrollRunMode.Calculated : PayrollRunMode.Entered;
            var amount = g.Where(x => x.Mode == mode && (VariableCalculations.Contains(x.Calculation)
                || (x.Calculation == PayrollCalculation.None && x.InssBase && (x.SeedKey is null || !FixedSeedKeys.Contains(x.SeedKey))))).Sum(x => x.Amount);
            return new VacationCalcVariable(g.Key, amount);
        }).Where(v => v.Amount > 0m).OrderBy(v => v.Month).ToList();
    }

    /// <summary>
    /// Guias da folha de férias (refeitas a cada cálculo): o INSS retido (vencimento da competência do início) e o IRRF retido (da competência do
    /// pagamento). O FGTS e os encargos da empresa sobre as férias vão na guia da folha mensal da competência (CA 10).
    /// </summary>
    private async Task ChargesAsync(PayrollRun run, decimal inss, decimal irrf, DateOnly payMonth, CancellationToken ct)
    {
        var old = await db.PayrollRunCharges.Where(c => c.PayrollRunId == run.Id && c.Calculated).ToListAsync(ct);
        var keep = old.ToDictionary(c => c.Kind);
        db.PayrollRunCharges.RemoveRange(old);
        var now = clock.GetUtcNow();
        foreach (var (kind, amount, competence, label) in new[] { (PayrollChargeKind.Inss, inss, run.Period, "INSS férias"), (PayrollChargeKind.Irrf, irrf, payMonth, "IRRF retido férias") })
        {
            if (amount <= 0m)
            {
                continue;
            }

            var d = (await parameters.ChargeDefaultsAsync(competence, ct)).First(x => x.Kind == kind);
            var previous = keep.GetValueOrDefault(kind);
            db.PayrollRunCharges.Add(new PayrollRunCharge
            {
                PayrollRunId = run.Id, Kind = kind, Description = $"{label} {PayrollRules.PeriodLabel(competence)}", Amount = amount,
                DueDate = previous?.DueDate ?? d.DueDate ?? PayrollRules.DueDateNextMonth(competence, HrParametersService.DefaultDueDay), PayeePartyId = previous?.PayeePartyId ?? d.PayeePartyId,
                CreatedAt = now, Calculated = true,
            });
        }
    }
}
