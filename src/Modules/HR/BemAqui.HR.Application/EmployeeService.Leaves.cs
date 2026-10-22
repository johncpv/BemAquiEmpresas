using System.Globalization;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Afastamentos (CAD-04c1 e CAD-04c1b). Tipo, datas e motivo são dado sensível (LGPD art. 11): gravar e ler exigem
/// <c>hr.view_sensitive</c>, e Doença e Acidente de trabalho, com o motivo, exigem também <c>hr.view_health</c> (PO 22/10/2026).
/// A leitura vai para a auditoria (<c>SensitiveRead</c>) e nada disso entra em log nem em evento. A situação "Afastado" é
/// calculada na leitura (<see cref="EmployeeLeaveRules.Situation"/>), então o job diário só publica os eventos.
/// </summary>
public sealed partial class EmployeeService
{
    public const string LeaveOverlapCode = "leave_overlap";
    public const string LeaveNotFoundCode = "leave_not_found";
    public const string HealthAction = "view_health";

    /// <summary>Registrar afastamento (CA 1 e 7): sobreposto 409; fim antes do início 422; início hoje ou antes publica na mesma transação.</summary>
    public async Task<Result<EmployeeDetail>> RegisterLeaveAsync(Guid id, LeaveInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return LeavesForbidden();
        }

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (employee is null)
        {
            return NotFound();
        }

        if (LeaveError(input, employee) is { } invalid)
        {
            return invalid;
        }

        if (EmployeeLeaveRules.IsHealth(input.Type) && !await CanHealthAsync(cancellationToken))
        {
            return HealthForbidden();
        }

        if (employee.Status == EmployeeStatus.Terminated)
        {
            return Terminated();
        }

        var start = input.StartDate!.Value;
        var end = input.ExpectedEndDate!.Value;
        if (await OverlapsAsync(id, null, start, end, cancellationToken))
        {
            return Overlap();
        }

        var now = clock.GetUtcNow();
        var leave = new EmployeeHistoryEntry
        {
            EmployeeId = id, Kind = EmployeeHistoryKind.Leave, EffectiveDate = start, LeaveEndDate = end, LeaveType = input.Type, NewValue = string.Empty,
            Reason = input.Reason?.Trim() ?? string.Empty, UserId = currentUser.UserId, CreatedAt = now,
        };
        LeaveEvents.PublishDue(events, leave, BusinessTime.Today(clock), now);
        db.EmployeeHistory.Add(leave);
        return await SaveLeaveAsync(employee, input.Version, cancellationToken);
    }

    /// <summary>
    /// Editar afastamento previsto ou em curso (CAD-04c1b CA 1 e 2): mesmas validações do registro (datas, admissão, motivo,
    /// sobreposição sem ele mesmo, desligado e versão); tipo de saúde, antes ou depois, exige <c>hr.view_health</c>. O que já
    /// venceu na nova data sai na mesma transação, uma vez só.
    /// </summary>
    public async Task<Result<EmployeeDetail>> UpdateLeaveAsync(Guid id, Guid leaveId, LeaveInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return LeavesForbidden();
        }

        var (employee, leave, missing) = await LeaveForChangeAsync(id, leaveId, cancellationToken);
        if (missing is not null)
        {
            return missing;
        }

        if ((EmployeeLeaveRules.IsHealth(leave!.LeaveType) || EmployeeLeaveRules.IsHealth(input.Type)) && !await CanHealthAsync(cancellationToken))
        {
            return HealthForbidden();
        }

        var today = BusinessTime.Today(clock);
        var oldStart = leave.EffectiveDate;
        var oldEnd = leave.LeaveEndDate ?? leave.EffectiveDate;
        var state = EmployeeLeaveRules.State(oldStart, oldEnd, leave.CancelledAt is not null, today);
        if (state is EmployeeLeaveState.Cancelled or EmployeeLeaveState.Ended)
        {
            return EmployeeLeaveRules.EditError(state, oldStart, oldEnd, today)!;
        }

        if (LeaveError(input, employee!) is { } invalid)
        {
            return invalid;
        }

        var start = input.StartDate!.Value;
        var end = input.ExpectedEndDate!.Value;
        if (EmployeeLeaveRules.EditError(state, start, end, today) is { } rule)
        {
            return rule;
        }

        if (employee!.Status == EmployeeStatus.Terminated)
        {
            return Terminated();
        }

        var reason = input.Reason?.Trim() ?? string.Empty;
        if (start == oldStart && end == oldEnd && input.Type == leave.LeaveType && reason == leave.Reason)
        {
            return (await DetailsAsync([employee], cancellationToken))[0];
        }

        if (await OverlapsAsync(id, leave.Id, start, end, cancellationToken))
        {
            return Overlap();
        }

        var now = clock.GetUtcNow();
        db.EmployeeHistory.Add(Change(leave, EmployeeLeaveChangeKind.Edited, start, oldStart, oldEnd, start, end, leave.LeaveType, input.Type, string.Empty, now));
        leave.EffectiveDate = start;
        leave.LeaveEndDate = end;
        leave.LeaveType = input.Type;
        leave.Reason = reason;
        LeaveEvents.PublishDue(events, leave, today, now);
        return await SaveLeaveAsync(employee, input.Version, cancellationToken);
    }

    /// <summary>
    /// Encerrar antes do previsto (CAD-04c1b CA 4 e 5): o fim previsto vira o dia anterior à volta, a situação muda na hora e o
    /// <c>EmployeeLeaveEnded</c> sai nesta transação, com a marca que impede o job diário de publicar de novo.
    /// </summary>
    public async Task<Result<EmployeeDetail>> EndLeaveAsync(Guid id, Guid leaveId, LeaveEndInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return LeavesForbidden();
        }

        var (employee, leave, missing) = await LeaveForChangeAsync(id, leaveId, cancellationToken);
        if (missing is not null)
        {
            return missing;
        }

        var today = BusinessTime.Today(clock);
        var start = leave!.EffectiveDate;
        var oldEnd = leave.LeaveEndDate ?? leave.EffectiveDate;
        var state = EmployeeLeaveRules.State(start, oldEnd, leave.CancelledAt is not null, today);
        if (EmployeeLeaveRules.EndError(state, start, oldEnd, input.ReturnDate, today) is { } rule)
        {
            return rule;
        }

        var back = input.ReturnDate!.Value;
        var now = clock.GetUtcNow();
        db.EmployeeHistory.Add(Change(leave, EmployeeLeaveChangeKind.EndedEarly, back, start, oldEnd, start, back.AddDays(-1), null, null, string.Empty, now));
        leave.LeaveEndDate = back.AddDays(-1);
        LeaveEvents.PublishDue(events, leave, today, now);
        return await SaveLeaveAsync(employee!, input.Version, cancellationToken);
    }

    /// <summary>
    /// Cancelar (excluir) o afastamento que não começou (CAD-04c1b CA 6): cancelamento lógico, com motivo; nada foi publicado e o
    /// job diário passa a ignorá-lo. Afastamento já iniciado não se cancela, só se encerra.
    /// </summary>
    public async Task<Result<EmployeeDetail>> CancelLeaveAsync(Guid id, Guid leaveId, LeaveCancelInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return LeavesForbidden();
        }

        var (employee, leave, missing) = await LeaveForChangeAsync(id, leaveId, cancellationToken);
        if (missing is not null)
        {
            return missing;
        }

        var today = BusinessTime.Today(clock);
        var start = leave!.EffectiveDate;
        var end = leave.LeaveEndDate ?? leave.EffectiveDate;
        if (EmployeeLeaveRules.CancelError(EmployeeLeaveRules.State(start, end, leave.CancelledAt is not null, today), input.Reason) is { } rule)
        {
            return rule;
        }

        var now = clock.GetUtcNow();
        db.EmployeeHistory.Add(Change(leave, EmployeeLeaveChangeKind.Cancelled, today, start, end, start, end, null, null, input.Reason!.Trim(), now));
        leave.CancelledAt = now;
        return await SaveLeaveAsync(employee!, input.Version, cancellationToken);
    }

    /// <summary>
    /// Afastamentos com tipo, datas, motivo e rastro (CA 4, CAD-04c1b CA 7 e 8): a rota exige <c>hr.view_sensitive</c>; sem
    /// <c>hr.view_health</c>, o de saúde (agora ou numa versão anterior) vem só com as datas. A leitura vai para a auditoria.
    /// </summary>
    public async Task<Result<IReadOnlyList<LeaveDetail>>> LeavesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var rows = await db.EmployeeHistory.AsNoTracking().Where(h => h.EmployeeId == id && (h.Kind == EmployeeHistoryKind.Leave || h.Kind == EmployeeHistoryKind.LeaveChange))
            .ToListAsync(cancellationToken);
        var health = await CanHealthAsync(cancellationToken);
        await audit.RecordReadAsync(nameof(Employee), id.ToString(), cancellationToken);
        var today = BusinessTime.Today(clock);
        var changes = rows.Where(h => h.Kind == EmployeeHistoryKind.LeaveChange && h.LeaveId is not null).ToLookup(h => h.LeaveId!.Value);
        return rows.Where(h => h.Kind == EmployeeHistoryKind.Leave)
            .OrderByDescending(h => h.EffectiveDate).ThenByDescending(h => h.CreatedAt)
            .Select(h =>
            {
                var end = h.LeaveEndDate ?? h.EffectiveDate;
                var own = changes[h.Id].OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).ToList();
                var hidden = !health && (EmployeeLeaveRules.IsHealth(h.LeaveType) || own.Any(c => EmployeeLeaveRules.IsHealth(c.PreviousLeaveType) || EmployeeLeaveRules.IsHealth(c.LeaveType)));
                return new LeaveDetail(h.Id, hidden ? null : h.LeaveType ?? EmployeeLeaveType.Other, hidden,
                    EmployeeLeaveRules.State(h.EffectiveDate, end, h.CancelledAt is not null, today), h.EffectiveDate, end, end.AddDays(1),
                    hidden ? null : h.Reason, h.UserId, h.CreatedAt, h.CancelledAt, own.Select(c => ChangeDetail(c, hidden)).ToList());
            }).ToList();
    }

    private async Task<(Employee? Employee, EmployeeHistoryEntry? Leave, Error? Missing)> LeaveForChangeAsync(Guid id, Guid leaveId, CancellationToken ct)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (employee is null)
        {
            return (null, null, NotFound());
        }

        // O afastamento precisa ser deste funcionário (autorização pelo registro, sem IDOR).
        var leave = await db.EmployeeHistory.FirstOrDefaultAsync(h => h.Id == leaveId && h.EmployeeId == id && h.Kind == EmployeeHistoryKind.Leave, ct);
        return leave is null ? (employee, null, Error.NotFound(LeaveNotFoundCode, "Afastamento não encontrado. Recarregue a linha do tempo.")) : (employee, leave, null);
    }

    /// <summary>Sobreposição com outro afastamento não cancelado do funcionário (CA 1); <paramref name="except"/> é o que está sendo editado.</summary>
    private Task<bool> OverlapsAsync(Guid id, Guid? except, DateOnly start, DateOnly end, CancellationToken ct) =>
        db.EmployeeHistory.AnyAsync(h => h.EmployeeId == id && h.Kind == EmployeeHistoryKind.Leave && h.CancelledAt == null && h.Id != except
            && h.EffectiveDate <= end && h.LeaveEndDate >= start, ct);

    /// <summary>Toda gravação de afastamento passa pelo <c>xmin</c> do funcionário: duas mudanças simultâneas dão 409 (ADR 0019 §2).</summary>
    private async Task<Result<EmployeeDetail>> SaveLeaveAsync(Employee employee, uint version, CancellationToken ct)
    {
        db.Entry(employee).Property(e => e.Version).OriginalValue = version;
        db.Entry(employee).State = EntityState.Modified;
        await db.SaveChangesAsync(ct);
        return (await DetailsAsync([employee], ct))[0];
    }

    private EmployeeHistoryEntry Change(EmployeeHistoryEntry leave, EmployeeLeaveChangeKind kind, DateOnly effective, DateOnly oldStart, DateOnly oldEnd, DateOnly newStart, DateOnly newEnd,
        EmployeeLeaveType? oldType, EmployeeLeaveType? newType, string reason, DateTimeOffset now) => new()
    {
        EmployeeId = leave.EmployeeId, Kind = EmployeeHistoryKind.LeaveChange, LeaveId = leave.Id, LeaveChange = kind, EffectiveDate = effective,
        OldValue = Period(oldStart, oldEnd), NewValue = Period(newStart, newEnd), PreviousLeaveType = oldType, LeaveType = newType, Reason = reason,
        UserId = currentUser.UserId, CreatedAt = now,
    };

    private static LeaveChangeDetail ChangeDetail(EmployeeHistoryEntry c, bool hidden)
    {
        var (oldStart, oldEnd) = ParsePeriod(c.OldValue ?? c.NewValue);
        var (newStart, newEnd) = ParsePeriod(c.NewValue);
        return new LeaveChangeDetail(c.Id, c.LeaveChange ?? EmployeeLeaveChangeKind.Edited, oldStart, oldEnd, newStart, newEnd,
            hidden ? null : c.PreviousLeaveType, hidden ? null : c.LeaveType, hidden || string.IsNullOrEmpty(c.Reason) ? null : c.Reason, c.UserId, c.CreatedAt);
    }

    /// <summary>Período do rastro como texto "aaaa-mm-dd/aaaa-mm-dd" nas colunas de antes e depois (já <c>[PersonalData]</c>).</summary>
    private static string Period(DateOnly start, DateOnly end) =>
        start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "/" + end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static (DateOnly Start, DateOnly End) ParsePeriod(string value)
    {
        var parts = value.Split('/');
        return (DateOnly.ParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture), DateOnly.ParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static Error LeavesForbidden() => Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso aos afastamentos. Fale com o administrador.");

    private static Error HealthForbidden() =>
        Error.Forbidden(Error.Codes.Forbidden, "Afastamento por doença ou acidente de trabalho é dado de saúde: só quem tem a permissão Ver dados de saúde registra ou altera. Fale com o administrador.");

    private static Error Terminated() => Error.Conflict("employee_terminated", "Funcionário desligado não recebe nem altera afastamento.");

    private static Error Overlap() => Error.Conflict(LeaveOverlapCode, "Já existe um afastamento deste funcionário nesse período. Confira as datas na linha do tempo.");

    private static Error? LeaveError(LeaveInput input, Employee employee)
    {
        if (input.Type is null || !Enum.IsDefined(input.Type.Value))
        {
            return PositionService.Invalid("type", "Escolha o tipo do afastamento.");
        }

        if (input.StartDate is null)
        {
            return PositionService.Invalid("startDate", "Informe o início do afastamento.");
        }

        if (input.ExpectedEndDate is null)
        {
            return PositionService.Invalid("expectedEndDate", "Informe o fim previsto do afastamento.");
        }

        if (input.ExpectedEndDate < input.StartDate)
        {
            return PositionService.Invalid("expectedEndDate", "O fim previsto precisa ser igual ou depois do início.");
        }

        if (input.StartDate < employee.AdmissionDate)
        {
            return PositionService.Invalid("startDate", "O início precisa ser igual ou depois da admissão.");
        }

        return input.Reason is { } reason && reason.Trim().Length > EmployeeLeaveRules.ReasonMaxLength
            ? PositionService.Invalid("reason", "O motivo aceita até 200 caracteres. Não informe CID nem diagnóstico.")
            : null;
    }
}

/// <summary>
/// Eventos devidos no dia (CA 6 e 7), só com o id do funcionário e as datas: começou (início ≤ hoje) e terminou (fim previsto antes
/// de hoje, ou seja, no dia seguinte ao fim). A marca na entrada faz cada evento sair uma vez só; usado no registro, na edição, no
/// encerramento antes do previsto (CAD-04c1b) e no job diário. Cancelado nunca publica.
/// </summary>
public static class LeaveEvents
{
    public static bool PublishDue(IEventPublisher events, EmployeeHistoryEntry leave, DateOnly today, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(leave);
        if (leave.CancelledAt is not null)
        {
            return false;
        }

        var published = false;
        var end = leave.LeaveEndDate ?? leave.EffectiveDate;
        if (leave.StartPublishedAt is null && leave.EffectiveDate <= today)
        {
            events.Publish(new EmployeeLeaveStarted(leave.EmployeeId, leave.EffectiveDate, end));
            leave.StartPublishedAt = now;
            published = true;
        }

        if (leave.StartPublishedAt is not null && leave.EndPublishedAt is null && end < today)
        {
            events.Publish(new EmployeeLeaveEnded(leave.EmployeeId, leave.EffectiveDate, end));
            leave.EndPublishedAt = now;
            published = true;
        }

        return published;
    }
}
