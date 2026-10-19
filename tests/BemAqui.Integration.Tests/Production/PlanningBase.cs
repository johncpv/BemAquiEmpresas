using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// Fixture "planejamento-base" (CAD-03 CA 17), reaproveitada pelos testes do PLN-01: um calendário seg–sex próprio (não o
/// padrão, para não colidir com outros testes do tenant) com o feriado de 20/11/2026 (sexta, 0 h) e a parada parcial de
/// 18/11/2026 (4 h), e dois centros criados em 01/01/2026: Corte (8,00 h) e Montagem (8,80 h).
/// </summary>
public static class PlanningBase
{
    public sealed record Ids(Guid CalendarId, Guid Cut, Guid Assembly);

    public static readonly DateOnly Holiday = new(2026, 11, 20);
    public static readonly DateOnly PartialStop = new(2026, 11, 18);

    public static async Task<Ids> SeedAsync(IServiceProvider tenantScope)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IProductionDb>();
        var created = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var calendar = new Calendar { Name = "Planejamento-base " + suffix, WorkingDays = [.. CalendarService.DefaultWorkingDays], CreatedAt = created };
        db.Calendars.Add(calendar);
        db.CalendarExceptions.Add(new CalendarExceptionDay { CalendarId = calendar.Id, Date = Holiday, Type = CalendarExceptionType.Holiday, HoursAvailable = 0, Description = "Consciência Negra", CreatedAt = created });
        db.CalendarExceptions.Add(new CalendarExceptionDay { CalendarId = calendar.Id, Date = PartialStop, Type = CalendarExceptionType.Stoppage, HoursAvailable = 4, Description = "Manutenção da ponte rolante", CreatedAt = created });
        var cut = new WorkCenter { Name = "Corte " + suffix, Line = "Cabines", CalendarId = calendar.Id, CreatedAt = created };
        cut.SetShifts([new Shift("07:00", "11:00"), new Shift("12:00", "16:00")]);
        var assembly = new WorkCenter { Name = "Montagem " + suffix, Line = "Cabines", CalendarId = calendar.Id, CreatedAt = created };
        assembly.SetShifts([new Shift("07:00", "11:00"), new Shift("12:00", "16:48")]);
        db.WorkCenters.AddRange(cut, assembly);
        await db.SaveChangesAsync();
        return new Ids(calendar.Id, cut.Id, assembly.Id);
    }
}
