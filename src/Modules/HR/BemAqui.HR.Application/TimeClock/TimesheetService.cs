using System.Globalization;
using System.Text.Json;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application.TimeClock;

/// <summary>
/// Espelho de ponto (RH-01c): o cálculo pelo <see cref="TimesheetCalculator"/> (puro) com a jornada do funcionário, o calendário padrão pelo
/// <see cref="IBusinessCalendar"/> do Production (sem ler tabela de outro módulo), as marcações válidas, os abonos aprovados (RH-01b), os
/// afastamentos (CAD-04c1), os dias revisados e os parâmetros do RH. Período em aberto: recalculado a cada leitura; fechado: os dias e os
/// totais congelados no fechamento. O motivo do afastamento e do abono só sai com <c>hr.view_health</c> (ou para o próprio funcionário).
/// </summary>
public sealed partial class TimesheetService(
    IHrDb db,
    HrParametersService parameters,
    IBusinessCalendar calendar,
    IPermissionChecker permissions,
    IUserDirectory users,
    IEventPublisher events,
    INotificationService notifications,
    IAuditWriter audit,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public const string HealthAction = "view_health";
    public const string NotificationType = "hr.timesheet";
    public const string NotFoundCode = "timesheet_not_found";

    internal static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);

    /// <summary>Funcionário do período: só o que o espelho precisa (sem CPF nem salário).</summary>
    internal sealed record Person(Guid Id, string Name, string? Registration, string PositionName, Guid? ScheduleId, DateOnly Admission, DateOnly? Termination, DateOnly? Agreement);

    /// <summary>O período resolvido: o intervalo, se está fechado, as linhas gravadas e os parâmetros que valem.</summary>
    internal sealed record PeriodInfo(DateOnly Period, DateOnly Start, DateOnly End, bool Closed, List<Timesheet> Rows, TimesheetParameters Parameters);

    /// <summary>Visão geral do período (<c>hr.view</c>): sem período, o mês do período em aberto mais antigo ou o atual.</summary>
    public async Task<Result<TimesheetOverview>> OverviewAsync(string? period, CancellationToken cancellationToken = default)
    {
        var chosen = string.IsNullOrWhiteSpace(period) ? await SuggestedPeriodAsync(cancellationToken) : PayrollRules.ParsePeriod(period);
        if (chosen is not { } month)
        {
            return InvalidPeriod();
        }

        var info = await PeriodAsync(month, cancellationToken);
        var people = await PeopleAsync(info, null, cancellationToken);
        var results = info.Closed ? null : await ComputeAsync(people, info, cancellationToken);
        var pending = await PendingByEmployeeAsync(people.Select(p => p.Id).ToList(), info.Start, info.End, cancellationToken);
        var rows = new List<TimesheetOverviewRow>();
        foreach (var p in people.OrderBy(p => p.Name, StringComparer.CurrentCulture))
        {
            var row = info.Rows.FirstOrDefault(r => r.EmployeeId == p.Id);
            var totals = info.Closed && row is not null ? FrozenTotals(row) : results![p.Id].Totals;
            var pendingCount = pending.GetValueOrDefault(p.Id);
            var (label, tone) = Situation(info.Closed, totals, pendingCount);
            var ack = info.Closed ? row?.EmployeeAckAt is { } at ? "Confirmado em " + TimesheetRules.Date(DateOnly.FromDateTime(BusinessTime.ToBusiness(at).DateTime)) : "Aguardando o funcionário" : "Período em aberto";
            rows.Add(new TimesheetOverviewRow(p.Id, p.Name, p.Registration, info.Closed ? TimesheetStatus.Closed : TimesheetStatus.Open, totals.ExpectedMinutes, totals.WorkedMinutes,
                totals.OvertimeMinutes, totals.AbsenceMinutes, totals.LateMinutes, totals.JustifiedMinutes, totals.BankMonthMinutes, totals.BankBalanceMinutes, totals.InconsistentDays, pendingCount,
                info.Closed ? row?.EmployeeAckAt : null, ack, label, tone));
        }

        var (closedAt, closedBy, banner) = await ClosedInfoAsync(info, cancellationToken);
        return new TimesheetOverview(info.Period, info.Start, info.End, await SuggestedPeriodAsync(cancellationToken), info.Closed ? TimesheetStatus.Closed : TimesheetStatus.Open,
            closedAt, closedBy, banner, !info.Closed && rows.Count > 0, TimeClockRules.InternalControlBanner, rows);
    }

    /// <summary>Espelho de um funcionário (<c>hr.view</c>, CA 3); funcionário inexistente, 404 (IDOR pela rota).</summary>
    public async Task<Result<TimesheetDetail>> EmployeeAsync(Guid employeeId, string? period, CancellationToken cancellationToken = default)
    {
        if (PayrollRules.ParsePeriod(period) is not { } month)
        {
            return InvalidPeriod();
        }

        var canHealth = await permissions.HasPermissionAsync("hr", HealthAction, cancellationToken);
        return await DetailAsync(employeeId, month, canHealth, cancellationToken);
    }

    /// <summary>"Meu espelho" (CA 8, ADR 0035): o funcionário vem da sessão; vê os próprios motivos.</summary>
    public async Task<Result<TimesheetDetail>> MineAsync(EmployeeSummary employee, string? period, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employee);
        var chosen = string.IsNullOrWhiteSpace(period) ? await SuggestedPeriodAsync(cancellationToken) : PayrollRules.ParsePeriod(period);
        if (chosen is not { } month)
        {
            return InvalidPeriod();
        }

        return await DetailAsync(employee.Id, month, true, cancellationToken);
    }

    /// <summary>Períodos do "Meu espelho" (CA 8): o em aberto e os fechados do próprio funcionário, do mais recente ao mais antigo.</summary>
    public async Task<IReadOnlyList<MyTimesheetPeriod>> MyPeriodsAsync(EmployeeSummary employee, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employee);
        var rows = await db.Timesheets.AsNoTracking().Where(t => t.EmployeeId == employee.Id).OrderByDescending(t => t.Period)
            .Select(t => new MyTimesheetPeriod(t.Period, t.PeriodStart, t.PeriodEnd, t.Status, t.Status == TimesheetStatus.Closed ? t.ClosedAt : null, t.Status == TimesheetStatus.Closed ? t.EmployeeAckAt : null))
            .Take(24).ToListAsync(cancellationToken);
        var open = await SuggestedPeriodAsync(cancellationToken);
        if (rows.All(r => r.Period != open))
        {
            var info = await PeriodAsync(open, cancellationToken);
            rows.Insert(0, new MyTimesheetPeriod(open, info.Start, info.End, TimesheetStatus.Open, null, null));
        }

        return rows.OrderByDescending(r => r.Period).ToList();
    }

    /// <summary>
    /// CA 16: os totais do espelho fechado do mês da folha ao lado do holerite e a referência de cada rubrica "Origem no ponto"; o valor
    /// continua vindo da contadora. Holerite de outra folha: 404.
    /// </summary>
    public async Task<Result<PayslipTimesheetHours>> PayslipHoursAsync(Guid runId, Guid payslipId, CancellationToken cancellationToken = default)
    {
        var slip = await (from p in db.Payslips.AsNoTracking()
                          join r in db.PayrollRuns.AsNoTracking() on p.PayrollRunId equals r.Id
                          where p.Id == payslipId && r.Id == runId
                          select new { p.EmployeeId, r.Period }).FirstOrDefaultAsync(cancellationToken);
        if (slip is null)
        {
            return Error.NotFound("payslip_not_found", "Holerite não encontrado.");
        }

        var row = await db.Timesheets.AsNoTracking().FirstOrDefaultAsync(t => t.EmployeeId == slip.EmployeeId && t.Period == slip.Period && t.Status == TimesheetStatus.Closed, cancellationToken);
        if (row is null)
        {
            return new PayslipTimesheetHours(false, $"O ponto de {PayrollRules.PeriodLabel(slip.Period)} ainda não foi fechado. Feche o período de ponto para usar as horas do espelho.",
                slip.Period, null, 0, 0, 0, null, null, []);
        }

        var items = await db.PayrollItems.AsNoTracking().Where(i => i.IsActive && i.TimesheetSource != null).OrderBy(i => i.Code).ToListAsync(cancellationToken);
        var references = items.Select(i =>
        {
            var minutes = i.TimesheetSource switch { PayrollTimesheetSource.Overtime => row.OvertimeMinutes, PayrollTimesheetSource.Absence => row.AbsenceMinutes, _ => row.LateMinutes };
            return new PayslipTimesheetReference(i.Id, i.Code, i.Name, i.TimesheetSource!.Value, minutes, TimesheetRules.Clock(minutes) + " h");
        }).ToList();
        return new PayslipTimesheetHours(true, $"Ponto fechado em {TimesheetRules.Date(DateOnly.FromDateTime(BusinessTime.ToBusiness(row.ClosedAt ?? row.CreatedAt).DateTime))}.", slip.Period, row.ClosedAt,
            row.OvertimeMinutes, row.AbsenceMinutes, row.LateMinutes, row.BankMonthMinutes, row.BankBalanceMinutes, references);
    }

    private async Task<Result<TimesheetDetail>> DetailAsync(Guid employeeId, DateOnly month, bool canHealth, CancellationToken ct)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId, ct))
        {
            return Error.NotFound("employee_not_found", "Funcionário não encontrado.");
        }

        var info = await PeriodAsync(month, ct);
        var people = await PeopleAsync(info, employeeId, ct);
        var person = people.FirstOrDefault();
        if (person is null)
        {
            // Fora do vínculo no período inteiro (admitido depois ou desligado antes): o espelho sai vazio, sem falta.
            person = await db.Employees.AsNoTracking().Where(e => e.Id == employeeId)
                .Select(e => new Person(e.Id, e.Name, e.Registration, db.JobPositions.IgnoreQueryFilters().Where(j => j.Id == e.PositionId).Select(j => j.Name).FirstOrDefault() ?? string.Empty,
                    e.WorkScheduleId ?? db.JobPositions.IgnoreQueryFilters().Where(j => j.Id == e.PositionId).Select(j => j.WorkScheduleId).FirstOrDefault(), e.AdmissionDate, e.TerminationDate, e.HourBankAgreementOn))
                .FirstAsync(ct);
        }

        var row = info.Rows.FirstOrDefault(r => r.EmployeeId == employeeId);
        IReadOnlyList<TimesheetDayResult> days;
        TimesheetTotals totals;
        if (info.Closed && row?.DaysSnapshot is { } snapshot)
        {
            days = JsonSerializer.Deserialize<List<TimesheetDayResult>>(snapshot, SnapshotJson) ?? [];
            totals = FrozenTotals(row);
        }
        else
        {
            var result = (await ComputeAsync([person], info, ct))[person.Id];
            days = result.Days;
            totals = result.Totals;
        }

        var reviews = await db.TimesheetDayReviews.AsNoTracking().Where(r => r.EmployeeId == employeeId && r.Day >= info.Start && r.Day <= info.End)
            .ToDictionaryAsync(r => r.Day, r => r.Comment, ct);
        var schedule = person.ScheduleId is { } sid ? (await db.WorkSchedules.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(s => s.Id == sid, ct))?.Days : null;
        var pending = (await PendingByEmployeeAsync([employeeId], info.Start, info.End, ct)).GetValueOrDefault(employeeId);
        var (closedAt, closedBy, banner) = await ClosedInfoAsync(info, ct);
        return new TimesheetDetail(person.Id, person.Name, person.Registration, person.PositionName, info.Period, info.Start, info.End,
            info.Closed ? TimesheetStatus.Closed : TimesheetStatus.Open, closedAt, closedBy, banner, row?.ReopenedAt, row?.ReopenReason,
            info.Closed ? row?.EmployeeAckAt : null, info.Closed ? row?.EmployeeAckSummary : null, TimeClockRules.InternalControlBanner, ScheduleSummary(schedule),
            days.Select(d => DayView(d, canHealth, reviews.GetValueOrDefault(d.Day))).ToList(), TotalsView(totals), days.Any(d => d.Entries.Count > 0), pending);
    }

    /// <summary>O período de referência: o intervalo (o congelado, se fechado; senão pelo parâmetro, começando depois do último fechado).</summary>
    internal async Task<PeriodInfo> PeriodAsync(DateOnly month, CancellationToken ct)
    {
        var period = new DateOnly(month.Year, month.Month, 1);
        var p = await parameters.TimesheetParametersAsync(ct);
        var rows = await db.Timesheets.AsNoTracking().Where(t => t.Period == period).ToListAsync(ct);
        var closed = rows.Where(r => r.Status == TimesheetStatus.Closed).ToList();
        if (closed.Count > 0)
        {
            return new PeriodInfo(period, closed.Min(r => r.PeriodStart), closed.Max(r => r.PeriodEnd), true, rows, p);
        }

        var (start, end) = TimesheetRules.Range(period, p.PeriodStartDay);
        var lastClosedEnd = await db.Timesheets.AsNoTracking().Where(t => t.Status == TimesheetStatus.Closed && t.Period < period).MaxAsync(t => (DateOnly?)t.PeriodEnd, ct);
        if (lastClosedEnd is { } last && last >= start && last < end)
        {
            start = last.AddDays(1);
        }

        return new PeriodInfo(period, start, end, false, rows, p);
    }

    /// <summary>Quem entra no período: no fechado, quem tem espelho; no aberto, quem tem vínculo em algum dia (o excluído não entra).</summary>
    internal async Task<List<Person>> PeopleAsync(PeriodInfo info, Guid? only, CancellationToken ct)
    {
        var query = info.Closed
            ? db.Employees.IgnoreQueryFilters().AsNoTracking().Where(e => db.Timesheets.Any(t => t.EmployeeId == e.Id && t.Period == info.Period && t.Status == TimesheetStatus.Closed))
            : db.Employees.AsNoTracking().Where(e => e.AdmissionDate <= info.End && (e.TerminationDate == null || e.TerminationDate >= info.Start));
        if (only is { } id)
        {
            query = query.Where(e => e.Id == id);
        }

        return await query.Select(e => new Person(e.Id, e.Name, e.Registration,
                db.JobPositions.IgnoreQueryFilters().Where(j => j.Id == e.PositionId).Select(j => j.Name).FirstOrDefault() ?? string.Empty,
                e.WorkScheduleId ?? db.JobPositions.IgnoreQueryFilters().Where(j => j.Id == e.PositionId).Select(j => j.WorkScheduleId).FirstOrDefault(),
                e.AdmissionDate, e.TerminationDate, e.HourBankAgreementOn))
            .ToListAsync(ct);
    }

    /// <summary>Calcula o período para todos de uma vez (CA 19): uma consulta por tabela, o resto em memória.</summary>
    internal async Task<Dictionary<Guid, TimesheetResult>> ComputeAsync(IReadOnlyList<Person> people, PeriodInfo info, CancellationToken ct)
    {
        var ids = people.Select(p => p.Id).ToList();
        var (start, end) = (info.Start, info.End);
        var scheduleIds = people.Select(p => p.ScheduleId).OfType<Guid>().Distinct().ToList();
        var schedules = await db.WorkSchedules.IgnoreQueryFilters().AsNoTracking().Where(s => scheduleIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => (IReadOnlyList<ScheduleDay>)s.Days, ct);
        var holidays = (await calendar.ExceptionsAsync(start, end, ct)).Where(x => x.HoursAvailable <= 0m).GroupBy(x => x.Date).ToDictionary(g => g.Key, g => g.First().Description);
        var entries = (await db.TimeEntries.AsNoTracking().Where(e => ids.Contains(e.EmployeeId) && e.Day >= start && e.Day <= end && e.Status == TimeEntryStatus.Valid)
            .Select(e => new { e.Id, e.EmployeeId, e.Day, e.At, e.Type, e.Source }).ToListAsync(ct)).ToLookup(e => e.EmployeeId);
        var absences = (await db.TimeAdjustments.AsNoTracking()
            .Where(a => ids.Contains(a.EmployeeId) && a.Kind == TimeAdjustmentKind.Absence && a.Status == TimeAdjustmentStatus.Approved && a.Day >= start && a.Day <= end && a.AbsenceReason != null)
            .OrderBy(a => a.DecidedAt).Select(a => new { a.EmployeeId, a.Day, a.AbsenceMinutes, a.AbsenceReason }).ToListAsync(ct)).ToLookup(a => a.EmployeeId);
        var leaves = (await db.EmployeeHistory.AsNoTracking()
            .Where(h => ids.Contains(h.EmployeeId) && h.Kind == EmployeeHistoryKind.Leave && h.CancelledAt == null && h.EffectiveDate <= end && (h.LeaveEndDate ?? h.EffectiveDate) >= start)
            .Select(h => new { h.EmployeeId, h.EffectiveDate, h.LeaveEndDate, h.LeaveType }).ToListAsync(ct)).ToLookup(h => h.EmployeeId);
        var reviews = (await db.TimesheetDayReviews.AsNoTracking().Where(r => ids.Contains(r.EmployeeId) && r.Day >= start && r.Day <= end)
            .Select(r => new { r.EmployeeId, r.Day }).ToListAsync(ct)).ToLookup(r => r.EmployeeId);
        var previous = await PreviousBalancesAsync(ids, info.Period, ct);
        var today = BusinessTime.Today(clock);
        var p = info.Parameters;
        var result = new Dictionary<Guid, TimesheetResult>();
        foreach (var person in people)
        {
            var input = new TimesheetInput(start, end, today, person.Admission, person.Termination, person.ScheduleId is { } sid ? schedules.GetValueOrDefault(sid) : null, holidays,
                entries[person.Id].Select(e => new TimesheetEntryInput(e.Id, e.Day, LocalMinute(e.At), e.Type, e.Source)).ToList(),
                absences[person.Id].Select(a => new TimesheetAbsenceInput(a.Day, a.AbsenceMinutes, a.AbsenceReason!.Value)).ToList(),
                leaves[person.Id].Select(l => new TimesheetLeaveInput(l.EffectiveDate, l.LeaveEndDate ?? l.EffectiveDate, l.LeaveType)).ToList(),
                reviews[person.Id].Select(r => r.Day).ToHashSet(),
                new TimesheetSettings(p.TolerancePerEntryMinutes, p.TolerancePerDayMinutes, p.HourBankEnabled, person.Agreement), previous.GetValueOrDefault(person.Id));
            result[person.Id] = TimesheetCalculator.Calculate(input);
        }

        return result;
    }

    /// <summary>Saldo acumulado do último período fechado antes deste, por funcionário (CA 9).</summary>
    private async Task<Dictionary<Guid, int>> PreviousBalancesAsync(List<Guid> ids, DateOnly period, CancellationToken ct)
    {
        var rows = await db.Timesheets.AsNoTracking().Where(t => ids.Contains(t.EmployeeId) && t.Status == TimesheetStatus.Closed && t.Period < period)
            .Select(t => new { t.EmployeeId, t.Period, t.BankBalanceMinutes }).ToListAsync(ct);
        return rows.GroupBy(r => r.EmployeeId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Period).First().BankBalanceMinutes ?? 0);
    }

    internal async Task<Dictionary<Guid, int>> PendingByEmployeeAsync(List<Guid> ids, DateOnly start, DateOnly end, CancellationToken ct) =>
        await db.TimeAdjustments.AsNoTracking().Where(a => ids.Contains(a.EmployeeId) && a.Status == TimeAdjustmentStatus.Pending && a.Day >= start && a.Day <= end)
            .GroupBy(a => a.EmployeeId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);

    /// <summary>Sugestão: o período do dia de hoje (o em aberto mais antigo, se houver período anterior ainda aberto depois de um fechado).</summary>
    internal async Task<DateOnly> SuggestedPeriodAsync(CancellationToken ct)
    {
        var p = await parameters.TimesheetParametersAsync(ct);
        var current = TimesheetRules.PeriodOf(BusinessTime.Today(clock), p.PeriodStartDay);
        var lastClosed = await db.Timesheets.AsNoTracking().Where(t => t.Status == TimesheetStatus.Closed).MaxAsync(t => (DateOnly?)t.Period, ct);
        return lastClosed is { } last && last.AddMonths(1) < current ? last.AddMonths(1) : current;
    }

    private async Task<(DateTimeOffset? At, string? By, string? Banner)> ClosedInfoAsync(PeriodInfo info, CancellationToken ct)
    {
        if (!info.Closed)
        {
            return (null, null, null);
        }

        var last = info.Rows.Where(r => r.Status == TimesheetStatus.Closed).OrderByDescending(r => r.ClosedAt).First();
        var by = last.ClosedBy is { } id ? await users.DisplayNameAsync(id, ct) : null;
        var at = last.ClosedAt ?? last.CreatedAt;
        return (at, by, $"Período fechado em {TimesheetRules.Date(DateOnly.FromDateTime(BusinessTime.ToBusiness(at).DateTime))} por {by ?? "um usuário"}");
    }

    internal static TimesheetTotals FrozenTotals(Timesheet t) =>
        new(t.ExpectedMinutes, t.WorkedMinutes, t.OvertimeMinutes, t.AbsenceMinutes, t.LateMinutes, t.JustifiedMinutes, 0, t.WorkedDays, t.AbsenceDays, 0,
            t.BankMonthMinutes is not null, t.BankMonthMinutes, t.BankBalanceMinutes, TimesheetRules.ParseByReason(t.JustifiedByReason));

    internal static TimesheetTotalsView TotalsView(TimesheetTotals t) =>
        new(t.ExpectedMinutes, t.WorkedMinutes, t.OvertimeMinutes, t.AbsenceMinutes, t.LateMinutes, t.JustifiedMinutes, t.CompensatedMinutes, t.WorkedDays, t.AbsenceDays, t.InconsistentDays,
            t.BankApplies, t.BankMonthMinutes, t.BankBalanceMinutes, t.BankMonthMinutes is { } m && t.BankBalanceMinutes is { } b ? TimesheetRules.BankText(m, b) : null);

    /// <summary>O dia para a tela: o motivo do abono e o tipo do afastamento só com <paramref name="canHealth"/>; sem, "Ausência justificada" e "Afastado".</summary>
    internal static TimesheetDayView DayView(TimesheetDayResult d, bool canHealth, string? reviewComment) =>
        new(d.Day, d.Status, TimesheetRules.StatusLabel(d.Status), d.ExpectedMinutes, d.WorkedMinutes, d.OvertimeMinutes, d.LateMinutes, d.AbsenceMinutes, d.JustifiedMinutes, d.CompensatedMinutes,
            d.BankMinutes, d.Issues, d.Reviewed || reviewComment is not null, reviewComment,
            d.Entries.Select(e => new TimesheetEntryView(e.Id, e.Time.ToString("HH:mm", CultureInfo.InvariantCulture), e.Type, TimeClockRules.Label(e.Type), e.Source)).ToList(),
            d.Absences.Select(a => new TimesheetAbsenceView(a.Minutes, canHealth ? a.Reason : null,
                canHealth ? AbsenceReasons.Label(a.Reason) : TimeAdjustmentRules.ProtectedLabel, !canHealth)).ToList(),
            d.Holiday, d.Leave is null ? null : canHealth && d.Leave.Reason is { } r ? "Afastado: " + AbsenceReasons.Label(r) : "Afastado");

    /// <summary>"seg a sex 08:00–17:48, intervalo 1 h" da jornada, ou "Sem jornada".</summary>
    private static string ScheduleSummary(IReadOnlyList<ScheduleDay>? days)
    {
        if (days is not { Count: > 0 })
        {
            return "Sem jornada cadastrada";
        }

        string[] names = ["seg", "ter", "qua", "qui", "sex", "sáb", "dom"];
        return string.Join("; ", days.OrderBy(d => d.Weekday).GroupBy(d => (d.Start, d.End, d.BreakMinutes)).Select(g =>
        {
            var list = g.Select(d => d.Weekday).ToList();
            var label = list.Count > 2 && list[^1] - list[0] == list.Count - 1 ? $"{names[list[0] - 1]} a {names[list[^1] - 1]}" : string.Join(", ", list.Select(w => names[w - 1]));
            return $"{label} {g.Key.Start}–{g.Key.End}" + (g.Key.BreakMinutes > 0 ? $", intervalo {TimesheetRules.Duration(g.Key.BreakMinutes)}" : string.Empty);
        }));
    }

    private static (string Label, string Tone) Situation(bool closed, TimesheetTotals t, int pending) =>
        closed ? ("Fechado", "info")
        : pending > 0 ? (pending == 1 ? "1 pedido pendente" : $"{pending} pedidos pendentes", "alert")
        : t.InconsistentDays > 0 ? (t.InconsistentDays == 1 ? "1 dia inconsistente" : $"{t.InconsistentDays} dias inconsistentes", "alert")
        : t.AbsenceDays > 0 ? (t.AbsenceDays == 1 ? "1 falta" : $"{t.AbsenceDays} faltas", "critical")
        : ("Em dia", "ok");

    private static TimeOnly LocalMinute(DateTimeOffset at)
    {
        var local = BusinessTime.ToBusiness(at);
        return new TimeOnly(local.Hour, local.Minute);
    }

    internal static Error InvalidPeriod() => PayrollRules.Field("period", "Escolha o período (mês e ano).");
}
