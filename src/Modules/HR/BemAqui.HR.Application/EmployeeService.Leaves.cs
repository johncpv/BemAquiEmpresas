using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Afastamentos (CAD-04c1). Tipo, datas e motivo são dado sensível (LGPD art. 11): gravar e ler exigem <c>hr.view_sensitive</c>,
/// a leitura vai para a auditoria (<c>SensitiveRead</c>) e nada disso entra em log nem em evento. A situação "Afastado" é calculada
/// na leitura (<see cref="EmployeeLeaveRules.Situation"/>), então o job diário só publica os eventos.
/// </summary>
public sealed partial class EmployeeService
{
    public const string LeaveOverlapCode = "leave_overlap";

    /// <summary>Registrar afastamento (CA 1 e 7): sobreposto 409; fim antes do início 422; início hoje ou antes publica na mesma transação.</summary>
    public async Task<Result<EmployeeDetail>> RegisterLeaveAsync(Guid id, LeaveInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso aos afastamentos. Fale com o administrador.");
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

        if (employee.Status == EmployeeStatus.Terminated)
        {
            return Error.Conflict("employee_terminated", "Funcionário desligado não recebe afastamento.");
        }

        var start = input.StartDate!.Value;
        var end = input.ExpectedEndDate!.Value;
        var overlaps = await db.EmployeeHistory.AnyAsync(h => h.EmployeeId == id && h.Kind == EmployeeHistoryKind.Leave && h.EffectiveDate <= end && h.LeaveEndDate >= start, cancellationToken);
        if (overlaps)
        {
            return Error.Conflict(LeaveOverlapCode, "Já existe um afastamento deste funcionário nesse período. Confira as datas na linha do tempo.");
        }

        var now = clock.GetUtcNow();
        var leave = new EmployeeHistoryEntry
        {
            EmployeeId = id, Kind = EmployeeHistoryKind.Leave, EffectiveDate = start, LeaveEndDate = end, LeaveType = input.Type, NewValue = string.Empty,
            Reason = input.Reason?.Trim() ?? string.Empty, UserId = currentUser.UserId, CreatedAt = now,
        };
        LeaveEvents.PublishDue(events, leave, BusinessTime.Today(clock), now);
        db.EmployeeHistory.Add(leave);
        // Toda gravação passa pelo xmin do funcionário: dois registros simultâneos não criam afastamentos sobrepostos (ADR 0019 §2).
        db.Entry(employee).Property(e => e.Version).OriginalValue = input.Version;
        db.Entry(employee).State = EntityState.Modified;
        await db.SaveChangesAsync(cancellationToken);
        return (await DetailsAsync([employee], cancellationToken))[0];
    }

    /// <summary>Afastamentos com tipo, datas e motivo (CA 4): a rota exige <c>hr.view_sensitive</c>; a leitura vai para a auditoria.</summary>
    public async Task<Result<IReadOnlyList<LeaveDetail>>> LeavesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var leaves = await db.EmployeeHistory.AsNoTracking().Where(h => h.EmployeeId == id && h.Kind == EmployeeHistoryKind.Leave)
            .OrderByDescending(h => h.EffectiveDate).ThenByDescending(h => h.CreatedAt).ToListAsync(cancellationToken);
        await audit.RecordReadAsync(nameof(Employee), id.ToString(), cancellationToken);
        return leaves.Select(h =>
        {
            var end = h.LeaveEndDate ?? h.EffectiveDate;
            return new LeaveDetail(h.Id, h.LeaveType ?? EmployeeLeaveType.Other, h.EffectiveDate, end, end.AddDays(1), h.Reason, h.UserId, h.CreatedAt);
        }).ToList();
    }

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
/// de hoje, ou seja, no dia seguinte ao fim). A marca na entrada faz cada evento sair uma vez só; usado no registro e no job diário.
/// </summary>
public static class LeaveEvents
{
    public static bool PublishDue(IEventPublisher events, EmployeeHistoryEntry leave, DateOnly today, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(leave);
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
