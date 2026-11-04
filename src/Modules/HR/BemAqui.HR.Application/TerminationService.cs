using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.HR.Application.Documents;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Rescisão (FLH-05): a aba "Rescisão" do funcionário, "Simular rescisão" (sem gravar nada, CA 1), "Calcular rescisão" depois do desligamento
/// (CAD-04b, CA 3 e 4), o termo para conferência (CA 6), o fechamento pela folha (RH-02c, CA 7 a 9, pelos ganchos do
/// <see cref="PayrollClosingService"/>) e o estorno do desligamento (CA 10). O cálculo é o <see cref="TerminationCalculator"/> (puro), com o
/// retrato montado pelo <see cref="PayrollCalculationService"/>. Valores só com <c>hr.view_sensitive</c>; nada vai para log, evento ou job.
/// </summary>
public sealed class TerminationService(
    IHrDb db,
    IPermissionChecker permissions,
    HrParametersService parameters,
    PayrollCalculationService calculation,
    PayrollService runs,
    VacationService vacations,
    PayslipDocumentService documents,
    IPdfRenderer renderer,
    IPayables payables,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    /// <summary>Aba "Rescisão" (CA 1, 3 e 10): a rescisão do funcionário (a não cancelada), os tipos da matriz, os padrões e o próximo passo.</summary>
    public async Task<Result<TerminationView>> GetAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var employee = await db.Employees.IgnoreQueryFilters().AsNoTracking().Where(e => e.Id == employeeId && !e.IsDeleted)
            .Select(e => new { e.Id, e.Name, e.AdmissionDate, e.TerminationDate, e.Status }).FirstOrDefaultAsync(cancellationToken);
        if (employee is null)
        {
            return EmployeeNotFound();
        }

        var (prm, _) = await parameters.TerminationParametersAsync(cancellationToken);
        var termination = await db.EmployeeTerminations.AsNoTracking().Where(t => t.EmployeeId == employeeId && t.Status != TerminationStatus.Cancelled).FirstOrDefaultAsync(cancellationToken);
        var item = termination is null ? null : await ItemAsync(termination, cancellationToken);
        var terminated = employee.Status == EmployeeStatus.Terminated && employee.TerminationDate is not null;
        var date = employee.TerminationDate ?? BusinessTime.Today(clock);
        var next = new List<PayrollNextStep>();
        if (terminated && item is null)
        {
            next.Add(new PayrollNextStep("Calcular rescisão", EmployeeLink(employeeId)));
        }
        else if (item is { Status: TerminationStatus.Calculated, PayrollRunId: { } runId })
        {
            next.Add(item.IsSimulation ? new PayrollNextStep("Baixar termo para conferência", TermLink(item.Id)) : new PayrollNextStep("Fechar folha de rescisão", PayrollClosingService.RunLink(runId, "fechamento")));
        }

        return new TerminationView(employee.Id, employee.Name, terminated, employee.TerminationDate, item,
            prm.Matrix.Select(r => new TerminationKindOption(r.Kind, TerminationPolicy.KindLabel(r.Kind), TerminationPolicy.DefaultNotice(r.Kind), TerminationPolicy.NeedsContractEnd(r.Kind), r.FgtsFinePct)).ToList(),
            date, TerminationPolicy.NoticeDays(employee.AdmissionDate, date, prm), prm.PaymentDays, next);
    }

    /// <summary>
    /// "Simular rescisão" (CA 1): <c>hr.edit</c> na rota e <c>hr.view_sensitive</c> aqui; o tipo, a data prevista e o aviso; as verbas e os
    /// descontos com a memória, o líquido, o FGTS, a multa e o custo total para a empresa, sem gravar nada (R-PRP-7). A leitura sensível é auditada.
    /// </summary>
    public async Task<Result<TerminationResultView>> SimulateAsync(Guid employeeId, TerminationRequestInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var employee = await db.Employees.AsNoTracking().Where(e => e.Id == employeeId).Select(e => new { e.Id, e.AdmissionDate }).FirstOrDefaultAsync(cancellationToken);
        if (employee is null)
        {
            return EmployeeNotFound();
        }

        if (input.Kind is null || input.TerminationDate is null)
        {
            return PayrollRules.Field(input.Kind is null ? "kind" : "terminationDate", TerminationPolicy.EmptySimulationMessage);
        }

        var request = Validate(input, input.TerminationDate.Value, employee.AdmissionDate);
        if (request.IsFailure)
        {
            return request.Error!;
        }

        var inputs = await calculation.TerminationInputsAsync(employeeId, request.Value, null, cancellationToken);
        if (inputs.IsFailure)
        {
            return inputs.Error!;
        }

        var result = TerminationCalculator.Calculate(inputs.Value.Input);
        if (result.Payslip.Issues.FirstOrDefault(i => i.Blocking) is { } blocking)
        {
            return Error.Validation("termination_calculation_blocked", PayrollService.Capitalize(blocking.Message));
        }

        await audit.RecordReadAsync(nameof(EmployeeTermination) + ".Simulate", employeeId.ToString(), cancellationToken);
        return View(request.Value, result, inputs.Value);
    }

    /// <summary>
    /// "Calcular rescisão" (CA 3 e 4): com o desligamento registrado (CAD-04b) e o espelho do mês fechado, nasce (ou é refeita) a folha
    /// <c>Termination</c> do funcionário. O saldo do FGTS do extrato é obrigatório nos tipos com multa (422). Fechada, só reabrindo a folha (409).
    /// </summary>
    public async Task<Result<TerminationCalculationResult>> CalculateAsync(Guid employeeId, TerminationRequestInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var employee = await db.Employees.AsNoTracking().Where(e => e.Id == employeeId).Select(e => new { e.Id, e.AdmissionDate, e.TerminationDate, e.Status }).FirstOrDefaultAsync(cancellationToken);
        if (employee is null)
        {
            return EmployeeNotFound();
        }

        if (employee.Status != EmployeeStatus.Terminated || employee.TerminationDate is not { } date)
        {
            return Error.Conflict(TerminationPolicy.NotTerminatedCode, "Registre o desligamento antes de calcular a rescisão: até lá, use \"Simular rescisão\".");
        }

        var termination = await db.EmployeeTerminations.FirstOrDefaultAsync(t => t.EmployeeId == employeeId && t.Status != TerminationStatus.Cancelled, cancellationToken);
        if (termination is not null && await RunClosedAsync(termination, cancellationToken))
        {
            return Error.Conflict(TerminationPolicy.ClosedCode, TerminationPolicy.ClosedMessage) with
            {
                Data = new Dictionary<string, object?> { ["link"] = PayrollClosingService.RunLink(termination.PayrollRunId!.Value, "historico") },
            };
        }

        if (input.Kind is null)
        {
            return PayrollRules.Field("kind", "Escolha o tipo do desligamento.");
        }

        var request = Validate(input, date, employee.AdmissionDate);
        if (request.IsFailure)
        {
            return request.Error!;
        }

        var (prm, _) = await parameters.TerminationParametersAsync(cancellationToken);
        if (prm.Row(request.Value.Kind).FgtsFinePct > 0m && request.Value.FgtsBalance is null)
        {
            return Error.Validation(TerminationPolicy.FgtsBalanceCode, TerminationPolicy.FgtsBalanceMessage, new Dictionary<string, string[]> { ["fgtsBalance"] = [TerminationPolicy.FgtsBalanceMessage] });
        }

        var month = new DateOnly(date.Year, date.Month, 1);
        if (!await db.Timesheets.AsNoTracking().AnyAsync(t => t.EmployeeId == employeeId && t.Period == month && t.Status == TimesheetStatus.Closed, cancellationToken))
        {
            return Error.Validation(TerminationPolicy.TimesheetOpenCode, $"Feche o espelho de ponto de {PayrollRules.PeriodLabel(month)} até o desligamento antes de calcular.") with
            {
                Data = new Dictionary<string, object?> { ["link"] = $"/app/rh/ponto/espelho/{employeeId}?mes={month:yyyy-MM}" },
            };
        }

        var isNew = termination is null;
        termination ??= new EmployeeTermination { EmployeeId = employeeId, CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId };
        var r = request.Value;
        (termination.Kind, termination.NoticeKind, termination.TerminationDate, termination.ContractEndDate, termination.FgtsBalance, termination.FgtsStatementDate) =
            (r.Kind, r.NoticeKind, r.TerminationDate, r.ContractEndDate, r.FgtsBalance, r.FgtsStatementDate);
        var done = await calculation.CalculateTerminationAsync(termination, isNew, cancellationToken);
        if (done.IsFailure)
        {
            return done.Error!;
        }

        var (run, payslip, result, inputs) = done.Value;
        var summary = (await runs.GetAsync(run.Id, cancellationToken)).Value;
        var item = await ItemAsync(termination, cancellationToken);
        List<PayrollNextStep> next = run.IsSimulation
            ? [new("Baixar termo para conferência", TermLink(termination.Id)), new("Ver folha de rescisão", PayrollClosingService.RunLink(run.Id, "holerites"))]
            : [new("Fechar folha de rescisão", PayrollClosingService.RunLink(run.Id, "fechamento")), new("Baixar termo para conferência", TermLink(termination.Id))];
        return new TerminationCalculationResult(item, summary, payslip.Id, View(r, result, inputs),
            run.IsSimulation ? "Rescisão calculada em simulação: a rescisão oficial continua a lançada." : "Rescisão calculada.", next);
    }

    /// <summary>
    /// "Baixar termo para conferência" (CA 6 e 7): o PDF pelo <c>IPdfRenderer</c> com as verbas, os descontos e o líquido da folha de rescisão,
    /// a marca de conferência interna e, antes da virada, a de simulação. Com <c>hr.view_sensitive</c>; leitura auditada.
    /// </summary>
    public async Task<Result<PayslipFile>> TermAsync(Guid terminationId, CancellationToken cancellationToken = default)
    {
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var termination = await db.EmployeeTerminations.AsNoTracking().FirstOrDefaultAsync(t => t.Id == terminationId && t.Status != TerminationStatus.Cancelled, cancellationToken);
        if (termination?.PayrollRunId is not { } runId)
        {
            return Error.NotFound("termination_not_found", "Rescisão não calculada: clique em \"Calcular rescisão\" antes.");
        }

        var run = await db.PayrollRuns.AsNoTracking().FirstAsync(x => x.Id == runId, cancellationToken);
        var payslip = await db.Payslips.AsNoTracking().FirstAsync(p => p.PayrollRunId == runId && p.EmployeeId == termination.EmployeeId, cancellationToken);
        var lines = await db.PayslipLines.AsNoTracking().Where(l => l.PayslipId == payslip.Id).OrderBy(l => l.Sort).ToListAsync(cancellationToken);
        var person = await (from e in db.Employees.IgnoreQueryFilters().AsNoTracking()
                            join pos in db.JobPositions.IgnoreQueryFilters().AsNoTracking() on e.PositionId equals pos.Id
                            where e.Id == termination.EmployeeId
                            select new { e.Name, e.Registration, e.AdmissionDate, Position = pos.Name }).FirstAsync(cancellationToken);
        var fine = await FineAsync(runId, cancellationToken) ?? 0m;
        var model = new TerminationTermModel(person.Name, person.Registration, person.Position, person.AdmissionDate, termination.TerminationDate,
            TerminationPolicy.KindLabel(termination.Kind), TerminationPolicy.NoticeLabel(termination.NoticeKind), termination.NoticeDays,
            lines.Select(l => new TerminationTermLine(l.Code, l.Description, l.Reference, l.Type, l.Amount)).ToList(), payslip.Gross, payslip.Deductions, payslip.Net, payslip.FgtsAmount, fine,
            run.PaymentDate ?? termination.TerminationDate, BusinessTime.Today(clock));
        var bytes = await renderer.RenderAsync(new TerminationTermPdfDocument(await documents.CompanyAsync(cancellationToken), model), cancellationToken);
        await audit.RecordReadAsync(nameof(EmployeeTermination) + ".Term", terminationId.ToString(), cancellationToken);
        return new PayslipFile(new MemoryStream(bytes), $"termo-rescisao-conferencia-{termination.TerminationDate:yyyy-MM-dd}.pdf", PayslipDocumentService.PdfContentType);
    }

    /// <summary>
    /// Estornar o desligamento (CA 10): o funcionário volta antes do pagamento. Com a folha de rescisão fechada, reabra antes (409); com
    /// título pago, 409 "A rescisão já foi paga: fale com a contadora para o estorno.". A folha em rascunho sai; a rescisão fica cancelada
    /// (com quem, quando e o motivo) e o funcionário volta a ativo, sem apagar o histórico.
    /// </summary>
    public async Task<Result<TerminationView>> CancelAsync(Guid terminationId, TerminationCancelInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var reason = (input.Reason ?? string.Empty).Trim();
        if (reason.Length is < 5 or > 200)
        {
            return PayrollRules.Field("reason", "Informe o motivo do estorno, de 5 a 200 caracteres.");
        }

        var termination = await db.EmployeeTerminations.FirstOrDefaultAsync(t => t.Id == terminationId && t.Status != TerminationStatus.Cancelled, cancellationToken);
        if (termination is null)
        {
            return Error.NotFound("termination_not_found", "Rescisão não encontrada.");
        }

        if (termination.Version != input.Version)
        {
            return Stale();
        }

        var run = termination.PayrollRunId is { } runId ? await db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken) : null;
        if (run is not null && run.Status != PayrollRunStatus.Draft)
        {
            var origins = await db.Payslips.AsNoTracking().Where(p => p.PayrollRunId == run.Id).Select(p => p.Id)
                .Concat(db.PayrollRunCharges.AsNoTracking().Where(c => c.PayrollRunId == run.Id).Select(c => c.Id)).ToListAsync(cancellationToken);
            var titles = await payables.StatusByOriginAsync(DocumentOrigin.Payroll, origins, cancellationToken);
            var paid = run.Status == PayrollRunStatus.Paid || titles.Any(t => t.Status is PayableStatus.Paid or PayableStatus.PartiallyPaid);
            return Error.Conflict(paid ? TerminationPolicy.PaidCode : TerminationPolicy.ClosedCode, paid ? TerminationPolicy.PaidMessage : TerminationPolicy.ClosedMessage) with
            {
                Data = new Dictionary<string, object?> { ["link"] = PayrollClosingService.RunLink(run.Id, paid ? "pagamentos" : "historico") },
            };
        }

        var employee = await db.Employees.FirstAsync(e => e.Id == termination.EmployeeId, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Entry(termination).Property(t => t.Version).OriginalValue = input.Version;
        if (run is not null)
        {
            var slips = await db.Payslips.Where(p => p.PayrollRunId == run.Id).ToListAsync(cancellationToken);
            var slipIds = slips.Select(p => p.Id).ToList();
            db.PayslipLines.RemoveRange(await db.PayslipLines.Where(l => slipIds.Contains(l.PayslipId)).ToListAsync(cancellationToken));
            db.Payslips.RemoveRange(slips);
            db.PayrollRunCharges.RemoveRange(await db.PayrollRunCharges.Where(c => c.PayrollRunId == run.Id).ToListAsync(cancellationToken));
            db.PayrollCalculationLogs.RemoveRange(await db.PayrollCalculationLogs.Where(l => l.PayrollRunId == run.Id).ToListAsync(cancellationToken));
            termination.PayrollRunId = null;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Stale();
            }

            db.PayrollRuns.Remove(run);
        }

        termination.Status = TerminationStatus.Cancelled;
        termination.CancelledAt = clock.GetUtcNow();
        termination.CancelledBy = currentUser.UserId;
        termination.CancelReason = reason;
        employee.TerminationDate = null;
        employee.Status = EmployeeStatus.Active;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Stale();
        }

        await vacations.SyncAsync([employee.Id], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(employee.Id, cancellationToken);
    }

    /// <summary>O prazo legal do pagamento da folha de rescisão (CA 8): <c>hr.termination_payment_days</c> corridos do desligamento, ou null.</summary>
    public async Task<DateOnly?> PaymentDeadlineAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var termination = await db.EmployeeTerminations.AsNoTracking().Where(t => t.PayrollRunId == runId && t.Status != TerminationStatus.Cancelled).FirstOrDefaultAsync(cancellationToken);
        if (termination is null)
        {
            return null;
        }

        var (prm, _) = await parameters.TerminationParametersAsync(cancellationToken);
        return TerminationPolicy.PaymentDeadline(termination.TerminationDate, prm);
    }

    /// <summary>A multa do FGTS da folha de rescisão, para o diálogo do fechamento (CA 8), ou null.</summary>
    public async Task<decimal?> FineAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var amounts = await (from l in db.PayslipLines.AsNoTracking()
                             join p in db.Payslips.AsNoTracking() on l.PayslipId equals p.Id
                             join i in db.PayrollItems.IgnoreQueryFilters().AsNoTracking() on l.PayrollItemId equals i.Id
                             where p.PayrollRunId == runId && i.Calculation == PayrollCalculation.FgtsFine
                             select l.Amount).ToListAsync(cancellationToken);
        return amounts.Count == 0 ? null : amounts.Sum();
    }

    /// <summary>
    /// Fechada a folha de rescisão (CA 9), a rescisão fica fechada e o funcionário sai da Mensal calculada da competência; reaberta, volta a
    /// calculada. Os períodos de férias se encerram na próxima leitura (<see cref="VacationService"/>); o banco de horas baixa pela decisão do holerite.
    /// </summary>
    public async Task MarkClosedAsync(PayrollRun run, bool closed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        var termination = await db.EmployeeTerminations.FirstOrDefaultAsync(t => t.PayrollRunId == run.Id && t.Status != TerminationStatus.Cancelled, cancellationToken);
        if (termination is null)
        {
            return;
        }

        termination.Status = closed ? TerminationStatus.Closed : TerminationStatus.Calculated;
        termination.ClosedAt = closed ? clock.GetUtcNow() : null;
        if (closed)
        {
            await calculation.RemoveFromMonthlyAsync(termination.EmployeeId, run.Period, cancellationToken);
        }
    }

    private static Result<TerminationRequest> Validate(TerminationRequestInput input, DateOnly date, DateOnly admission)
    {
        if (input.Kind is not { } kind || !Enum.IsDefined(kind))
        {
            return PayrollRules.Field("kind", "Escolha o tipo do desligamento.");
        }

        var notice = input.NoticeKind ?? TerminationPolicy.DefaultNotice(kind);
        if (!Enum.IsDefined(notice))
        {
            return PayrollRules.Field("noticeKind", "Escolha o aviso prévio: trabalhado, indenizado ou dispensado.");
        }

        if (date < admission)
        {
            return PayrollRules.Field("terminationDate", "A data do desligamento precisa ser igual ou depois da admissão.");
        }

        if (TerminationPolicy.NeedsContractEnd(kind) && (input.ContractEndDate is not { } end || end <= date))
        {
            return PayrollRules.Field("contractEndDate", "Informe o fim previsto do contrato de experiência, depois do desligamento.");
        }

        if (input.FgtsBalance is { } balance && !PayrollRules.ValidBase(balance))
        {
            return PayrollRules.Field("fgtsBalance", "Informe o saldo do FGTS do extrato, zero ou mais, com até 2 casas.");
        }

        if (input.FgtsBalance is not null && input.FgtsStatementDate is null)
        {
            return PayrollRules.Field("fgtsStatementDate", "Informe a data do extrato do FGTS.");
        }

        return new TerminationRequest(kind, notice, date, TerminationPolicy.NeedsContractEnd(kind) ? input.ContractEndDate : null, input.FgtsBalance, input.FgtsBalance is null ? null : input.FgtsStatementDate);
    }

    private static TerminationResultView View(TerminationRequest request, TerminationCalcResult result, TerminationInputs inputs)
    {
        var r = result.Payslip;
        var s = result.Summary;
        var warnings = r.Issues.Where(i => !i.Blocking).Select(i => PayrollService.Capitalize(i.Message)).ToList();
        if (inputs.HourBankBalance < 0 && !inputs.Input.Employee.Termination.DiscountNegativeHourBank)
        {
            warnings.Add($"Saldo negativo de {TimesheetRules.Duration(-inputs.HourBankBalance)} no banco de horas: não descontado na rescisão (parâmetro hr.hour_bank_termination).");
        }

        return new TerminationResultView(request.Kind, TerminationPolicy.KindLabel(request.Kind), request.NoticeKind, request.TerminationDate, s.NoticeDays, s.PaidNoticeDays, s.ProjectedEnd,
            r.Lines.Select(l => new TerminationLineView(l.Code, l.Name, l.Type, l.Reference, l.Amount, l.Memory)).ToList(), r.Totals.Gross, r.Totals.Deductions, r.Totals.Net,
            s.FgtsMonth, s.FgtsTermination, s.FinePct, s.Fine, s.FineMemory, s.EmployerCharges, s.TotalCost, s.PaymentDeadline, warnings, TerminationCalculator.Version);
    }

    private async Task<TerminationItem> ItemAsync(EmployeeTermination t, CancellationToken ct)
    {
        var run = t.PayrollRunId is { } runId ? await db.PayrollRuns.AsNoTracking().Where(r => r.Id == runId).Select(r => new { r.Status, r.IsSimulation }).FirstOrDefaultAsync(ct) : null;
        var slip = t.PayrollRunId is { } id ? await db.Payslips.AsNoTracking().Where(p => p.PayrollRunId == id && p.EmployeeId == t.EmployeeId).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct) : null;
        var sensitive = await CanSensitiveAsync(ct);
        var (prm, _) = await parameters.TerminationParametersAsync(ct);
        return new TerminationItem(t.Id, t.Kind, TerminationPolicy.KindLabel(t.Kind), t.NoticeKind, TerminationPolicy.NoticeLabel(t.NoticeKind), t.NoticeDays, t.TerminationDate, t.ContractEndDate,
            sensitive ? t.FgtsBalance : null, t.FgtsStatementDate, t.Status, TerminationPolicy.StatusLabel(t.Status), t.PayrollRunId, slip, run?.IsSimulation ?? false,
            run is not null && run.Status != PayrollRunStatus.Draft, TerminationPolicy.PaymentDeadline(t.TerminationDate, prm), t.CalculatedAt, t.Version);
    }

    private async Task<bool> RunClosedAsync(EmployeeTermination t, CancellationToken ct) =>
        t.Status == TerminationStatus.Closed
        || (t.PayrollRunId is { } runId && await db.PayrollRuns.AsNoTracking().AnyAsync(r => r.Id == runId && r.Status != PayrollRunStatus.Draft, ct));

    internal static string EmployeeLink(Guid employeeId) => $"/app/rh/funcionarios/{employeeId}?aba=rescisao";

    internal static string TermLink(Guid terminationId) => $"/api/v1/hr/terminations/{terminationId}/term";

    private ValueTask<bool> CanSensitiveAsync(CancellationToken ct) => permissions.HasPermissionAsync("hr", PositionService.SensitiveAction, ct);

    private static Error EmployeeNotFound() => Error.NotFound("employee_not_found", "Funcionário não encontrado.");

    private static Error Stale() => Error.Conflict("concurrency_conflict", "A rescisão mudou enquanto você trabalhava. Recarregue.");

    private static Error Forbidden() => Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso aos valores da folha. Fale com o administrador.");
}
