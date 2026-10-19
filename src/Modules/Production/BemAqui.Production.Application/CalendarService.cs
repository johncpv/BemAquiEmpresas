using System.Globalization;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Calendar = BemAqui.Production.Domain.Calendar;

namespace BemAqui.Production.Application;

/// <summary>
/// Calendário padrão do tenant e as exceções (CAD-03 CA 8 a 11). Toda rota de exceção confere que ela é do calendário da URL
/// (IDOR pai/filho, CA 11); a sugestão de feriados nacionais não grava nada sem "Adicionar feriados" (CA 10, R-PRP-7).
/// </summary>
public sealed class CalendarService(IProductionDb db, TimeProvider clock)
{
    public const string DateIndex = "ix_calendar_exceptions_date";
    public const string DefaultName = "Calendário padrão";
    public const int MaxHolidaysPerRequest = 40;

    public static readonly IReadOnlyList<Weekday> DefaultWorkingDays = [Weekday.Monday, Weekday.Tuesday, Weekday.Wednesday, Weekday.Thursday, Weekday.Friday];

    /// <summary>O calendário padrão; cria o de seg a sex se o seed ainda não rodou neste tenant.</summary>
    public async Task<Calendar> DefaultEntityAsync(CancellationToken cancellationToken = default)
    {
        var calendar = await db.Calendars.FirstOrDefaultAsync(c => c.IsDefault, cancellationToken);
        if (calendar is not null)
        {
            return calendar;
        }

        calendar = new Calendar { Name = DefaultName, IsDefault = true, WorkingDays = [.. DefaultWorkingDays], CreatedAt = clock.GetUtcNow() };
        db.Calendars.Add(calendar);
        await db.SaveChangesAsync(cancellationToken);
        return calendar;
    }

    public async Task<CalendarDetail> GetDefaultAsync(CancellationToken cancellationToken = default) => ToDetail(await DefaultEntityAsync(cancellationToken));

    public async Task<Result<CalendarDetail>> UpdateAsync(Guid id, CalendarInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var calendar = await db.Calendars.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (calendar is null)
        {
            return CalendarNotFound();
        }

        var days = (input.WorkingDays ?? []).Distinct().Where(Enum.IsDefined).OrderBy(d => ((int)d + 6) % 7).ToList();
        if (days.Count == 0)
        {
            return WorkCenterService.Invalid("workingDays", "Marque ao menos um dia útil.");
        }

        calendar.WorkingDays = days;
        await db.SaveChangesAsync(cancellationToken);
        return ToDetail(calendar);
    }

    public async Task<Result<IReadOnlyList<CalendarExceptionDetail>>> ListExceptionsAsync(Guid calendarId, int year, CancellationToken cancellationToken = default)
    {
        if (!await db.Calendars.AnyAsync(c => c.Id == calendarId, cancellationToken))
        {
            return CalendarNotFound();
        }

        var from = new DateOnly(Math.Clamp(year, 1900, 2200), 1, 1);
        var to = from.AddYears(1);
        var items = await db.CalendarExceptions.AsNoTracking().Where(e => e.CalendarId == calendarId && e.Date >= from && e.Date < to)
            .OrderBy(e => e.Date).ToListAsync(cancellationToken);
        return items.Select(ToDetail).ToList();
    }

    public async Task<Result<CalendarExceptionDetail>> CreateExceptionAsync(Guid calendarId, CalendarExceptionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await db.Calendars.AnyAsync(c => c.Id == calendarId, cancellationToken))
        {
            return CalendarNotFound();
        }

        if (Validate(input) is { } invalid)
        {
            return invalid;
        }

        var exception = new CalendarExceptionDay { CalendarId = calendarId, CreatedAt = clock.GetUtcNow() };
        Apply(exception, input);
        db.CalendarExceptions.Add(exception);
        return await SaveAsync(exception, cancellationToken);
    }

    public async Task<Result<CalendarExceptionDetail>> UpdateExceptionAsync(Guid calendarId, Guid exceptionId, CalendarExceptionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var exception = await db.CalendarExceptions.FirstOrDefaultAsync(e => e.Id == exceptionId && e.CalendarId == calendarId, cancellationToken);
        if (exception is null)
        {
            return ExceptionNotFound();
        }

        if (Validate(input) is { } invalid)
        {
            return invalid;
        }

        Apply(exception, input);
        return await SaveAsync(exception, cancellationToken);
    }

    public async Task<Result> DeleteExceptionAsync(Guid calendarId, Guid exceptionId, CancellationToken cancellationToken = default)
    {
        var exception = await db.CalendarExceptions.FirstOrDefaultAsync(e => e.Id == exceptionId && e.CalendarId == calendarId, cancellationToken);
        if (exception is null)
        {
            return ExceptionNotFound();
        }

        db.CalendarExceptions.Remove(exception);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Feriados nacionais do ano para conferir (CA 10); marca os que já têm exceção na data. Não grava nada.</summary>
    public async Task<Result<IReadOnlyList<HolidaySuggestion>>> SuggestHolidaysAsync(Guid calendarId, int year, CancellationToken cancellationToken = default)
    {
        if (!await db.Calendars.AnyAsync(c => c.Id == calendarId, cancellationToken))
        {
            return CalendarNotFound();
        }

        if (year is < 2000 or > 2100)
        {
            return WorkCenterService.Invalid("year", "Informe um ano entre 2000 e 2100.");
        }

        var holidays = NationalHolidays.For(year);
        var dates = holidays.Select(h => h.Date).ToList();
        var taken = (await db.CalendarExceptions.AsNoTracking().Where(e => e.CalendarId == calendarId && dates.Contains(e.Date)).Select(e => e.Date).ToListAsync(cancellationToken)).ToHashSet();
        return holidays.Select(h => new HolidaySuggestion(h.Date, h.Name, h.Optional, taken.Contains(h.Date))).ToList();
    }

    /// <summary>"Adicionar feriados" (CA 10): grava os confirmados como feriado de 0 h; datas que já têm exceção ficam como estão.</summary>
    public async Task<Result<AddHolidaysResult>> AddHolidaysAsync(Guid calendarId, AddHolidaysInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await db.Calendars.AnyAsync(c => c.Id == calendarId, cancellationToken))
        {
            return CalendarNotFound();
        }

        var wanted = (input.Holidays ?? []).ToList();
        if (wanted.Count == 0 || wanted.Count > MaxHolidaysPerRequest)
        {
            return WorkCenterService.Invalid("holidays", $"Escolha de 1 a {MaxHolidaysPerRequest} feriados.");
        }

        foreach (var holiday in wanted)
        {
            if (Validate(new CalendarExceptionInput(holiday.Date, CalendarExceptionType.Holiday, 0, holiday.Description)) is { } invalid)
            {
                return invalid;
            }
        }

        var dates = wanted.Select(h => h.Date).ToList();
        var taken = (await db.CalendarExceptions.Where(e => e.CalendarId == calendarId && dates.Contains(e.Date)).Select(e => e.Date).ToListAsync(cancellationToken)).ToHashSet();
        var added = 0;
        foreach (var holiday in wanted.DistinctBy(h => h.Date).Where(h => !taken.Contains(h.Date)))
        {
            db.CalendarExceptions.Add(new CalendarExceptionDay
            {
                CalendarId = calendarId, Date = holiday.Date, Type = CalendarExceptionType.Holiday, HoursAvailable = 0, Description = holiday.Description.Trim(), CreatedAt = clock.GetUtcNow(),
            });
            added++;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, DateIndex))
        {
            return Error.Conflict("calendar_exception_race", "Outra pessoa acabou de cadastrar uma destas datas. Atualize a tela e tente de novo.");
        }

        return new AddHolidaysResult(added, wanted.Count - added);
    }

    private static Error? Validate(CalendarExceptionInput input)
    {
        if (input.Date.Year is < 2000 or > 2100)
        {
            return WorkCenterService.Invalid("date", "Informe uma data entre 2000 e 2100.");
        }

        if (!Enum.IsDefined(input.Type))
        {
            return WorkCenterService.Invalid("type", "Escolha Feriado ou Parada.");
        }

        if (input.HoursAvailable is < 0 or > 24 || decimal.Round(input.HoursAvailable, 2) != input.HoursAvailable)
        {
            return WorkCenterService.Invalid("hoursAvailable", "Informe as horas disponíveis entre 0 e 24, com até 2 casas.");
        }

        if (string.IsNullOrWhiteSpace(input.Description) || input.Description.Trim().Length > 100)
        {
            return WorkCenterService.Invalid("description", "Informe a descrição, com até 100 caracteres.");
        }

        return null;
    }

    private static void Apply(CalendarExceptionDay exception, CalendarExceptionInput input)
    {
        exception.Date = input.Date;
        exception.Type = input.Type;
        exception.HoursAvailable = input.HoursAvailable;
        exception.Description = input.Description.Trim();
    }

    private async Task<Result<CalendarExceptionDetail>> SaveAsync(CalendarExceptionDay exception, CancellationToken cancellationToken)
    {
        if (await db.CalendarExceptions.AnyAsync(e => e.CalendarId == exception.CalendarId && e.Date == exception.Date && e.Id != exception.Id, cancellationToken))
        {
            return Duplicate(exception.Date);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, DateIndex))
        {
            return Duplicate(exception.Date);
        }

        return ToDetail(exception);
    }

    private static Error Duplicate(DateOnly date) =>
        Error.Conflict("calendar_exception_date_taken", $"Já existe uma exceção em {date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}.");

    public static CalendarDetail ToDetail(Calendar c) => new(c.Id, c.Name, c.IsDefault, c.WorkingDays);

    public static CalendarExceptionDetail ToDetail(CalendarExceptionDay e) => new(e.Id, e.CalendarId, e.Date, e.Type, e.HoursAvailable, e.Description);

    private static Error CalendarNotFound() => Error.NotFound("calendar_not_found", "Calendário não encontrado.");

    private static Error ExceptionNotFound() => Error.NotFound("calendar_exception_not_found", "Exceção não encontrada.");
}
