using System.Globalization;
using System.Text.Json;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.HR.Domain;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Hangfire;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Folha calculada (FLH-02a, ADR 0039): monta o retrato das entradas de cada funcionário (cadastro, dependentes, afastamentos, espelho
/// fechado, avulsos e parâmetros), roda o <see cref="PayrollCalculator"/> (puro, no Domain) com as tabelas da competência (FLH-01) e grava
/// as linhas com a memória, as bases, o FGTS, os encargos, as guias calculadas, a versão do motor, as tabelas e os parâmetros usados.
/// Acima de 20 funcionários, roda no <see cref="PayrollCalculationJob"/> (só ids). Folha fechada nunca é recalculada: "Ver diferença" e
/// "Gerar retificação". Nenhum valor vai para log, evento ou argumento de job.
/// </summary>
public sealed partial class PayrollCalculationService(
    IHrDb db,
    IPermissionChecker permissions,
    IPayrollTaxTables taxTables,
    HrParametersService parameters,
    PayrollService runs,
    IBackgroundJobClient jobs,
    ITenantContext tenant,
    INotificationService notifications,
    IAuditWriter audit,
    IUserDirectory users,
    ICurrentUser currentUser,
    IBusinessCalendar calendar,
    TimeProvider clock)
{
    public const string NotificationType = "hr.payroll";

    /// <summary>Cálculo em segundo plano parado há mais que isso pode ser pedido de novo.</summary>
    public static readonly TimeSpan CalculatingTimeout = TimeSpan.FromMinutes(10);

    private static readonly PayrollCalculation[] Required = [PayrollCalculation.BaseSalary, PayrollCalculation.Inss, PayrollCalculation.Irrf, PayrollCalculation.Fgts];

    private sealed record Context(List<PayrollCalcTable> Tables, List<PayrollCalcItem> Items, PayrollCalcParameters Parameters, List<Guid> ParameterIds, bool Official);

    /// <summary>
    /// "Calcular" e "Recalcular" (CA 1 e 13): <c>hr.view_sensitive</c> (a rota exige <c>hr.create</c> ou <c>hr.edit</c>); folha Mensal,
    /// calculada e em rascunho; o espelho da competência fechado (422 com o link); as tabelas da competência (422 da FLH-01). Até 20
    /// funcionários, na hora; acima, em segundo plano, com o aviso no sino.
    /// </summary>
    public async Task<Result<PayrollCalculationStart>> CalculateAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var run = await db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
        {
            return RunNotFound();
        }

        if (Precondition(run) is { } invalid)
        {
            return invalid;
        }

        if (run.CalculatingSince is { } since && clock.GetUtcNow() - since < CalculatingTimeout)
        {
            return Error.Conflict(PayrollCalculationRules.CalculatingCode, "O cálculo desta folha já está em andamento. Aguarde o aviso no sino.");
        }

        // FLH-04: o 13º não depende do ponto do mês (as faltas do ano vêm dos espelhos fechados de cada mês).
        if (!ThirteenthCalculator.IsThirteenth(run.Type) && await TimesheetErrorAsync(run, cancellationToken) is { } timesheet)
        {
            return timesheet;
        }

        var context = await ContextForAsync(run, cancellationToken);
        if (context.IsFailure)
        {
            return context.Error!;
        }

        if (run.RectifiesRunId is null)
        {
            await AddEligibleAsync(run, cancellationToken);
        }

        var count = await db.Payslips.CountAsync(p => p.PayrollRunId == run.Id, cancellationToken);
        if (count > PayrollCalculationRules.InlineLimit)
        {
            run.CalculatingSince = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            var tenantId = tenant.Tenant?.Id ?? throw new InvalidOperationException("Cálculo da folha fora do escopo de tenant.");
            var args = new PayrollCalculationArgs(run.Id, currentUser.UserId);
            jobs.Create<PayrollCalculationJob>(j => j.ExecuteAsync(tenantId, args, CancellationToken.None), new EnqueuedState(JobQueues.Default));
            return new PayrollCalculationStart((await runs.GetAsync(run.Id, cancellationToken)).Value, true, count, $"Calculando {count} holerites… o aviso chega no sino.");
        }

        var done = await RunAsync(run, context.Value, currentUser.UserId, cancellationToken);
        if (done.IsFailure)
        {
            return done.Error!;
        }

        return new PayrollCalculationStart((await runs.GetAsync(run.Id, cancellationToken)).Value, false, done.Value, done.Value == 1 ? "1 holerite calculado." : $"{done.Value} holerites calculados.");
    }

    /// <summary>Competência sugerida para a folha calculada (Propositivo): a do último espelho de ponto fechado; null sem nenhum.</summary>
    public async Task<PayrollSuggestedPeriod> SuggestedPeriodAsync(CancellationToken cancellationToken = default)
    {
        var last = await db.Timesheets.AsNoTracking().Where(t => t.Status == TimesheetStatus.Closed).MaxAsync(t => (DateOnly?)t.Period, cancellationToken);
        return new PayrollSuggestedPeriod(last?.ToString("yyyy-MM", CultureInfo.InvariantCulture));
    }

    /// <summary>Segundo plano (CA 17): calcula e avisa quem pediu no sino; erro também volta pelo sino, sem valor.</summary>
    public async Task CompleteAsync(Guid runId, Guid? requestedBy, CancellationToken cancellationToken = default)
    {
        var run = await db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
        {
            return;
        }

        Error? result = Precondition(run);
        Result<int>? done = null;
        if (result is null)
        {
            var context = await ContextForAsync(run, cancellationToken);
            done = context.IsFailure ? Result<int>.Failure(context.Error!) : await RunAsync(run, context.Value, requestedBy, cancellationToken);
            result = done.IsFailure ? done.Error : null;
        }

        if (result is not null)
        {
            db.ChangeTracker.Clear();
            var tracked = await db.PayrollRuns.FirstAsync(r => r.Id == runId, cancellationToken);
            tracked.CalculatingSince = null;
        }

        if (requestedBy is { } user)
        {
            var link = PayrollClosingService.RunLink(runId, "holerites");
            notifications.Notify(user, NotificationType, result is null
                ? $"Folha de {PayrollRules.PeriodLabel(run.Period)} calculada: {done!.Value} holerites."
                : $"Não foi possível calcular a folha de {PayrollRules.PeriodLabel(run.Period)}: {result.Message}", link);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Lançamento avulso (CA 13): rubrica sem regra com motivo; entra nas bases pela rubrica e o holerite é recalculado na hora.</summary>
    public async Task<Result<PayslipDetail>> AddManualLineAsync(Guid runId, Guid payslipId, PayrollManualLineInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var target = await ManualTargetAsync(runId, payslipId, cancellationToken);
        if (target.IsFailure)
        {
            return target.Error!;
        }

        var (run, payslip) = target.Value;
        var item = input.PayrollItemId is { } itemId ? await db.PayrollItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId && i.IsActive, cancellationToken) : null;
        if (item is null || PayrollCalculator.HasRule(item.Calculation))
        {
            return PayrollRules.Field("payrollItemId", "Escolha uma rubrica ativa sem regra (prêmio, adiantamento, pensão, consignado, vale-transporte fornecido).");
        }

        if (!PayrollRules.ValidAmount(input.Amount))
        {
            return PayrollRules.Field("amount", "Informe o valor maior que zero, com até 2 casas.");
        }

        if (!PayrollRules.ValidReference(input.Reference))
        {
            return PayrollRules.Field("reference", "A referência aceita até 20 caracteres.");
        }

        var note = (input.Note ?? string.Empty).Trim();
        if (note.Length is < PayrollCalculationRules.NoteMin or > PayrollCalculationRules.NoteMax)
        {
            return PayrollRules.Field("note", "Informe o motivo do lançamento, de 5 a 200 caracteres.");
        }

        var sort = await db.PayslipLines.Where(l => l.PayslipId == payslip.Id).Select(l => (int?)l.Sort).MaxAsync(cancellationToken) ?? 0;
        var reference = input.Reference?.Trim();
        db.PayslipLines.Add(new PayslipLine
        {
            PayslipId = payslip.Id, PayrollItemId = item.Id, Code = item.Code, Description = item.Name, Type = item.Kind, Reference = string.IsNullOrEmpty(reference) ? null : reference,
            Amount = input.Amount, Sort = sort + 1, Calculated = false, Note = note,
        });
        await db.SaveChangesAsync(cancellationToken);
        return await RecalculateOneAsync(run, payslip.Id, cancellationToken);
    }

    /// <summary>Tirar o avulso (CA 13): só a linha avulsa do holerite da rota; o holerite é recalculado na hora.</summary>
    public async Task<Result<PayslipDetail>> RemoveManualLineAsync(Guid runId, Guid payslipId, Guid lineId, CancellationToken cancellationToken = default)
    {
        var target = await ManualTargetAsync(runId, payslipId, cancellationToken);
        if (target.IsFailure)
        {
            return target.Error!;
        }

        var line = await db.PayslipLines.FirstOrDefaultAsync(l => l.Id == lineId && l.PayslipId == payslipId, cancellationToken);
        if (line is null)
        {
            return Error.NotFound("payslip_line_not_found", "Lançamento não encontrado.");
        }

        if (line.Calculated)
        {
            return Error.Conflict(PayrollCalculationRules.CalculatedLineCode, PayrollCalculationRules.CalculatedLineMessage);
        }

        db.PayslipLines.Remove(line);
        await db.SaveChangesAsync(cancellationToken);
        return await RecalculateOneAsync(target.Value.Run, payslipId, cancellationToken);
    }

    /// <summary>
    /// Situação do cálculo (CA 1, 14 e 15), sem valor: simulação, tabelas a confirmar, quantos funcionários com cadastro ou tabela mudados
    /// depois do cálculo (a folha fechada nunca é recalculada: "Ver diferença" e "Gerar retificação") e os avisos por funcionário.
    /// </summary>
    public async Task<Result<PayrollCalculationStatus>> StatusAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
        {
            return RunNotFound();
        }

        var official = await parameters.CalculatedOfficialAsync(run.Type, run.Period, cancellationToken);
        var slips = await (from p in db.Payslips.AsNoTracking()
                           join e in db.Employees.IgnoreQueryFilters().AsNoTracking() on p.EmployeeId equals e.Id
                           where p.PayrollRunId == runId
                           select new { Payslip = p, e.Name }).ToListAsync(cancellationToken);
        var used = slips.SelectMany(s => s.Payslip.TaxTableIds).Distinct().ToList();
        var tables = await runs.TableRefsAsync(used, cancellationToken);
        var issues = slips.SelectMany(s => PayrollCalculator.ReadIssues(s.Payslip.CalculationIssues).Select(i => new PayrollCalcEmployeeIssue(s.Payslip.EmployeeId, s.Name, Capitalize(i.Message), i.Blocking)))
            .OrderByDescending(i => i.Blocking).ThenBy(i => i.EmployeeName, StringComparer.CurrentCulture).ToList();
        var (stale, reasons) = run.Mode == PayrollRunMode.Calculated && run.CalculatedAt is not null && run.RectifiesRunId is null ? await StaleAsync(run, cancellationToken) : (0, []);
        var rectifications = await db.PayrollRuns.AsNoTracking().Where(r => r.RectifiesRunId == run.Id).OrderBy(r => r.CreatedAt).Select(r => r.Id).ToListAsync(cancellationToken);
        PayrollNextStep? next = null;
        if (run.Mode == PayrollRunMode.Calculated && run.CalculatedAt is not null && run.Status == PayrollRunStatus.Draft)
        {
            next = official ? new PayrollNextStep("Fechar folha", PayrollClosingService.RunLink(run.Id, "fechamento")) : await CompareStepAsync(run, cancellationToken);
        }

        return new PayrollCalculationStatus(run.Id, run.CalculatedAt is not null, run.CalculatingSince is { } since && clock.GetUtcNow() - since < CalculatingTimeout, run.CalculatingSince,
            run.Mode == PayrollRunMode.Calculated && !official, official, run.CalculatorVersion, run.CalculatedAt, tables,
            tables.Where(t => t.Status != PayrollTaxTableStatus.Active).Select(t => t.Label).ToList(), stale, stale > 0 ? PayrollCalculationRules.StaleMessage(stale) : null, reasons, issues,
            run.RectifiesRunId, rectifications, next);
    }

    /// <summary>
    /// "Ver diferença" (CA 14): o cálculo de hoje (cadastro, tabelas e parâmetros atuais) contra o gravado, por funcionário e rubrica, sem
    /// gravar nada. Leitura sensível auditada.
    /// </summary>
    public async Task<Result<PayrollRunDifference>> DifferenceAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var run = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
        {
            return RunNotFound();
        }

        if (run.Mode != PayrollRunMode.Calculated || run.CalculatedAt is null || run.RectifiesRunId is not null)
        {
            return Error.Validation(PayrollCalculationRules.NotCalculatedCode, "A diferença compara uma folha calculada com o cálculo de hoje.");
        }

        if (ThirteenthCalculator.IsThirteenth(run.Type))
        {
            return ThirteenthNotRectifiable();
        }

        var context = await ContextAsync(run, cancellationToken);
        if (context.IsFailure)
        {
            return context.Error!;
        }

        var diff = await DiffAsync(run, context.Value, cancellationToken);
        var (_, reasons) = await StaleAsync(run, cancellationToken);
        await audit.RecordReadAsync(nameof(PayrollRun) + ".Difference", runId.ToString(), cancellationToken);
        return new PayrollRunDifference(run.Id, diff.Employees, diff.Employees.Sum(e => e.NetBefore), diff.Employees.Sum(e => e.NetAfter), reasons);
    }

    /// <summary>
    /// "Gerar retificação" (CA 14, ADR 0039 §3): só da folha calculada fechada; nasce uma folha complementar <c>Draft</c> ligada à fechada,
    /// só com as diferenças por funcionário e rubrica, que segue o fechamento normal (antes da virada, a conferência da FLH-06). A fechada
    /// não muda.
    /// </summary>
    public async Task<Result<PayrollRunSummary>> RectifyAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var original = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (original is null)
        {
            return RunNotFound();
        }

        if (original.Mode != PayrollRunMode.Calculated || original.RectifiesRunId is not null || original.CalculatedAt is null)
        {
            return Error.Validation(PayrollCalculationRules.NotCalculatedCode, "Só a folha calculada é retificada pelo cálculo.");
        }

        if (ThirteenthCalculator.IsThirteenth(original.Type))
        {
            return ThirteenthNotRectifiable();
        }

        if (original.Status == PayrollRunStatus.Draft)
        {
            return Error.Conflict(PayrollCalculationRules.NotClosedCode, "A folha em rascunho não precisa de retificação: clique em \"Recalcular\".");
        }

        if (await db.PayrollRuns.AnyAsync(r => r.RectifiesRunId == original.Id && r.Status == PayrollRunStatus.Draft, cancellationToken))
        {
            return Error.Conflict(PayrollCalculationRules.RectificationOpenCode, "Já existe uma retificação desta folha em rascunho. Feche ou recalcule a retificação.");
        }

        var context = await ContextAsync(original, cancellationToken);
        if (context.IsFailure)
        {
            return context.Error!;
        }

        var number = await db.PayrollRuns.CountAsync(r => r.RectifiesRunId == original.Id, cancellationToken) + 1;
        var now = clock.GetUtcNow();
        var rectification = new PayrollRun
        {
            Period = original.Period, Type = original.Type, Mode = PayrollRunMode.Calculated, RectifiesRunId = original.Id, Number = number, CreatedAt = now, CreatedBy = currentUser.UserId,
        };
        db.PayrollRuns.Add(rectification);
        var built = await BuildRectificationAsync(rectification, original, context.Value, PayrollCalculationAction.RectificationCreated, cancellationToken);
        if (built.IsFailure)
        {
            db.ChangeTracker.Clear();
            return built.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        return (await runs.GetAsync(rectification.Id, cancellationToken)).Value;
    }

    /// <summary>Histórico do cálculo (CA 19), sem valor: quem, quando, o quê, a versão do motor, as tabelas e os parâmetros.</summary>
    public async Task<Result<IReadOnlyList<PayrollCalculationEntry>>> HistoryAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (!await db.PayrollRuns.AnyAsync(r => r.Id == runId, cancellationToken))
        {
            return RunNotFound();
        }

        var logs = await db.PayrollCalculationLogs.AsNoTracking().Where(l => l.PayrollRunId == runId).OrderByDescending(l => l.At).ToListAsync(cancellationToken);
        var tables = (await runs.TableRefsAsync(logs.SelectMany(l => l.TaxTableIds).Distinct().ToList(), cancellationToken)).ToDictionary(t => t.Id);
        var parameterIds = logs.SelectMany(l => l.ParameterIds).Distinct().ToList();
        var keys = await db.HrParameters.AsNoTracking().Where(p => parameterIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Key, cancellationToken);
        var names = new Dictionary<Guid, string?>();
        foreach (var by in logs.Select(l => l.By).OfType<Guid>().Distinct())
        {
            names[by] = await users.DisplayNameAsync(by, cancellationToken);
        }

        return logs.Select(l => new PayrollCalculationEntry(l.Id, l.Action, ActionLabel(l.Action), l.At, l.By is { } by ? names.GetValueOrDefault(by) : null, l.CalculatorVersion,
            l.TaxTableIds.Where(tables.ContainsKey).Select(id => tables[id]).ToList(), l.ParameterIds.Where(keys.ContainsKey).Select(id => keys[id]).ToList(), l.Employees)).ToList();
    }

    /// <summary>Os totais antes e depois do histórico (CA 19), só com <c>hr.view_sensitive</c>; a leitura vai para a auditoria.</summary>
    public async Task<Result<IReadOnlyList<PayrollCalculationTotals>>> HistoryTotalsAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (!await db.PayrollRuns.AnyAsync(r => r.Id == runId, cancellationToken))
        {
            return RunNotFound();
        }

        var totals = await db.PayrollCalculationLogs.AsNoTracking().Where(l => l.PayrollRunId == runId).OrderByDescending(l => l.At)
            .Select(l => new PayrollCalculationTotals(l.Id, l.GrossBefore, l.NetBefore, l.GrossAfter, l.NetAfter)).ToListAsync(cancellationToken);
        await audit.RecordReadAsync(nameof(PayrollRun) + ".CalculationTotals", runId.ToString(), cancellationToken);
        return totals;
    }

    /// <summary>
    /// Fechamento da folha calculada (CA 15 e 16): antes da virada (FLH-06), 409 "Folha calculada em conferência paralela: feche a folha
    /// lançada." com "Comparar com a lançada"; depois, a folha precisa estar calculada e atualizada, e toda tabela usada conferida (422 com a
    /// lista). Null quando pode fechar (ou não é calculada).
    /// </summary>
    public async Task<Error?> CloseErrorAsync(PayrollRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Mode != PayrollRunMode.Calculated)
        {
            return null;
        }

        if (!await parameters.CalculatedOfficialAsync(run.Type, run.Period, cancellationToken))
        {
            var step = await CompareStepAsync(run, cancellationToken);
            return Error.Conflict(PayrollCalculationRules.SimulationCode, ThirteenthCalculator.IsThirteenth(run.Type) ? ThirteenthCalculator.SimulationMessage : PayrollCalculationRules.SimulationMessage) with
            {
                Data = new Dictionary<string, object?> { ["nextStep"] = step.Label, ["link"] = step.Link },
            };
        }

        if (run.CalculatedAt is null)
        {
            return Error.Validation(PayrollCalculationRules.NotCalculatedCode, "Calcule a folha antes de fechar.");
        }

        var used = await db.Payslips.AsNoTracking().Where(p => p.PayrollRunId == run.Id).Select(p => p.TaxTableIds).ToListAsync(cancellationToken);
        var refs = await runs.TableRefsAsync(used.SelectMany(u => u).Distinct().ToList(), cancellationToken);
        var pending = refs.Where(t => t.Status != PayrollTaxTableStatus.Active).ToList();
        if (pending.Count > 0)
        {
            return Error.Validation(PayrollCalculationRules.UnconfirmedTablesCode, PayrollCalculationRules.UnconfirmedTablesMessage(pending.Select(p => p.Label))) with
            {
                Data = new Dictionary<string, object?> { ["tables"] = pending.Select(p => p.Id).ToList(), ["link"] = PayrollTaxTableService.Link },
            };
        }

        if (run.RectifiesRunId is null && (await StaleAsync(run, cancellationToken)).Count is > 0 and var stale)
        {
            return Error.Validation(PayrollCalculationRules.NotCalculatedCode, PayrollCalculationRules.StaleMessage(stale) + " Recalcule antes de fechar.");
        }

        return null;
    }

    /// <summary>Os funcionários com erro que trava o cálculo ("Sem jornada"), como pendência do fechamento.</summary>
    public async Task<IReadOnlyList<PayrollCloseIssue>> CloseBlockersAsync(PayrollRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Mode != PayrollRunMode.Calculated)
        {
            return [];
        }

        var list = new List<PayrollCloseIssue>();
        if (await CloseErrorAsync(run, cancellationToken) is { } error)
        {
            list.Add(new PayrollCloseIssue(error.Code, error.Message, error.Data?.GetValueOrDefault("link") as string ?? PayrollClosingService.RunLink(run.Id, "holerites")));
        }

        var status = await StatusAsync(run.Id, cancellationToken);
        if (status.IsSuccess)
        {
            list.AddRange(status.Value.Issues.Where(i => i.Blocking).Select(i => new PayrollCloseIssue("payroll_calculation_issue", $"{i.EmployeeName}: {i.Message}", PayrollClosingService.RunLink(run.Id, "holerites"))));
        }

        return list;
    }

    private static Error? Precondition(PayrollRun run)
    {
        if (run.Mode != PayrollRunMode.Calculated)
        {
            return Error.Validation(PayrollCalculationRules.NotCalculatedCode, "Esta folha é lançada: os valores vêm da contadora. Crie a folha no modo \"Calculada\" para calcular.");
        }

        if (run.Status != PayrollRunStatus.Draft)
        {
            return Error.Conflict(PayrollRules.ClosedCode, $"A folha de {PayrollRules.PeriodLabel(run.Period)} está fechada: nada é recalculado. Use \"Ver diferença\" e \"Gerar retificação\".");
        }

        return run.Type != PayrollRunType.Monthly && !ThirteenthCalculator.IsThirteenth(run.Type)
            ? Error.Validation(PayrollCalculationRules.NotSupportedCode, $"O cálculo da folha {PayrollRules.TypeLabel(run.Type)} chega nas próximas etapas. Use a folha lançada.")
            : null;
    }

    /// <summary>CA 1: sem o espelho da competência fechado, 422 "Feche o ponto de {mês} antes de calcular." com o link.</summary>
    private async Task<Error?> TimesheetErrorAsync(PayrollRun run, CancellationToken ct) =>
        await db.Timesheets.AsNoTracking().AnyAsync(t => t.Period == run.Period && t.Status == TimesheetStatus.Closed, ct)
            ? null
            : Error.Validation(PayrollCalculationRules.TimesheetOpenCode, PayrollCalculationRules.TimesheetOpenMessage(run.Period)) with
            {
                Data = new Dictionary<string, object?> { ["link"] = "/app/rh/ponto/espelho" },
            };

    /// <summary>As tabelas da competência (FLH-01 CA 8), as rubricas (as quatro essenciais com regra) e os parâmetros.</summary>
    private async Task<Result<Context>> ContextAsync(PayrollRun run, CancellationToken ct)
    {
        var tables = new List<PayrollCalcTable>();
        foreach (var kind in Enum.GetValues<PayrollTaxTableKind>())
        {
            var found = await taxTables.ForCompetenceAsync(kind, run.Period, ct);
            if (found.IsFailure)
            {
                return found.Error!;
            }

            var t = found.Value;
            tables.Add(new PayrollCalcTable(t.Id, t.Kind, t.ValidFrom, t.Status, t.Rounding, t.Content));
        }

        var all = await db.PayrollItems.AsNoTracking().OrderBy(i => i.SeedKey == null).ThenBy(i => i.Code).ToListAsync(ct);
        var items = all.Select(i => new PayrollCalcItem(i.Id, i.Code, i.Name, i.Kind, i.IsActive ? i.Calculation : PayrollCalculation.None, i.InssBase, i.FgtsBase, i.IrrfBase, i.DeductsIrrfBase)).ToList();
        if (Required.FirstOrDefault(r => items.All(i => i.Calculation != r)) is var missing && missing != PayrollCalculation.None)
        {
            return Error.Validation(PayrollCalculationRules.ItemMissingCode, $"Falta a rubrica com a regra {PayrollCalculator.Label(missing)}: confira em RH → Rubricas.") with
            {
                Data = new Dictionary<string, object?> { ["link"] = "/app/rh/rubricas" },
            };
        }

        // FLH-02b (Estados): sem os percentuais das horas extras, nada é calculado (o link leva aos parâmetros do RH).
        if (await parameters.OvertimeRatesMissingAsync(ct))
        {
            return Error.Validation(PayrollCalculationRules.OvertimeRatesMissingCode, PayrollCalculationRules.OvertimeRatesMissingMessage) with
            {
                Data = new Dictionary<string, object?> { ["link"] = "/app/configuracoes/parametros?secao=rh" },
            };
        }

        var (prm, ids) = await parameters.CalculationParametersAsync(ct);
        return new Context(tables, items, prm, ids, await parameters.CalculatedOfficialAsync(run.Type, run.Period, ct));
    }

    /// <summary>Funcionário que entrou na competência depois de a folha nascer ganha o holerite no recálculo.</summary>
    private async Task AddEligibleAsync(PayrollRun run, CancellationToken ct)
    {
        var end = PayrollRules.PeriodEnd(run.Period);
        var have = await db.Payslips.Where(p => p.PayrollRunId == run.Id).Select(p => p.EmployeeId).ToListAsync(ct);
        var missing = await db.Employees.AsNoTracking().Where(e => e.AdmissionDate <= end && (e.TerminationDate == null || e.TerminationDate >= run.Period) && !have.Contains(e.Id))
            .Select(e => e.Id).ToListAsync(ct);
        var now = clock.GetUtcNow();
        foreach (var id in missing)
        {
            db.Payslips.Add(new Payslip { PayrollRunId = run.Id, EmployeeId = id, CreatedAt = now, UpdatedAt = now });
        }

        if (missing.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Retrato das entradas (ADR 0039 §2) por funcionário: salário vigente no fim da competência, cargo, dependentes, afastamentos, espelho
    /// fechado, avulsos e parâmetros, em ordem estável (o mesmo cadastro dá o mesmo retrato).
    /// </summary>
    private async Task<Dictionary<Guid, PayrollCalcSnapshot>> SnapshotsAsync(Guid runId, DateOnly period, IReadOnlyCollection<Guid> employeeIds, IReadOnlyDictionary<Guid, List<PayrollCalcManualLine>> manual,
        PayrollCalcParameters prm, CancellationToken ct)
    {
        var end = PayrollRules.PeriodEnd(period);
        var ids = employeeIds.ToList();
        var employees = await db.Employees.IgnoreQueryFilters().AsNoTracking().Where(e => ids.Contains(e.Id)).ToListAsync(ct);
        var salaries = await runs.SalariesAtAsync(employees.ToDictionary(e => e.Id, e => e.BaseSalary), end, ct);
        var positionIds = employees.Select(e => e.PositionId).Distinct().ToList();
        var positions = await db.JobPositions.IgnoreQueryFilters().AsNoTracking().Where(p => positionIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.MonthlyHours, ct);
        var dependents = (await db.EmployeeDependents.AsNoTracking().Where(d => ids.Contains(d.EmployeeId)).ToListAsync(ct)).ToLookup(d => d.EmployeeId);
        var leaves = (await db.EmployeeHistory.AsNoTracking()
            .Where(h => ids.Contains(h.EmployeeId) && h.Kind == EmployeeHistoryKind.Leave && h.CancelledAt == null && h.EffectiveDate <= end && (h.LeaveEndDate == null || h.LeaveEndDate >= period))
            .ToListAsync(ct)).ToLookup(h => h.EmployeeId);
        var timesheets = (await db.Timesheets.AsNoTracking().Where(t => ids.Contains(t.EmployeeId) && t.Period == period && t.Status == TimesheetStatus.Closed).ToListAsync(ct))
            .GroupBy(t => t.EmployeeId).ToDictionary(g => g.Key, g => g.First());

        // FLH-02b: as horas do espelho fechado (dias com extra, noturno ou falta injustificada), o calendário padrão da competência e o
        // banco de horas com a decisão do RH nesta folha.
        var decisions = await db.Payslips.AsNoTracking().Where(p => p.PayrollRunId == runId && ids.Contains(p.EmployeeId))
            .Select(p => new { p.EmployeeId, p.HourBankDecisionMinutes, p.HourBankDecisionNote }).ToDictionaryAsync(p => p.EmployeeId, ct);
        var holidays = (await calendar.ExceptionsAsync(period, end, ct)).Where(x => x.HoursAvailable <= 0m).Select(x => x.Date).Distinct().Order().ToList();
        var monthCalendar = new PayrollCalcMonthCalendar(holidays, await calendar.WorkingDaysAsync(period, end, ct));
        var result = new Dictionary<Guid, PayrollCalcSnapshot>();
        foreach (var e in employees)
        {
            var sheet = timesheets.GetValueOrDefault(e.Id);
            var days = sheet?.DaysSnapshot is { } json ? JsonSerializer.Deserialize<List<TimesheetDayResult>>(json, TimeClock.TimesheetService.SnapshotJson) ?? [] : [];
            var decision = decisions.GetValueOrDefault(e.Id);
            PayrollCalcHourBank? bank = sheet?.BankMonthMinutes is not null
                ? new PayrollCalcHourBank(sheet.BankBalanceMinutes ?? 0, decision?.HourBankDecisionMinutes ?? 0, decision?.HourBankDecisionNote)
                : null;
            result[e.Id] = new PayrollCalcSnapshot(
                salaries.GetValueOrDefault(e.Id), e.AdmissionDate, e.TerminationDate, positions.ContainsKey(e.PositionId), positions.GetValueOrDefault(e.PositionId),
                e.InsalubrityPct, e.HazardPay, e.UsesTransportVoucher,
                dependents[e.Id].OrderBy(d => d.BirthDate).ThenBy(d => d.IrDependent).ThenBy(d => d.FamilyAllowance).ThenBy(d => d.FamilyAllowanceDocsOk)
                    .Select(d => new PayrollCalcDependent(d.BirthDate, d.IrDependent, d.FamilyAllowance, d.FamilyAllowanceDocsOk)).ToList(),
                leaves[e.Id].OrderBy(l => l.EffectiveDate).ThenBy(l => l.LeaveEndDate).Select(l => new PayrollCalcLeave(l.EffectiveDate, l.LeaveEndDate ?? end, l.LeaveType)).ToList(),
                sheet is not null, sheet?.AbsenceMinutes ?? 0, sheet?.LateMinutes ?? 0, manual.GetValueOrDefault(e.Id) ?? [], prm,
                PayrollHours.FromTimesheet(days, prm.HourRules), monthCalendar, bank);
        }

        return result;
    }

    /// <summary>Os avulsos gravados de cada funcionário da folha (as linhas que não são do motor), na ordem do holerite.</summary>
    private async Task<Dictionary<Guid, List<PayrollCalcManualLine>>> ManualAsync(Guid runId, CancellationToken ct) =>
        (await (from l in db.PayslipLines.AsNoTracking()
                join p in db.Payslips.AsNoTracking() on l.PayslipId equals p.Id
                where p.PayrollRunId == runId && !l.Calculated
                orderby l.Sort
                select new { p.EmployeeId, l.PayrollItemId, l.Reference, l.Amount, l.Note }).ToListAsync(ct))
        .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.Select(x => new PayrollCalcManualLine(x.PayrollItemId, x.Reference, x.Amount, x.Note ?? string.Empty)).ToList());

    /// <summary>Calcula a folha inteira e grava (linhas, bases, guias calculadas, histórico); devolve quantos holerites.</summary>
    private async Task<Result<int>> RunAsync(PayrollRun run, Context context, Guid? by, CancellationToken ct)
    {
        if (run.RectifiesRunId is { } originalId)
        {
            var original = await db.PayrollRuns.AsNoTracking().FirstAsync(r => r.Id == originalId, ct);
            var rebuilt = await BuildRectificationAsync(run, original, context, PayrollCalculationAction.Recalculated, ct, by);
            if (rebuilt.IsFailure)
            {
                return rebuilt.Error!;
            }

            await db.SaveChangesAsync(ct);
            return rebuilt.Value;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var payslips = await db.Payslips.Where(p => p.PayrollRunId == run.Id).ToListAsync(ct);
        (decimal Gross, decimal Net)? before = run.CalculatedAt is null ? null : (payslips.Sum(p => p.Gross), payslips.Sum(p => p.Net));
        if (ThirteenthCalculator.IsThirteenth(run.Type))
        {
            payslips = await ApplyThirteenthAsync(run, context, payslips, ct);
        }
        else
        {
            await ApplyAsync(run, context, payslips, ct);
        }

        await db.SaveChangesAsync(ct);
        await ChargesAsync(run, payslips, ct);
        var now = clock.GetUtcNow();
        db.PayrollCalculationLogs.Add(new PayrollCalculationLog
        {
            PayrollRunId = run.Id, Action = run.CalculatedAt is null ? PayrollCalculationAction.Calculated : PayrollCalculationAction.Recalculated, At = now, By = by,
            CalculatorVersion = VersionOf(run), TaxTableIds = context.Tables.OrderBy(t => t.Kind).Select(t => t.Id).ToList(), ParameterIds = context.ParameterIds,
            Employees = payslips.Count, GrossBefore = before?.Gross, NetBefore = before?.Net, GrossAfter = payslips.Sum(p => p.Gross), NetAfter = payslips.Sum(p => p.Net),
        });
        run.CalculatedAt = now;
        run.CalculatedBy = by;
        run.CalculatorVersion = VersionOf(run);
        run.IsSimulation = !context.Official;
        run.CalculatingSince = null;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return payslips.Count;
    }

    /// <summary>Recalcula um holerite (depois do avulso), refaz as guias calculadas e devolve o holerite.</summary>
    private async Task<Result<PayslipDetail>> RecalculateOneAsync(PayrollRun run, Guid payslipId, CancellationToken ct)
    {
        var context = await ContextAsync(run, ct);
        if (context.IsFailure)
        {
            return context.Error!;
        }

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var payslip = await db.Payslips.FirstAsync(p => p.Id == payslipId, ct);
            await ApplyAsync(run, context.Value, [payslip], ct);
            await db.SaveChangesAsync(ct);
            var all = await db.Payslips.Where(p => p.PayrollRunId == run.Id).ToListAsync(ct);
            await ChargesAsync(run, all, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        return await runs.PayslipAsync(run.Id, payslipId, ct);
    }

    /// <summary>Roda o motor nos holerites e troca as linhas (as avulsas voltam do retrato) e os campos do cálculo.</summary>
    private async Task ApplyAsync(PayrollRun run, Context context, IReadOnlyList<Payslip> payslips, CancellationToken ct)
    {
        var manual = await ManualAsync(run.Id, ct);
        var snapshots = await SnapshotsAsync(run.Id, run.Period, payslips.Select(p => p.EmployeeId).ToList(), manual, context.Parameters, ct);
        var ids = payslips.Select(p => p.Id).ToList();
        var old = await db.PayslipLines.Where(l => ids.Contains(l.PayslipId)).ToListAsync(ct);
        db.PayslipLines.RemoveRange(old);
        var now = clock.GetUtcNow();
        foreach (var payslip in payslips)
        {
            var snapshot = snapshots[payslip.EmployeeId];
            var result = PayrollCalculator.Calculate(new PayrollCalcInput(run.Period, snapshot, context.Items, context.Tables));
            db.PayslipLines.AddRange(result.Lines.Select((l, i) => ToLine(payslip.Id, l, i)));
            Store(payslip, result, snapshot, context, now);
        }
    }

    private static PayslipLine ToLine(Guid payslipId, PayrollCalcLine l, int sort) => new()
    {
        PayslipId = payslipId, PayrollItemId = l.ItemId, Code = l.Code, Description = l.Name, Type = l.Type, Reference = l.Reference, Amount = l.Amount, Sort = sort,
        Calculated = l.Calculated, Memory = PayrollCalculator.WriteMemory(l.Memory), Note = l.Note,
    };

    private static void Store(Payslip payslip, PayrollCalcResult result, PayrollCalcSnapshot snapshot, Context context, DateTimeOffset now)
    {
        payslip.ApplyTotals(result.Totals);
        payslip.InssBase = result.InssBase;
        payslip.IrrfBase = result.IrrfBase;
        payslip.FgtsBase = result.FgtsBase;
        payslip.FgtsAmount = result.FgtsAmount;
        payslip.EmployerCharges = result.EmployerCharges;
        payslip.EmployerChargesMemory = PayrollCalculator.WriteMemory(result.EmployerMemory);
        payslip.CalculationIssues = PayrollCalculator.WriteIssues(result.Issues);
        payslip.InputsSnapshot = PayrollCalculator.WriteSnapshot(snapshot);
        payslip.TaxTableIds = [.. result.TableIds];
        payslip.ParameterIds = [.. context.ParameterIds];
        payslip.CalculatorVersion = PayrollCalculator.Version;
        payslip.CalculatedAt = now;
        payslip.UpdatedAt = now;
        payslip.PdfAttachmentId = null;
    }

    /// <summary>
    /// Guias calculadas (CA 10): FGTS (soma do FGTS do mês), INSS (o retido + os encargos da empresa da tabela) e IRRF retido, com o
    /// vencimento e o órgão padrão; refeitas a cada cálculo. As guias lançadas à mão (sindical, outros) ficam.
    /// </summary>
    private async Task ChargesAsync(PayrollRun run, IReadOnlyList<Payslip> payslips, CancellationToken ct)
    {
        // As linhas já gravadas (o chamador grava antes, na mesma transação).
        var ids = payslips.Select(p => p.Id).ToList();
        var lines = await db.PayslipLines.AsNoTracking().Where(l => ids.Contains(l.PayslipId) && l.Calculated).Select(l => new { l.PayrollItemId, l.Amount }).ToListAsync(ct);
        var items = await db.PayrollItems.AsNoTracking().Where(i => i.Calculation == PayrollCalculation.Inss || i.Calculation == PayrollCalculation.Irrf).ToDictionaryAsync(i => i.Id, i => i.Calculation, ct);
        decimal Sum(PayrollCalculation calc) => lines.Where(l => items.GetValueOrDefault(l.PayrollItemId) == calc).Sum(l => l.Amount);
        await ReplaceChargesAsync(run, payslips.Sum(p => p.FgtsAmount), Sum(PayrollCalculation.Inss) + payslips.Sum(p => p.EmployerCharges), Sum(PayrollCalculation.Irrf), ct);
    }

    private async Task ReplaceChargesAsync(PayrollRun run, decimal fgts, decimal inss, decimal irrf, CancellationToken ct)
    {
        var old = await db.PayrollRunCharges.Where(c => c.PayrollRunId == run.Id && c.Calculated).ToListAsync(ct);
        var keep = old.ToDictionary(c => c.Kind);
        db.PayrollRunCharges.RemoveRange(old);
        var defaults = (await parameters.ChargeDefaultsAsync(run.Period, ct)).ToDictionary(d => d.Kind);
        var now = clock.GetUtcNow();
        foreach (var (kind, amount, label) in new[] { (PayrollChargeKind.Fgts, fgts, "FGTS"), (PayrollChargeKind.Inss, inss, "INSS (empregados e empresa)"), (PayrollChargeKind.Irrf, irrf, "IRRF retido") })
        {
            if (amount <= 0m)
            {
                continue;
            }

            var d = defaults[kind];
            var previous = keep.GetValueOrDefault(kind);
            db.PayrollRunCharges.Add(new PayrollRunCharge
            {
                PayrollRunId = run.Id, Kind = kind, Description = $"{label} {PayrollRules.PeriodLabel(run.Period)}" + (run.RectifiesRunId is null ? string.Empty : " (retificação)"),
                Amount = amount, DueDate = previous?.DueDate ?? d.DueDate ?? PayrollRules.DueDateNextMonth(run.Period, HrParametersService.DefaultDueDay),
                PayeePartyId = previous?.PayeePartyId ?? d.PayeePartyId, CreatedAt = now, Calculated = true,
            });
        }
    }

    private sealed record Diff(List<PayrollDifferenceEmployee> Employees, Dictionary<Guid, (PayrollCalcResult Result, PayrollCalcSnapshot Snapshot)> Results);

    /// <summary>O cálculo de hoje contra o gravado, por funcionário e rubrica (sem gravar).</summary>
    private async Task<Diff> DiffAsync(PayrollRun run, Context context, CancellationToken ct)
    {
        var payslips = await (from p in db.Payslips.AsNoTracking()
                              join e in db.Employees.IgnoreQueryFilters().AsNoTracking() on p.EmployeeId equals e.Id
                              where p.PayrollRunId == run.Id
                              orderby e.Name
                              select new { Payslip = p, e.Name, e.Registration }).ToListAsync(ct);
        var manual = await ManualAsync(run.Id, ct);
        var snapshots = await SnapshotsAsync(run.Id, run.Period, payslips.Select(p => p.Payslip.EmployeeId).ToList(), manual, context.Parameters, ct);
        var ids = payslips.Select(p => p.Payslip.Id).ToList();
        var stored = (await db.PayslipLines.AsNoTracking().Where(l => ids.Contains(l.PayslipId)).ToListAsync(ct)).ToLookup(l => l.PayslipId);
        var items = context.Items.ToDictionary(i => i.Id);
        var employees = new List<PayrollDifferenceEmployee>();
        var results = new Dictionary<Guid, (PayrollCalcResult, PayrollCalcSnapshot)>();
        foreach (var slip in payslips)
        {
            var snapshot = snapshots[slip.Payslip.EmployeeId];
            var result = PayrollCalculator.Calculate(new PayrollCalcInput(run.Period, snapshot, context.Items, context.Tables));
            results[slip.Payslip.Id] = (result, snapshot);
            var before = stored[slip.Payslip.Id].GroupBy(l => l.PayrollItemId).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));
            var after = result.Lines.GroupBy(l => l.ItemId).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));
            var lines = before.Keys.Union(after.Keys).Where(id => before.GetValueOrDefault(id) != after.GetValueOrDefault(id)).Select(id =>
            {
                var item = items.GetValueOrDefault(id);
                var line = stored[slip.Payslip.Id].FirstOrDefault(l => l.PayrollItemId == id);
                return new PayrollDifferenceLine(id, item?.Code ?? line?.Code ?? string.Empty, item?.Name ?? line?.Description ?? string.Empty, item?.Kind ?? line?.Type ?? PayrollItemKind.Information,
                    before.GetValueOrDefault(id), after.GetValueOrDefault(id));
            }).OrderBy(l => l.Code, StringComparer.Ordinal).ToList();
            if (lines.Count > 0 || slip.Payslip.Net != result.Totals.Net)
            {
                employees.Add(new PayrollDifferenceEmployee(slip.Payslip.EmployeeId, slip.Name, slip.Registration, slip.Payslip.Net, result.Totals.Net, lines));
            }
        }

        return new Diff(employees, results);
    }

    /// <summary>
    /// Monta (ou refaz) a retificação: um holerite por funcionário com diferença, só com as diferenças por rubrica (o provento que diminuiu
    /// vira desconto), as bases e o FGTS da diferença, e as guias da diferença a maior (a diferença a menor é compensada pela contadora).
    /// </summary>
    private async Task<Result<int>> BuildRectificationAsync(PayrollRun rectification, PayrollRun original, Context context, PayrollCalculationAction action, CancellationToken ct, Guid? by = null)
    {
        var diff = await DiffAsync(original, context, ct);
        if (diff.Employees.Count == 0)
        {
            return Error.Validation(PayrollCalculationRules.NothingToRectifyCode, "Nada mudou: a folha fechada bate com o cálculo de hoje.");
        }

        var oldSlips = await db.Payslips.Where(p => p.PayrollRunId == rectification.Id).ToListAsync(ct);
        (decimal Gross, decimal Net)? before = oldSlips.Count == 0 ? null : (oldSlips.Sum(p => p.Gross), oldSlips.Sum(p => p.Net));
        var oldIds = oldSlips.Select(p => p.Id).ToList();
        db.PayslipLines.RemoveRange(await db.PayslipLines.Where(l => oldIds.Contains(l.PayslipId)).ToListAsync(ct));
        db.Payslips.RemoveRange(oldSlips);
        var originals = await db.Payslips.AsNoTracking().Where(p => p.PayrollRunId == original.Id).ToDictionaryAsync(p => p.EmployeeId, ct);
        var items = context.Items.ToDictionary(i => i.Id);
        var now = clock.GetUtcNow();
        var created = new List<Payslip>();
        decimal inss = 0m, irrf = 0m;
        foreach (var employee in diff.Employees)
        {
            var source = originals[employee.EmployeeId];
            var (result, snapshot) = diff.Results[source.Id];
            var payslip = new Payslip { PayrollRunId = rectification.Id, EmployeeId = employee.EmployeeId, CreatedAt = now, UpdatedAt = now };
            var lines = new List<PayslipLine>();
            foreach (var line in employee.Lines)
            {
                if (PayrollCalculationRules.Difference(line.Kind, line.Before, line.After) is not { } d)
                {
                    continue;
                }

                var name = line.Name + (d.Reduced ? " (diferença a menor)" : " (diferença)");
                lines.Add(new PayslipLine
                {
                    PayslipId = payslip.Id, PayrollItemId = line.PayrollItemId, Code = line.Code, Description = name.Length > PayrollRules.NameMaxLength ? name[..PayrollRules.NameMaxLength] : name,
                    Type = d.Type, Amount = d.Amount, Sort = lines.Count, Calculated = true,
                    Memory = PayrollCalculator.WriteMemory([$"Retificação da folha fechada: antes {PayrollCalculator.Brl(line.Before)}, pelo cálculo de hoje {PayrollCalculator.Brl(line.After)}."]),
                });
                var calc = items.GetValueOrDefault(line.PayrollItemId)?.Calculation;
                inss += calc == PayrollCalculation.Inss ? line.After - line.Before : 0m;
                irrf += calc == PayrollCalculation.Irrf ? line.After - line.Before : 0m;
            }

            payslip.ApplyTotals(PayrollRules.Totals(lines.Select(l => new PayslipLineValue(l.Type, l.Amount, l.Reference))));
            payslip.InssBase = Math.Max(result.InssBase - source.InssBase, 0m);
            payslip.IrrfBase = Math.Max(result.IrrfBase - source.IrrfBase, 0m);
            payslip.FgtsBase = Math.Max(result.FgtsBase - source.FgtsBase, 0m);
            payslip.FgtsAmount = Math.Max(result.FgtsAmount - source.FgtsAmount, 0m);
            payslip.EmployerCharges = Math.Max(result.EmployerCharges - source.EmployerCharges, 0m);
            payslip.EmployerChargesMemory = PayrollCalculator.WriteMemory(result.EmployerMemory);
            payslip.CalculationIssues = PayrollCalculator.WriteIssues(result.Issues);
            payslip.InputsSnapshot = PayrollCalculator.WriteSnapshot(snapshot);
            payslip.TaxTableIds = [.. result.TableIds];
            payslip.ParameterIds = [.. context.ParameterIds];
            payslip.CalculatorVersion = PayrollCalculator.Version;
            payslip.CalculatedAt = now;
            db.Payslips.Add(payslip);
            db.PayslipLines.AddRange(lines);
            created.Add(payslip);
        }

        await ReplaceChargesAsync(rectification, created.Sum(p => p.FgtsAmount), Math.Max(inss, 0m) + created.Sum(p => p.EmployerCharges), Math.Max(irrf, 0m), ct);
        db.PayrollCalculationLogs.Add(new PayrollCalculationLog
        {
            PayrollRunId = rectification.Id, Action = action, At = now, By = by ?? currentUser.UserId, CalculatorVersion = PayrollCalculator.Version,
            TaxTableIds = context.Tables.OrderBy(t => t.Kind).Select(t => t.Id).ToList(), ParameterIds = context.ParameterIds, Employees = created.Count,
            GrossBefore = before?.Gross, NetBefore = before?.Net, GrossAfter = created.Sum(p => p.Gross), NetAfter = created.Sum(p => p.Net),
        });
        rectification.CalculatedAt = now;
        rectification.CalculatedBy = by ?? currentUser.UserId;
        rectification.CalculatorVersion = PayrollCalculator.Version;
        rectification.IsSimulation = !context.Official;
        rectification.CalculatingSince = null;
        return created.Count;
    }

    /// <summary>
    /// Quantos funcionários têm o cadastro, um parâmetro ou uma tabela mudados depois do cálculo (FLH-01 CA 7 e 13; FLH-02a CA 14), e por
    /// quê: "Tabela alterada depois do cálculo" (a versão a confirmar usada foi editada), "Tabela retificada depois do cálculo" (a usada foi
    /// substituída ou a competência agora usa outra versão) e "Cadastro ou parâmetro mudou depois do cálculo" (o retrato de hoje é outro).
    /// </summary>
    private async Task<(int Count, IReadOnlyList<string> Reasons)> StaleAsync(PayrollRun run, CancellationToken ct)
    {
        var payslips = await db.Payslips.AsNoTracking().Where(p => p.PayrollRunId == run.Id && p.CalculatedAt != null).ToListAsync(ct);
        if (payslips.Count == 0)
        {
            return (0, []);
        }

        if (ThirteenthCalculator.IsThirteenth(run.Type))
        {
            return await ThirteenthStaleAsync(run, payslips, ct);
        }

        var current = new List<Guid>();
        foreach (var kind in Enum.GetValues<PayrollTaxTableKind>())
        {
            if ((await taxTables.ForCompetenceAsync(kind, run.Period, ct)) is { IsSuccess: true } found)
            {
                current.Add(found.Value.Id);
            }
        }

        var usedIds = payslips.SelectMany(p => p.TaxTableIds).Distinct().ToList();
        var used = await db.PayrollTaxTables.AsNoTracking().Where(t => usedIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var (prm, _) = await parameters.CalculationParametersAsync(ct);
        var snapshots = await SnapshotsAsync(run.Id, run.Period, payslips.Select(p => p.EmployeeId).ToList(), await ManualAsync(run.Id, ct), prm, ct);
        var reasons = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        foreach (var p in payslips)
        {
            var mine = new List<string>();
            if (p.TaxTableIds.Any(id => used.GetValueOrDefault(id) is { } t && t.EditedAt > p.CalculatedAt))
            {
                mine.Add("Tabela alterada depois do cálculo");
            }

            if (p.TaxTableIds.Any(id => used.GetValueOrDefault(id)?.Status == PayrollTaxTableStatus.Superseded) || !current.Order().SequenceEqual(p.TaxTableIds.Order()))
            {
                mine.Add("Tabela retificada depois do cálculo");
            }

            var stored = PayrollCalculator.ReadSnapshot(p.InputsSnapshot);
            var today = snapshots[p.EmployeeId];
            if (stored is null || PayrollCalculator.WriteSnapshot(stored with { Parameters = today.Parameters }) != PayrollCalculator.WriteSnapshot(today))
            {
                mine.Add("Cadastro ou parâmetro mudou depois do cálculo");
            }
            else if (PayrollCalculator.WriteSnapshot(stored) != PayrollCalculator.WriteSnapshot(today))
            {
                // FLH-02b CA 8: só um parâmetro (das horas ou da folha) mudou com settings.edit depois do cálculo.
                mine.Add("Parâmetro mudou depois do cálculo");
            }

            if (mine.Count > 0)
            {
                count++;
                reasons.UnionWith(mine);
            }
        }

        return (count, reasons.Order(StringComparer.Ordinal).ToList());
    }

    /// <summary>"Comparar com a lançada" (CA 15): a conferência paralela da competência (FLH-06), ou a lista das folhas sem a lançada.</summary>
    private async Task<PayrollNextStep> CompareStepAsync(PayrollRun run, CancellationToken ct)
    {
        var entered = await db.PayrollRuns.AsNoTracking().AnyAsync(r => r.Period == run.Period && r.Type == run.Type && r.Mode == PayrollRunMode.Entered && r.RectifiesRunId == null, ct);
        return new PayrollNextStep("Comparar com a lançada", entered ? PayrollParallelService.Link(run.Period) : "/app/rh/folha");
    }

    private async Task<Result<(PayrollRun Run, Payslip Payslip)>> ManualTargetAsync(Guid runId, Guid payslipId, CancellationToken ct)
    {
        if (!await CanSensitiveAsync(ct))
        {
            return Forbidden();
        }

        var run = await db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
        var payslip = run is null ? null : await db.Payslips.AsNoTracking().FirstOrDefaultAsync(p => p.Id == payslipId && p.PayrollRunId == runId, ct);
        if (run is null || payslip is null)
        {
            return Error.NotFound("payslip_not_found", "Holerite não encontrado.");
        }

        if (Precondition(run) is { } invalid)
        {
            return invalid;
        }

        return run.RectifiesRunId is not null || ThirteenthCalculator.IsThirteenth(run.Type)
            ? Error.Conflict(PayrollCalculationRules.CalculatedLineCode, run.RectifiesRunId is not null
                ? "Na retificação, o ajuste é feito no cadastro e no cálculo: clique em \"Recalcular\"."
                : "No 13º calculado, o ajuste é feito no cadastro, no ponto e nas folhas do ano: clique em \"Recalcular\".")
            : (run, payslip);
    }

    private static string ActionLabel(PayrollCalculationAction action) => action switch
    {
        PayrollCalculationAction.Calculated => "Calculou",
        PayrollCalculationAction.Recalculated => "Recalculou",
        _ => "Gerou a retificação",
    };

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.GetCultureInfo("pt-BR")) + text[1..];

    private ValueTask<bool> CanSensitiveAsync(CancellationToken ct) => permissions.HasPermissionAsync("hr", PositionService.SensitiveAction, ct);

    private static Error RunNotFound() => Error.NotFound("payroll_run_not_found", "Folha não encontrada.");

    private static Error Forbidden() => Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso aos valores da folha. Fale com o administrador.");
}

/// <summary>Argumentos do cálculo em segundo plano (CA 17): só ids, nunca valor nem dado pessoal.</summary>
public sealed record PayrollCalculationArgs(Guid PayrollRunId, Guid? RequestedBy);

/// <summary>Cálculo da folha acima de 20 funcionários (CA 17): roda no escopo do tenant e avisa quem pediu no sino.</summary>
public sealed class PayrollCalculationJob(ITenantDirectory directory, ITenantScopeFactory scopes, Microsoft.Extensions.Logging.ILogger<PayrollCalculationJob> logger)
    : TenantJob<PayrollCalculationArgs>(directory, scopes, logger)
{
    protected override Task RunAsync(IServiceProvider services, TenantInfo tenant, PayrollCalculationArgs args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(args);
        return Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<PayrollCalculationService>(services).CompleteAsync(args.PayrollRunId, args.RequestedBy, cancellationToken);
    }
}
