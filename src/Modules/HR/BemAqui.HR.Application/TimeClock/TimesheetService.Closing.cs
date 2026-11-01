using System.Text.Json;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application.TimeClock;

/// <summary>
/// Fechamento, reabertura, dia revisado, "Li e confirmo" e o acordo de banco de horas (RH-01c CA 2 e 11 a 15). O fechamento e a reabertura
/// tomam o lock do período (<c>pg_advisory_xact_lock</c>) na transação: dois fechamentos ao mesmo tempo, só um grava; o outro recebe 409
/// <c>concurrency_conflict</c> (CA 14). Nada é apagado: a reabertura guarda quem, quando e o motivo.
/// </summary>
public sealed partial class TimesheetService
{
    /// <summary>Pré-condições do "Fechar o período {mês}" (CA 11), com <c>hr.edit</c>.</summary>
    public async Task<Result<TimesheetCloseCheck>> CloseCheckAsync(string? period, CancellationToken cancellationToken = default)
    {
        if (PayrollRules.ParsePeriod(period) is not { } month)
        {
            return InvalidPeriod();
        }

        var info = await PeriodAsync(month, cancellationToken);
        return await CheckAsync(info, cancellationToken);
    }

    private async Task<TimesheetCloseCheck> CheckAsync(PeriodInfo info, CancellationToken ct)
    {
        var blockers = new List<string>();
        if (info.Closed)
        {
            blockers.Add($"O período {PayrollRules.PeriodLabel(info.Period)} já está fechado.");
            return new TimesheetCloseCheck(info.Period, info.Start, info.End, false, blockers, [], 0);
        }

        var today = BusinessTime.Today(clock);
        if (info.End >= today)
        {
            blockers.Add($"O período só pode ser fechado depois do último dia ({TimesheetRules.Date(info.End)}).");
        }

        var lastClosed = await db.Timesheets.AsNoTracking().Where(t => t.Status == TimesheetStatus.Closed && t.Period < info.Period).MaxAsync(t => (DateOnly?)t.Period, ct);
        if (lastClosed is { } last && last < info.Period.AddMonths(-1))
        {
            blockers.Add($"Feche antes o período de {PayrollRules.PeriodLabel(last.AddMonths(1))}: o saldo do banco de horas depende dele.");
        }

        var people = await PeopleAsync(info, null, ct);
        if (people.Count == 0)
        {
            blockers.Add("Nenhum funcionário com vínculo neste período.");
        }

        var results = await ComputeAsync(people, info, ct);
        var pending = await PendingByEmployeeAsync(people.Select(p => p.Id).ToList(), info.Start, info.End, ct);
        var employees = people.OrderBy(p => p.Name, StringComparer.CurrentCulture)
            .Select(p => new TimesheetClosePrecondition(p.Id, p.Name, pending.GetValueOrDefault(p.Id),
                results[p.Id].Days.Where(d => d.Status == TimesheetDayStatus.Inconsistent && !d.Reviewed).Select(d => d.Day).ToList()))
            .Where(e => e.PendingAdjustments > 0 || e.InconsistentDays.Count > 0).ToList();
        return new TimesheetCloseCheck(info.Period, info.Start, info.End, blockers.Count == 0 && employees.Count == 0, blockers, employees, people.Count);
    }

    /// <summary>
    /// "Fechar período" (CA 11 e 14): sem pendência, grava para cada funcionário do período o espelho com os totais e os dias congelados e
    /// <c>Closed</c>, na mesma transação, com o <c>TimesheetPeriodClosed</c> no outbox. Idempotente por (funcionário, período): o espelho
    /// reaberto é atualizado, nunca duplicado.
    /// </summary>
    public async Task<Result<TimesheetCloseResult>> CloseAsync(string? period, CancellationToken cancellationToken = default)
    {
        if (PayrollRules.ParsePeriod(period) is not { } month)
        {
            return InvalidPeriod();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(month, cancellationToken);
        var info = await PeriodAsync(month, cancellationToken);
        if (info.Closed)
        {
            return Error.Conflict(TimesheetRules.ConcurrencyCode, $"O período {PayrollRules.PeriodLabel(info.Period)} já foi fechado. Recarregue a página.");
        }

        var check = await CheckAsync(info, cancellationToken);
        if (!check.CanClose)
        {
            var first = (check.Blockers.Count > 0 ? check.Blockers[0] : null) ?? "Resolva os pedidos de ajuste pendentes e os dias inconsistentes antes de fechar.";
            return Error.Conflict(TimesheetRules.PreconditionsCode, first);
        }

        var people = await PeopleAsync(info, null, cancellationToken);
        var results = await ComputeAsync(people, info, cancellationToken);
        var tracked = await db.Timesheets.Where(t => t.Period == info.Period).ToDictionaryAsync(t => t.EmployeeId, cancellationToken);
        var now = clock.GetUtcNow();
        foreach (var person in people)
        {
            var r = results[person.Id];
            if (!tracked.TryGetValue(person.Id, out var sheet))
            {
                sheet = new Timesheet { EmployeeId = person.Id, Period = info.Period, CreatedAt = now };
                db.Timesheets.Add(sheet);
            }

            var t = r.Totals;
            sheet.PeriodStart = info.Start;
            sheet.PeriodEnd = info.End;
            sheet.Status = TimesheetStatus.Closed;
            sheet.ExpectedMinutes = t.ExpectedMinutes;
            sheet.WorkedMinutes = t.WorkedMinutes;
            sheet.OvertimeMinutes = t.OvertimeMinutes;
            sheet.AbsenceMinutes = t.AbsenceMinutes;
            sheet.LateMinutes = t.LateMinutes;
            sheet.JustifiedMinutes = t.JustifiedMinutes;
            sheet.BankMonthMinutes = t.BankMonthMinutes;
            sheet.BankBalanceMinutes = t.BankBalanceMinutes;
            sheet.WorkedDays = t.WorkedDays;
            sheet.AbsenceDays = t.AbsenceDays;
            sheet.JustifiedByReason = TimesheetRules.FormatByReason(t.JustifiedByReason);
            sheet.DaysSnapshot = JsonSerializer.Serialize(r.Days, SnapshotJson);
            sheet.ClosedAt = now;
            sheet.ClosedBy = currentUser.UserId;
            sheet.EmployeeAckAt = null;
            sheet.EmployeeAckBy = null;
            sheet.EmployeeAckSummary = null;
            sheet.PdfAttachmentId = null;
        }

        events.Publish(new TimesheetPeriodClosed(info.Period));
        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        await transaction.CommitAsync(cancellationToken);
        return new TimesheetCloseResult(info.Period, people.Count, $"Período {PayrollRules.PeriodLabel(info.Period)} fechado");
    }

    /// <summary>
    /// "Reabrir período" (CA 13), com <c>hr.approve</c> e motivo: o período e os seguintes já fechados voltam a <c>Open</c> (o saldo acumulado
    /// depende deles), as ciências ficam sem efeito, os PDFs deixam de valer e os membros do Proprietário são avisados.
    /// </summary>
    public async Task<Result<TimesheetReopenResult>> ReopenAsync(string? period, TimesheetReopenInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (PayrollRules.ParsePeriod(period) is not { } month)
        {
            return InvalidPeriod();
        }

        var reason = (input.Reason ?? string.Empty).Trim();
        if (reason.Length is < TimesheetRules.ReasonMin or > TimesheetRules.ReasonMax)
        {
            return PayrollRules.Field("reason", $"Informe o motivo da reabertura, de {TimesheetRules.ReasonMin} a {TimesheetRules.ReasonMax} caracteres.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(month, cancellationToken);
        var rows = await db.Timesheets.Where(t => t.Period >= month && t.Status == TimesheetStatus.Closed).ToListAsync(cancellationToken);
        if (rows.All(r => r.Period != month))
        {
            return Error.Conflict(TimesheetRules.NotClosedCode, $"O período {PayrollRules.PeriodLabel(month)} não está fechado.");
        }

        var now = clock.GetUtcNow();
        foreach (var row in rows)
        {
            row.Status = TimesheetStatus.Open;
            row.ReopenedAt = now;
            row.ReopenedBy = currentUser.UserId;
            row.ReopenReason = reason;
            row.EmployeeAckAt = null;
            row.EmployeeAckBy = null;
            row.EmployeeAckSummary = null;
            row.PdfAttachmentId = null;
        }

        var periods = rows.Select(r => r.Period).Distinct().Order().ToList();
        foreach (var p in periods)
        {
            events.Publish(new TimesheetPeriodReopened(p));
        }

        var who = currentUser.UserId is { } me ? await users.DisplayNameAsync(me, cancellationToken) ?? "um usuário" : "o sistema";
        foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
        {
            notifications.Notify(owner, NotificationType, $"Ponto de {PayrollRules.PeriodLabel(month)} reaberto por {who}: {(reason.Length > 100 ? reason[..100] + "…" : reason)}",
                "/app/rh/ponto/espelho?mes=" + month.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture));
        }

        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        await transaction.CommitAsync(cancellationToken);
        var message = periods.Count == 1 ? $"Período {PayrollRules.PeriodLabel(month)} reaberto"
            : $"Período {PayrollRules.PeriodLabel(month)} reaberto, com os {periods.Count - 1} seguintes que já estavam fechados";
        return new TimesheetReopenResult(periods, message);
    }

    /// <summary>"Marcar como revisado" (CA 11), com <c>hr.edit</c>: só dia inconsistente de período em aberto, com comentário de 5 a 200.</summary>
    public async Task<Result<TimesheetDayView>> ReviewDayAsync(Guid employeeId, TimesheetDayReviewInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var comment = (input.Comment ?? string.Empty).Trim();
        if (comment.Length is < TimesheetRules.CommentMin or > TimesheetRules.CommentMax)
        {
            return PayrollRules.Field("comment", $"Escreva o comentário da revisão, de {TimesheetRules.CommentMin} a {TimesheetRules.CommentMax} caracteres. Não informe CID nem diagnóstico.");
        }

        if (!await db.Employees.AnyAsync(e => e.Id == employeeId, cancellationToken))
        {
            return Error.NotFound("employee_not_found", "Funcionário não encontrado.");
        }

        var p = await parameters.TimesheetParametersAsync(cancellationToken);
        var info = await PeriodAsync(TimesheetRules.PeriodOf(input.Day, p.PeriodStartDay), cancellationToken);
        if (info.Closed || input.Day < info.Start || input.Day > info.End)
        {
            return TimesheetPeriods.Closed(info.Period);
        }

        var people = await PeopleAsync(info, employeeId, cancellationToken);
        var day = people.Count == 0 ? null : (await ComputeAsync(people, info, cancellationToken))[employeeId].Days.FirstOrDefault(d => d.Day == input.Day);
        if (day is null || day.Status != TimesheetDayStatus.Inconsistent)
        {
            return Error.Validation("timesheet_day_not_inconsistent", "Este dia não está inconsistente: não precisa de revisão.");
        }

        if (!day.Reviewed)
        {
            db.TimesheetDayReviews.Add(new TimesheetDayReview { EmployeeId = employeeId, Day = input.Day, Comment = comment, ReviewedBy = currentUser.UserId, ReviewedAt = clock.GetUtcNow() });
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
            {
                // Outro revisor marcou o mesmo dia ao mesmo tempo: vale o primeiro.
                db.ChangeTracker.Clear();
            }
        }

        var saved = await db.TimesheetDayReviews.AsNoTracking().Where(r => r.EmployeeId == employeeId && r.Day == input.Day).Select(r => r.Comment).FirstAsync(cancellationToken);
        return DayView(day with { Reviewed = true }, await permissions.HasPermissionAsync("hr", HealthAction, cancellationToken), saved);
    }

    /// <summary>"Li e confirmo" do próprio funcionário (CA 15): só no período fechado; grava quando, quem e o resumo dos totais. Não bloqueia nada.</summary>
    public async Task<Result<TimesheetAckResult>> AcknowledgeAsync(EmployeeSummary employee, string? period, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employee);
        if (PayrollRules.ParsePeriod(period) is not { } month)
        {
            return InvalidPeriod();
        }

        var row = await db.Timesheets.FirstOrDefaultAsync(t => t.EmployeeId == employee.Id && t.Period == month, cancellationToken);
        if (row is null || row.Status != TimesheetStatus.Closed)
        {
            return Error.Conflict(TimesheetRules.NotClosedCode, "O espelho deste período ainda não foi fechado pelo RH. A confirmação fica disponível depois do fechamento.");
        }

        if (row.EmployeeAckAt is null)
        {
            row.EmployeeAckAt = clock.GetUtcNow();
            row.EmployeeAckBy = currentUser.UserId;
            row.EmployeeAckSummary = TimesheetRules.AckSummary(FrozenTotals(row));
            if (await SaveAsync(cancellationToken) is { } conflict)
            {
                return conflict;
            }
        }

        return new TimesheetAckResult(row.Period, row.EmployeeAckAt.Value, row.EmployeeAckSummary ?? string.Empty);
    }

    /// <summary>Acordo de banco de horas (CA 2): ler com <c>hr.view_sensitive</c>.</summary>
    public async Task<Result<HourBankAgreementView>> AgreementAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (!await permissions.HasPermissionAsync("hr", PositionService.SensitiveAction, cancellationToken))
        {
            return SensitiveForbidden();
        }

        var employee = await db.Employees.AsNoTracking().Where(e => e.Id == employeeId).Select(e => new { e.HourBankAgreementOn, e.Version }).FirstOrDefaultAsync(cancellationToken);
        if (employee is null)
        {
            return Error.NotFound("employee_not_found", "Funcionário não encontrado.");
        }

        await audit.RecordReadAsync(nameof(Employee) + ".HourBank", employeeId.ToString(), cancellationToken);
        return new HourBankAgreementView(employeeId, employee.HourBankAgreementOn, (await parameters.TimesheetParametersAsync(cancellationToken)).HourBankEnabled, employee.Version);
    }

    /// <summary>
    /// "Tem acordo de banco de horas" (CA 2), com <c>hr.edit</c> + <c>hr.view_sensitive</c>: a data do acordo (nula tira o acordo). A data não
    /// pode cair num período fechado (o saldo congelado não muda), nem antes da admissão.
    /// </summary>
    public async Task<Result<HourBankAgreementView>> SetAgreementAsync(Guid employeeId, HourBankAgreementInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("hr", PositionService.SensitiveAction, cancellationToken))
        {
            return SensitiveForbidden();
        }

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        if (employee is null)
        {
            return Error.NotFound("employee_not_found", "Funcionário não encontrado.");
        }

        if (input.AgreementOn is { } on)
        {
            if (on < employee.AdmissionDate)
            {
                return PayrollRules.Field("agreementOn", "A data do acordo não pode ser antes da admissão.");
            }

            var lastClosedEnd = await db.Timesheets.AsNoTracking().Where(t => t.Status == TimesheetStatus.Closed).MaxAsync(t => (DateOnly?)t.PeriodEnd, cancellationToken);
            if (lastClosedEnd is { } end && on <= end && on != employee.HourBankAgreementOn)
            {
                return PayrollRules.Field("agreementOn", $"A data do acordo não pode cair num período de ponto já fechado (até {TimesheetRules.Date(end)}). Use uma data depois dele ou reabra o período.");
            }
        }

        if (input.Version is { } version)
        {
            db.Entry(employee).Property(e => e.Version).OriginalValue = version;
        }

        employee.HourBankAgreementOn = input.AgreementOn;
        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        return new HourBankAgreementView(employeeId, employee.HourBankAgreementOn, (await parameters.TimesheetParametersAsync(cancellationToken)).HourBankEnabled, employee.Version);
    }

    /// <summary>O lock do período na transação (CA 14).</summary>
    private async Task LockAsync(DateOnly period, CancellationToken ct)
    {
        var key = "hr.timesheet:" + period.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))) AS l").ToListAsync(ct);
    }

    private async Task<Error?> SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return Error.Conflict(TimesheetRules.ConcurrencyCode, "O espelho de ponto mudou desde que você abriu. Recarregue a página.");
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            return Error.Conflict(TimesheetRules.ConcurrencyCode, "O período foi fechado por outra pessoa ao mesmo tempo. Recarregue a página.");
        }
    }

    private static Error SensitiveForbidden() =>
        Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso ao acordo de banco de horas. Fale com o administrador.");
}
