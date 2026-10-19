using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>Jornadas (CAD-04a CA 15 e 16): horas semanais derivadas dos dias; em uso por cargo, só inativa.</summary>
public sealed class ScheduleService(IHrDb db, TimeProvider clock)
{
    public async Task<IReadOnlyList<ScheduleDetail>> ListAsync(CancellationToken cancellationToken = default) =>
        (await db.WorkSchedules.AsNoTracking().OrderBy(s => s.Name).ToListAsync(cancellationToken)).Select(ToDetail).ToList();

    public async Task<Result<ScheduleDetail>> CreateAsync(ScheduleInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var schedule = new WorkSchedule { CreatedAt = clock.GetUtcNow() };
        if (await ApplyAsync(schedule, input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        db.WorkSchedules.Add(schedule);
        await db.SaveChangesAsync(cancellationToken);
        return ToDetail(schedule);
    }

    public async Task<Result<ScheduleDetail>> UpdateAsync(Guid id, ScheduleInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var schedule = await db.WorkSchedules.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (schedule is null)
        {
            return NotFound();
        }

        if (await ApplyAsync(schedule, input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ToDetail(schedule);
    }

    public async Task<Result<ScheduleDetail>> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var schedule = await db.WorkSchedules.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (schedule is null)
        {
            return NotFound();
        }

        schedule.IsActive = active;
        await db.SaveChangesAsync(cancellationToken);
        return ToDetail(schedule);
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var schedule = await db.WorkSchedules.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (schedule is null)
        {
            return NotFound();
        }

        if (await db.JobPositions.AnyAsync(p => p.WorkScheduleId == id, cancellationToken))
        {
            return Error.Conflict("schedule_in_use", "A jornada está em uso por cargos. Inative-a.");
        }

        db.WorkSchedules.Remove(schedule);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Error?> ApplyAsync(WorkSchedule schedule, ScheduleInput input, CancellationToken ct)
    {
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > 100)
        {
            return PositionService.Invalid("name", "Informe o nome da jornada, com até 100 caracteres.");
        }

        if (ScheduleMath.Validate(input.Days) is { } message)
        {
            return PositionService.Invalid("days", message);
        }

        if (await db.WorkSchedules.AnyAsync(s => s.Id != schedule.Id && s.Name == name, ct))
        {
            return Error.Conflict("schedule_name_taken", "Já existe a jornada " + name + ".");
        }

        schedule.Name = name;
        schedule.SetDays(input.Days.Select(d => d with { Start = d.Start.Trim(), End = d.End.Trim() }).ToList());
        return null;
    }

    public static ScheduleDetail ToDetail(WorkSchedule s) => new(s.Id, s.Name, s.Days, s.WeeklyHours, ScheduleMath.MonthlyHours(s.WeeklyHours), s.IsActive, s.SeedKey);

    private static Error NotFound() => Error.NotFound("schedule_not_found", "Jornada não encontrada.");
}
