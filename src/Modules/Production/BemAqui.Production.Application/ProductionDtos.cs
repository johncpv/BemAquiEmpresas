using BemAqui.Production.Contracts;

namespace BemAqui.Production.Application;

/// <summary>Criação e edição de centro (CA 12): as horas por dia saem dos turnos, nunca do corpo.</summary>
public sealed record WorkCenterInput(string Name, string? Line, IReadOnlyList<Shift> Shifts);

public sealed record WorkCenterDetail(Guid Id, string Name, string? Line, IReadOnlyList<Shift> Shifts, decimal HoursPerDay, Guid CalendarId, bool IsActive, DateTimeOffset CreatedAt);

public sealed record WorkCenterListQuery(string? Q, bool IncludeInactive);

public sealed record CalendarDetail(Guid Id, string Name, bool IsDefault, IReadOnlyList<Weekday> WorkingDays);

public sealed record CalendarInput(IReadOnlyList<Weekday> WorkingDays);

public sealed record CalendarExceptionInput(DateOnly Date, CalendarExceptionType Type, decimal HoursAvailable, string Description);

public sealed record CalendarExceptionDetail(Guid Id, Guid CalendarId, DateOnly Date, CalendarExceptionType Type, decimal HoursAvailable, string Description);

/// <summary>Feriado sugerido (CA 10): <c>Optional</c> vem desmarcado; <c>AlreadyAdded</c> quando a data já tem exceção.</summary>
public sealed record HolidaySuggestion(DateOnly Date, string Name, bool Optional, bool AlreadyAdded);

public sealed record HolidayToAdd(DateOnly Date, string Description);

/// <summary>Os feriados que a pessoa confirmou em "Adicionar feriados" (CA 10).</summary>
public sealed record AddHolidaysInput(IReadOnlyList<HolidayToAdd> Holidays);

public sealed record AddHolidaysResult(int Added, int Skipped);
