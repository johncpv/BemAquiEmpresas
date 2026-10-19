using System.Net;
using System.Net.Http.Json;
using BemAqui.Production.Application;
using BemAqui.Production.Domain;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Production;

public partial class ProductionTests
{
    [Fact]
    public async Task Calendario_padrao_seg_a_sex_excecao_com_data_repetida_e_idor()
    {
        var supervisor = await ActorAsync("engineering");
        var calendar = await JsonAsync(await supervisor.GetAsync("/api/v1/production/calendars/default"));
        var id = calendar.GetProperty("id").GetGuid();
        calendar.GetProperty("workingDays").EnumerateArray().Select(d => d.GetString()).Should().Equal("Monday", "Tuesday", "Wednesday", "Thursday", "Friday");

        // Data única por teste: o calendário padrão é do tenant inteiro.
        var date = new DateOnly(2031, 1, 1).AddDays(Random.Shared.Next(0, 3000));
        var created = await supervisor.PostAsJsonAsync($"/api/v1/production/calendars/{id}/exceptions", new { date, type = "Stoppage", hoursAvailable = 4m, description = "Inventário anual" });
        var exceptionId = await CreatedAsync(created);
        var again = await supervisor.PostAsJsonAsync($"/api/v1/production/calendars/{id}/exceptions", new { date, type = "Holiday", hoursAvailable = 0m, description = "Repetida" });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict, "uma exceção por data no calendário (CA 9)");
        var hours = await supervisor.PostAsJsonAsync($"/api/v1/production/calendars/{id}/exceptions", new { date = date.AddDays(1), type = "Stoppage", hoursAvailable = 25m, description = "Horas demais" });
        hours.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await supervisor.GetFromJsonAsync<CalendarExceptionDetail[]>($"/api/v1/production/calendars/{id}/exceptions?year={date.Year}")).Should().Contain(e => e.Id == exceptionId);

        Guid other;
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IProductionDb>();
            var calendarB = new Calendar { Name = "Outro " + Guid.NewGuid().ToString("N")[..6], WorkingDays = [BemAqui.Production.Contracts.Weekday.Monday], CreatedAt = DateTimeOffset.UtcNow };
            db.Calendars.Add(calendarB);
            await db.SaveChangesAsync();
            other = calendarB.Id;
        }

        (await supervisor.PutAsJsonAsync($"/api/v1/production/calendars/{other}/exceptions/{exceptionId}", new { date, type = "Holiday", hoursAvailable = 0m, description = "Invasão" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "exceção de outro calendário (CA 11)");
        (await supervisor.DeleteAsync($"/api/v1/production/calendars/{other}/exceptions/{exceptionId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await supervisor.DeleteAsync($"/api/v1/production/calendars/{id}/exceptions/{exceptionId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Sugerir_feriados_nao_grava_e_adicionar_grava_so_os_confirmados()
    {
        var supervisor = await ActorAsync("production_supervision");
        var id = (await JsonAsync(await supervisor.GetAsync("/api/v1/production/calendars/default"))).GetProperty("id").GetGuid();
        const int year = 2090;

        var suggested = await supervisor.GetFromJsonAsync<HolidaySuggestion[]>($"/api/v1/production/calendars/{id}/holidays/{year}");
        suggested.Should().Contain(h => h.Name == "Sexta-feira Santa" && !h.Optional).And.Contain(h => h.Name == "Corpus Christi" && h.Optional);
        (await supervisor.GetFromJsonAsync<CalendarExceptionDetail[]>($"/api/v1/production/calendars/{id}/exceptions?year={year}")).Should().BeEmpty("sugerir não grava nada (CA 10)");

        var chosen = suggested!.Where(h => !h.Optional).Select(h => new { date = h.Date, description = h.Name }).ToArray();
        var added = await JsonAsync(await supervisor.PostAsJsonAsync($"/api/v1/production/calendars/{id}/holidays", new { holidays = chosen }));
        added.GetProperty("added").GetInt32().Should().Be(10);
        var twice = await JsonAsync(await supervisor.PostAsJsonAsync($"/api/v1/production/calendars/{id}/holidays", new { holidays = chosen }));
        twice.GetProperty("skipped").GetInt32().Should().Be(10, "data que já tem exceção fica como está");
        (await supervisor.GetFromJsonAsync<HolidaySuggestion[]>($"/api/v1/production/calendars/{id}/holidays/{year}")).Should().Contain(h => h.Name == "Natal" && h.AlreadyAdded);

        var viewer = await ActorAsync("view:production");
        (await viewer.PostAsJsonAsync($"/api/v1/production/calendars/{id}/holidays", new { holidays = chosen })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
