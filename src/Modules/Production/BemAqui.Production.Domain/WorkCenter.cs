using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Production.Domain;

/// <summary>
/// Centro de trabalho (<c>production.work_centers</c>, CAD-03, R-PLN-1): turnos em jsonb e horas por dia sempre derivadas
/// deles; usa o calendário padrão do tenant (<c>calendar_id</c> pronto para calendário por centro no futuro).
/// </summary>
public sealed class WorkCenter : Entity, IAuditable, ISoftDeletable
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Linha de produção a que o centro pertence (ex.: "Cabines"); opcional.</summary>
    public string? Line { get; set; }

    public List<Shift> Shifts { get; set; } = [];

    public decimal HoursPerDay { get; private set; }

    public Guid CalendarId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>Troca os turnos e recalcula as horas por dia (CA 12). Os turnos precisam ter passado por <see cref="ShiftMath.Validate"/>.</summary>
    public void SetShifts(IReadOnlyList<Shift> shifts)
    {
        ArgumentNullException.ThrowIfNull(shifts);
        Shifts = shifts.Select(ShiftMath.Normalize).OrderBy(s => s.Start, StringComparer.Ordinal).ToList();
        HoursPerDay = ShiftMath.HoursPerDay(Shifts);
    }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }
}

/// <summary>Calendário (<c>production.calendars</c>): um padrão por tenant no MVP, com os dias úteis da semana.</summary>
public sealed class Calendar : Entity, IAuditable
{
    public string Name { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    public List<Weekday> WorkingDays { get; set; } = [];

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Feriado ou parada (<c>production.calendar_exceptions</c>): uma por calendário e data; 0 h = dia parado.</summary>
public sealed class CalendarExceptionDay : Entity, IAuditable
{
    public Guid CalendarId { get; init; }

    public DateOnly Date { get; set; }

    public CalendarExceptionType Type { get; set; }

    public decimal HoursAvailable { get; set; }

    public string Description { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }
}
