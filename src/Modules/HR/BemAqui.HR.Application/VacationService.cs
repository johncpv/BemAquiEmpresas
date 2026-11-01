using System.Text.Json;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Application.TimeClock;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Férias (FLH-03): os períodos aquisitivos refeitos pela <see cref="VacationPolicy"/> (admissão, faltas injustificadas dos espelhos fechados e
/// afastamentos, CA 1 e 2), a programação com a validação dos parâmetros pré-configurados pela CLT (CA 4), o espelho e o "Meu ponto" (CA 5),
/// o cancelamento (CA 11), o aviso diário de vencimento (CA 3) e a folha de férias calculada (CA 6 a 9, em <c>VacationService.Calculation</c>).
/// Nenhum valor sai daqui sem <c>hr.view_sensitive</c>: a programação, os períodos e o calendário não têm valor.
/// </summary>
public sealed partial class VacationService(
    IHrDb db,
    IPermissionChecker permissions,
    HrParametersService parameters,
    IPayrollTaxTables taxTables,
    PayrollService runs,
    Documents.PayslipDocumentService documents,
    IPdfRenderer renderer,
    IBusinessCalendar calendar,
    IEventPublisher events,
    INotificationService notifications,
    IUserDirectory users,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public const string NotificationType = "hr.vacation";
    public const int OverviewDays = 90;

    /// <summary>Aba "Férias" (CA 1): os períodos desde a admissão, as programações e a sugestão do próximo gozo.</summary>
    public async Task<Result<EmployeeVacations>> EmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var employee = await db.Employees.IgnoreQueryFilters().AsNoTracking().Where(e => e.Id == employeeId && !e.IsDeleted).Select(e => new { e.Id, e.Name }).FirstOrDefaultAsync(cancellationToken);
        if (employee is null)
        {
            return EmployeeNotFound();
        }

        await SyncAsync([employeeId], cancellationToken);
        var (prm, _) = await parameters.VacationParametersAsync(cancellationToken);
        var today = BusinessTime.Today(clock);
        var periods = await db.VacationPeriods.AsNoTracking().Where(p => p.EmployeeId == employeeId).OrderByDescending(p => p.Sequence).ToListAsync(cancellationToken);
        var schedules = await SchedulesAsync(s => s.EmployeeId == employeeId, cancellationToken);
        VacationSuggestionView? suggestion = null;
        if (periods.OrderBy(p => p.Sequence).FirstOrDefault(p => p.Status is VacationPeriodStatus.Available or VacationPeriodStatus.Expired && p.Balance > 0) is { } next)
        {
            var blocked = await RestOrHolidayAsync(today, today.AddDays(prm.NoticeDays + 120), cancellationToken);
            var s = VacationPolicy.Suggest(today, next.EndsOn, next.Balance, blocked, prm);
            var maxSold = Math.Max(next.EntitledDays / 3 - next.SoldDays, 0);
            suggestion = new VacationSuggestionView(next.Id, s.Start, s.Days, maxSold);
        }

        return new EmployeeVacations(employee.Id, employee.Name, periods.Select(p => PeriodItem(p, today)).ToList(), schedules, suggestion,
            new VacationRulesView(prm.MaxParts, prm.FirstPartMinDays, prm.OtherPartMinDays, prm.NoticeDays, prm.PaymentDaysBefore));
    }

    /// <summary>
    /// "Programar férias" (CA 4 e 5): <c>hr.edit</c> na rota; a validação recusa com 422 o que fere o fracionamento, o abono e o início nos 2
    /// dias antes de feriado ou repouso, com a mensagem e o valor do parâmetro. Programada: o espelho mostra "Férias", o evento sai pelo outbox
    /// (só ids e datas) e o próximo passo é "Gerar aviso de férias". A capacidade do planejamento não muda (ADR 0029).
    /// </summary>
    public async Task<Result<VacationScheduleResult>> ScheduleAsync(Guid employeeId, VacationScheduleInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var employee = await db.Employees.AsNoTracking().Where(e => e.Id == employeeId).Select(e => new { e.Id, e.TerminationDate }).FirstOrDefaultAsync(cancellationToken);
        if (employee is null)
        {
            return EmployeeNotFound();
        }

        await SyncAsync([employeeId], cancellationToken);
        var period = input.PeriodId is { } periodId ? await db.VacationPeriods.FirstOrDefaultAsync(p => p.Id == periodId && p.EmployeeId == employeeId, cancellationToken) : null;
        if (period is null)
        {
            return PayrollRules.Field("periodId", "Escolha o período aquisitivo.");
        }

        if (input.StartsOn is not { } start)
        {
            return PayrollRules.Field("startsOn", "Informe o início das férias.");
        }

        var (prm, _) = await parameters.VacationParametersAsync(cancellationToken);
        var today = BusinessTime.Today(clock);
        var active = await db.VacationSchedules.AsNoTracking().Where(s => s.EmployeeId == employeeId && s.Status != VacationScheduleStatus.Cancelled).ToListAsync(cancellationToken);
        var blocked = await RestOrHolidayAsync(start, start.AddDays(VacationPolicy.BlockedDaysBefore), cancellationToken);
        var check = VacationPolicy.Validate(new VacationPeriodView(period.Status, period.EndsOn, period.EntitledDays, period.Balance, period.ConcessionEndsOn),
            active.Where(s => s.PeriodId == period.Id).Select(Part).ToList(), active.Select(Part).ToList(), new VacationRequest(start, input.Days, input.SoldDays), blocked, today,
            employee.TerminationDate, prm);
        if (check.Error is { } message)
        {
            return PayrollRules.Field(check.Field ?? "days", message);
        }

        var schedule = new VacationSchedule
        {
            EmployeeId = employeeId, PeriodId = period.Id, StartsOn = start, Days = input.Days, SoldDays = input.SoldDays, Advance13th = input.Advance13th,
            PaymentDueOn = await calendar.PreviousBusinessDayAsync(VacationPolicy.PaymentDue(start, prm), cancellationToken), CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
        };
        db.VacationSchedules.Add(schedule);
        period.TakenDays += input.Days;
        period.SoldDays += input.SoldDays;
        period.Status = VacationPolicy.StatusOf(period.EndsOn, period.ConcessionEndsOn, period.RestartedReason is not null, period.Balance, today, employee.TerminationDate);
        period.UpdatedAt = clock.GetUtcNow();
        events.Publish(new EmployeeVacationScheduled(employeeId, schedule.StartsOn, schedule.EndsOn));
        await db.SaveChangesAsync(cancellationToken);
        var item = (await SchedulesAsync(s => s.Id == schedule.Id, cancellationToken))[0];
        return new VacationScheduleResult(item, check.Warnings,
        [
            new PayrollNextStep("Gerar aviso de férias", NoticeLink(schedule.Id)),
            new PayrollNextStep("Calcular férias", EmployeeLink(employeeId)),
        ], "Férias programadas.");
    }

    /// <summary>
    /// "Cancelar programação" (CA 11): só a programada ou paga sem folha de férias fechada (409 "Reabra a folha de férias antes."); a folha de
    /// férias calculada em rascunho sai junto. O saldo volta, os dias somem do espelho e o <c>EmployeeVacationCancelled</c> sai pelo outbox.
    /// </summary>
    public async Task<Result<VacationScheduleItem>> CancelAsync(Guid scheduleId, VacationCancelInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var reason = (input.Reason ?? string.Empty).Trim();
        if (reason.Length is < 5 or > 200)
        {
            return PayrollRules.Field("reason", "Informe o motivo do cancelamento, de 5 a 200 caracteres.");
        }

        var schedule = await db.VacationSchedules.FirstOrDefaultAsync(s => s.Id == scheduleId, cancellationToken);
        if (schedule is null)
        {
            return ScheduleNotFound();
        }

        if (schedule.Version != input.Version)
        {
            return Stale();
        }

        if (schedule.Status is VacationScheduleStatus.Cancelled or VacationScheduleStatus.Taken)
        {
            return Error.Conflict("vacation_schedule_not_cancellable", schedule.Status == VacationScheduleStatus.Cancelled ? "A programação já foi cancelada." : "Férias já gozadas não são canceladas.");
        }

        var run = schedule.PayrollRunId is { } runId ? await db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken) : null;
        if (run is not null && run.Status != PayrollRunStatus.Draft)
        {
            return Error.Conflict(VacationPolicy.RunClosedCode, VacationPolicy.RunClosedMessage) with
            {
                Data = new Dictionary<string, object?> { ["link"] = PayrollClosingService.RunLink(run.Id, "historico") },
            };
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (run is not null)
        {
            await RemoveDraftRunAsync(run, cancellationToken);
        }

        schedule.Status = VacationScheduleStatus.Cancelled;
        schedule.CancelledAt = clock.GetUtcNow();
        schedule.CancelledBy = currentUser.UserId;
        schedule.CancelReason = reason;
        schedule.PayrollRunId = null;
        schedule.CalculatedSplit = null;
        db.Entry(schedule).Property(s => s.Version).OriginalValue = input.Version;
        events.Publish(new EmployeeVacationCancelled(schedule.EmployeeId, schedule.StartsOn, schedule.EndsOn));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Stale();
        }

        await SyncAsync([schedule.EmployeeId], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await SchedulesAsync(s => s.Id == scheduleId, cancellationToken))[0];
    }

    /// <summary>RH → Férias (CA 3): os vencimentos dos próximos <paramref name="days"/> dias e os vencidos, e o calendário de 6 meses.</summary>
    public async Task<VacationOverview> OverviewAsync(int? days, CancellationToken cancellationToken = default)
    {
        var window = Math.Clamp(days ?? OverviewDays, 1, 366);
        var today = BusinessTime.Today(clock);
        var ids = await db.Employees.AsNoTracking().Where(e => e.TerminationDate == null || e.TerminationDate >= today).Select(e => e.Id).ToListAsync(cancellationToken);
        await SyncAsync(ids, cancellationToken);
        var limit = today.AddDays(window);
        var due = await (from p in db.VacationPeriods.AsNoTracking()
                         join e in db.Employees.AsNoTracking() on p.EmployeeId equals e.Id
                         where ids.Contains(p.EmployeeId) && (p.Status == VacationPeriodStatus.Expired || (p.Status == VacationPeriodStatus.Available && p.ConcessionEndsOn <= limit))
                         orderby p.ConcessionEndsOn, e.Name
                         select new { Period = p, e.Name, e.Registration }).ToListAsync(cancellationToken);
        var first = new DateOnly(today.Year, today.Month, 1);
        var last = first.AddMonths(6).AddDays(-1);
        var entries = await (from s in db.VacationSchedules.AsNoTracking()
                             join e in db.Employees.IgnoreQueryFilters().AsNoTracking() on s.EmployeeId equals e.Id
                             where s.Status != VacationScheduleStatus.Cancelled && s.StartsOn <= last && s.StartsOn >= first.AddDays(-400)
                             orderby s.StartsOn, e.Name
                             select new { Schedule = s, e.Name }).ToListAsync(cancellationToken);
        entries = entries.Where(x => x.Schedule.EndsOn >= first).ToList();
        var calendarMonths = Enumerable.Range(0, 6).Select(i => first.AddMonths(i)).Select(month =>
        {
            var end = month.AddMonths(1).AddDays(-1);
            return new VacationCalendarMonth(month, entries.Where(x => x.Schedule.StartsOn <= end && x.Schedule.EndsOn >= month)
                .Select(x => new VacationCalendarEntry(x.Schedule.Id, x.Schedule.EmployeeId, x.Name, x.Schedule.StartsOn, x.Schedule.EndsOn, x.Schedule.Days, x.Schedule.Status,
                    VacationPolicy.ScheduleStatusLabel(x.Schedule.Status))).ToList());
        }).ToList();
        return new VacationOverview(window, due.Where(d => d.Period.Balance > 0).Select(d => new VacationDueRow(d.Period.EmployeeId, d.Name, d.Registration, d.Period.Id, d.Period.StartsOn,
            d.Period.EndsOn, d.Period.Balance, d.Period.ConcessionEndsOn, d.Period.Status, VacationPolicy.StatusLabel(d.Period.Status, d.Period.ConcessionEndsOn, today),
            Tone(d.Period.Status, d.Period.ConcessionEndsOn, today))).ToList(), calendarMonths);
    }

    /// <summary>"Meu ponto" (CA 5 e 12, ADR 0035): os períodos e as férias do funcionário da sessão, sem valor.</summary>
    public async Task<MyVacations> MineAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        await SyncAsync([employeeId], cancellationToken);
        var today = BusinessTime.Today(clock);
        var periods = await db.VacationPeriods.AsNoTracking().Where(p => p.EmployeeId == employeeId).OrderByDescending(p => p.Sequence).ToListAsync(cancellationToken);
        var schedules = await db.VacationSchedules.AsNoTracking().Where(s => s.EmployeeId == employeeId && s.Status != VacationScheduleStatus.Cancelled).OrderByDescending(s => s.StartsOn).ToListAsync(cancellationToken);
        return new MyVacations(
            periods.Select(p => new MyVacationPeriod(p.StartsOn, p.EndsOn, p.EntitledDays, p.TakenDays, p.SoldDays, p.Balance, p.ConcessionEndsOn, p.Status, VacationPolicy.StatusLabel(p.Status, p.ConcessionEndsOn, today))).ToList(),
            schedules.Select(s => new MyVacationSchedule(s.Id, s.StartsOn, s.EndsOn, s.Days, s.SoldDays, s.Status, VacationPolicy.ScheduleStatusLabel(s.Status))).ToList());
    }

    /// <summary>
    /// Job diário (CA 3): refaz os períodos de quem está ativo, marca como gozadas as férias que terminaram e avisa quem tem <c>hr.edit</c> do
    /// período concessivo que entrou nos 60 dias antes do fim ("Férias de {funcionário} vencem em {data}.", uma vez por período).
    /// </summary>
    public async Task<int> DailyAsync(CancellationToken cancellationToken = default)
    {
        var today = BusinessTime.Today(clock);
        var ids = await db.Employees.AsNoTracking().Where(e => e.TerminationDate == null || e.TerminationDate >= today).Select(e => e.Id).ToListAsync(cancellationToken);
        var started = await db.VacationSchedules.Where(s => (s.Status == VacationScheduleStatus.Scheduled || s.Status == VacationScheduleStatus.Paid) && s.StartsOn < today).ToListAsync(cancellationToken);
        foreach (var done in started.Where(s => s.EndsOn < today))
        {
            done.Status = VacationScheduleStatus.Taken;
        }

        await db.SaveChangesAsync(cancellationToken);
        await SyncAsync(ids, cancellationToken);
        var limit = today.AddDays(VacationPolicy.DueAlertDays);
        var due = await (from p in db.VacationPeriods.AsNoTracking()
                         join e in db.Employees.AsNoTracking() on p.EmployeeId equals e.Id
                         where ids.Contains(p.EmployeeId) && p.Status == VacationPeriodStatus.Available && p.ConcessionEndsOn <= limit
                         select new { p.Id, p.EmployeeId, p.ConcessionEndsOn, p.EntitledDays, p.TakenDays, p.SoldDays, e.Name }).ToListAsync(cancellationToken);
        var recipients = await users.UsersWithPermissionAsync("hr.edit", cancellationToken);
        var sent = 0;
        foreach (var period in due.Where(p => p.EntitledDays - p.TakenDays - p.SoldDays > 0))
        {
            foreach (var user in recipients)
            {
                var result = await notifications.NotifyOnceAsync(user, NotificationType, $"Férias de {period.Name} vencem em {VacationPolicy.Date(period.ConcessionEndsOn)}.", EmployeeLink(period.EmployeeId),
                    "vacation-due:" + period.Id, cancellationToken);
                sent += result.IsSuccess ? 1 : 0;
            }
        }

        return sent;
    }

    /// <summary>
    /// Refaz os períodos aquisitivos dos funcionários (por ordem desde a admissão), com os dias gozados e vendidos das programações não
    /// canceladas; grava só o que mudou (a auditoria não ganha linha à toa). O período sem programação que deixou de existir sai.
    /// </summary>
    internal async Task SyncAsync(IReadOnlyCollection<Guid> employeeIds, CancellationToken ct)
    {
        var ids = employeeIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var today = BusinessTime.Today(clock);
        var (prm, _) = await parameters.VacationParametersAsync(ct);
        var employees = await db.Employees.IgnoreQueryFilters().AsNoTracking().Where(e => ids.Contains(e.Id)).Select(e => new { e.Id, e.AdmissionDate, e.TerminationDate }).ToListAsync(ct);
        var leaves = (await db.EmployeeHistory.AsNoTracking().Where(h => ids.Contains(h.EmployeeId) && h.Kind == EmployeeHistoryKind.Leave && h.CancelledAt == null)
            .Select(h => new { h.EmployeeId, h.EffectiveDate, h.LeaveEndDate, h.LeaveType }).ToListAsync(ct)).ToLookup(h => h.EmployeeId);
        var sheets = (await db.Timesheets.AsNoTracking().Where(t => ids.Contains(t.EmployeeId) && t.Status == TimesheetStatus.Closed && t.DaysSnapshot != null)
            .Select(t => new { t.EmployeeId, t.DaysSnapshot }).ToListAsync(ct)).ToLookup(t => t.EmployeeId);
        var existing = (await db.VacationPeriods.Where(p => ids.Contains(p.EmployeeId)).ToListAsync(ct)).ToLookup(p => p.EmployeeId);
        var schedules = (await db.VacationSchedules.AsNoTracking().Where(s => ids.Contains(s.EmployeeId) && s.Status != VacationScheduleStatus.Cancelled)
            .Select(s => new { s.PeriodId, s.Days, s.SoldDays }).ToListAsync(ct)).ToLookup(s => s.PeriodId);
        var now = clock.GetUtcNow();
        foreach (var e in employees)
        {
            var absences = sheets[e.Id].SelectMany(t => JsonSerializer.Deserialize<List<TimesheetDayResult>>(t.DaysSnapshot!, TimesheetService.SnapshotJson) ?? [])
                .Where(d => d.Status == TimesheetDayStatus.Absent).Select(d => d.Day).ToHashSet();
            var plans = VacationPolicy.Plan(new VacationPlanInput(e.AdmissionDate, e.TerminationDate, today,
                leaves[e.Id].Select(l => new VacationLeaveInput(l.EffectiveDate, l.LeaveEndDate, l.LeaveType)).ToList(), absences, prm));
            var rows = existing[e.Id].ToDictionary(p => p.Sequence);
            foreach (var plan in plans)
            {
                var row = rows.GetValueOrDefault(plan.Sequence);
                if (row is null)
                {
                    row = new VacationPeriod { EmployeeId = e.Id, Sequence = plan.Sequence, CreatedAt = now, UpdatedAt = now };
                    db.VacationPeriods.Add(row);
                }

                var taken = schedules[row.Id].Sum(s => s.Days);
                var sold = schedules[row.Id].Sum(s => s.SoldDays);
                var status = VacationPolicy.StatusOf(plan.End, plan.ConcessionEnd, plan.RestartReason is not null, plan.EntitledDays - taken - sold, today, e.TerminationDate);
                var before = (row.StartsOn, row.EndsOn, row.Absences, row.EntitledDays, row.TakenDays, row.SoldDays, row.ConcessionEndsOn, row.Status, row.RestartedReason, row.RestartedOn);
                var after = (plan.Start, plan.End, plan.Absences, plan.EntitledDays, taken, sold, plan.ConcessionEnd, status, plan.RestartReason, plan.RestartedOn);
                if (before != after)
                {
                    (row.StartsOn, row.EndsOn, row.Absences, row.EntitledDays, row.TakenDays, row.SoldDays, row.ConcessionEndsOn, row.Status, row.RestartedReason, row.RestartedOn) = after;
                    row.UpdatedAt = now;
                }
            }

            var keep = plans.Select(p => p.Sequence).ToHashSet();
            foreach (var orphan in existing[e.Id].Where(p => !keep.Contains(p.Sequence) && !schedules[p.Id].Any()))
            {
                db.VacationPeriods.Remove(orphan);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Feriado ou parada do calendário padrão (sem hora disponível) e o domingo, o repouso semanal (CA 4).</summary>
    private async Task<Func<DateOnly, bool>> RestOrHolidayAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var holidays = (await calendar.ExceptionsAsync(from, to, ct)).Where(x => x.HoursAvailable <= 0m).Select(x => x.Date).ToHashSet();
        return d => d.DayOfWeek == DayOfWeek.Sunday || holidays.Contains(d);
    }

    private async Task<List<VacationScheduleItem>> SchedulesAsync(System.Linq.Expressions.Expression<Func<VacationSchedule, bool>> filter, CancellationToken ct)
    {
        var list = await db.VacationSchedules.AsNoTracking().Where(filter).OrderByDescending(s => s.StartsOn).ToListAsync(ct);
        var runIds = list.Select(s => s.PayrollRunId).OfType<Guid>().ToList();
        var runList = await db.PayrollRuns.AsNoTracking().Where(r => runIds.Contains(r.Id)).Select(r => new { r.Id, r.Status, r.IsSimulation, r.CalculatedAt }).ToDictionaryAsync(r => r.Id, ct);
        var slips = await db.Payslips.AsNoTracking().Where(p => runIds.Contains(p.PayrollRunId)).Select(p => new { p.PayrollRunId, p.EmployeeId, p.Id }).ToListAsync(ct);
        return list.Select(s =>
        {
            var run = s.PayrollRunId is { } id ? runList.GetValueOrDefault(id) : null;
            var slip = slips.FirstOrDefault(p => p.PayrollRunId == s.PayrollRunId && p.EmployeeId == s.EmployeeId)?.Id;
            return new VacationScheduleItem(s.Id, s.EmployeeId, s.PeriodId, s.StartsOn, s.EndsOn, s.Days, s.SoldDays, s.Advance13th, s.PaymentDueOn, s.Status, VacationPolicy.ScheduleStatusLabel(s.Status),
                s.PayrollRunId, slip, run?.CalculatedAt is not null, run is not null && run.Status != PayrollRunStatus.Draft, run?.IsSimulation ?? false, s.CreatedAt, s.CancelledAt, s.CancelReason, s.Version);
        }).ToList();
    }

    /// <summary>Folha de férias calculada em rascunho sai com a programação (sem título: nada foi fechado).</summary>
    private async Task RemoveDraftRunAsync(PayrollRun run, CancellationToken ct)
    {
        var slips = await db.Payslips.Where(p => p.PayrollRunId == run.Id).ToListAsync(ct);
        var slipIds = slips.Select(p => p.Id).ToList();
        db.PayslipLines.RemoveRange(await db.PayslipLines.Where(l => slipIds.Contains(l.PayslipId)).ToListAsync(ct));
        db.Payslips.RemoveRange(slips);
        db.PayrollRunCharges.RemoveRange(await db.PayrollRunCharges.Where(c => c.PayrollRunId == run.Id).ToListAsync(ct));
        db.PayrollCalculationLogs.RemoveRange(await db.PayrollCalculationLogs.Where(l => l.PayrollRunId == run.Id).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        db.PayrollRuns.Remove(run);
    }

    private static VacationPart Part(VacationSchedule s) => new(s.StartsOn, s.Days, s.SoldDays);

    internal static VacationPeriodItem PeriodItem(VacationPeriod p, DateOnly today)
    {
        var memory = new List<string>();
        if (p.RestartedReason is { } reason)
        {
            memory.Add(p.RestartedOn is { } on ? $"Período reiniciado em {VacationPolicy.Date(on)}: {reason}." : $"Período encerrado em {VacationPolicy.Date(p.EndsOn)}: {reason}; o próximo começa no retorno.");
        }
        else
        {
            memory.Add($"{VacationPolicy.Count(p.Absences, "falta injustificada", "faltas injustificadas")}: {p.EntitledDays} dias de direito (parâmetro hr.vacation_absence_brackets).");
        }

        return new VacationPeriodItem(p.Id, p.Sequence, p.StartsOn, p.EndsOn, p.Absences, p.EntitledDays, p.TakenDays, p.SoldDays, p.Balance, p.ConcessionEndsOn, p.Status,
            VacationPolicy.StatusLabel(p.Status, p.ConcessionEndsOn, today), Tone(p.Status, p.ConcessionEndsOn, today), p.RestartedReason, p.RestartedOn, memory);
    }

    /// <summary>Tom do chip (CA 1): "Vence em" é alerta, "Vencido" é crítico, "A gozar" é ok e o resto informativo.</summary>
    private static string Tone(VacationPeriodStatus status, DateOnly concessionEnd, DateOnly today) => status switch
    {
        VacationPeriodStatus.Expired => "crit",
        VacationPeriodStatus.Available when VacationPolicy.DueSoon(status, concessionEnd, today) => "warn",
        VacationPeriodStatus.Available => "ok",
        _ => "info",
    };

    internal static string EmployeeLink(Guid employeeId) => $"/app/rh/funcionarios/{employeeId}?aba=ferias";

    internal static string NoticeLink(Guid scheduleId) => $"/api/v1/hr/vacations/{scheduleId}/notice";

    private static Error EmployeeNotFound() => Error.NotFound("employee_not_found", "Funcionário não encontrado.");

    private static Error ScheduleNotFound() => Error.NotFound("vacation_schedule_not_found", "Programação de férias não encontrada.");

    private static Error Stale() => Error.Conflict("concurrency_conflict", "A programação mudou enquanto você trabalhava. Recarregue.");
}
