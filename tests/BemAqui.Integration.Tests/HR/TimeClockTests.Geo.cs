using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.HR.Domain;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.HR;

public partial class TimeClockTests
{
    private Task<List<AuditEntry>> AuditAsync(string entity, string? id = null) =>
        InTenantAsync(sp => sp.GetRequiredService<BemAquiDbContext>().AuditEntries.AsNoTracking().Where(a => a.Entity == entity && (id == null || a.EntityId == id)).ToListAsync());

    /// <summary>
    /// CA 9 a 12 e 16: geolocalização desligada (não grava), ligada sem a ciência (sem localização), a ciência registrada (e o parâmetro
    /// auditado), com a permissão (4 casas e a precisão), negada ("Sem localização"); a localização só sai pela rota /sensitive com
    /// hr.view_sensitive, SensitiveRead e no-store, e para o próprio funcionário; canário: as coordenadas nunca na auditoria nem no log.
    /// </summary>
    [Fact]
    public async Task Geolocalizacao_ligada_com_ciencia_reduzida_e_so_pela_rota_sensitive()
    {
        var ana = await PersonAsync();
        var coords = new { latitude = -23.550520m, longitude = -46.633308m, accuracyM = 18.6m };
        (await ana.Client.SendAsync(Mark(new { type = "In", coords.latitude, coords.longitude, coords.accuracyM }))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await EntriesOfAsync(ana.EmployeeId)).Single().Should().Match<TimeEntry>(e => e.Location == TimeEntryLocation.NotAsked && e.Latitude == null, "desligada, a localização enviada é ignorada");

        await SetGeolocationAsync("Ask");
        try
        {
            (await AuditAsync("HrParameter")).Should().Contain(a => a.Changes.Contains("Ask"), "mudança do parâmetro auditada (CA 16)");
            var clock = await JsonAsync(await ana.Client.GetAsync(Today));
            clock.GetProperty("geolocation").GetString().Should().Be("Ask");
            clock.GetProperty("geoConsentNeeded").GetBoolean().Should().BeTrue();
            clock.GetProperty("geoConsentText").GetString().Should().Be("Para que serve a localização: registrar onde a marcação foi feita, como indício. Só o RH com permissão vê. Você pode negar.");

            // Sem a ciência, a localização não grava (e a marcação entra do mesmo jeito).
            var noConsent = await JsonAsync(await ana.Client.SendAsync(Mark(new { type = "BreakStart", coords.latitude, coords.longitude, coords.accuracyM })));
            noConsent.GetProperty("location").GetString().Should().Be("Missing");

            var consent = await ana.Client.PostAsync($"{Entries}/geo-consent", null);
            consent.StatusCode.Should().Be(HttpStatusCode.OK, await consent.Content.ReadAsStringAsync());
            (await ana.Client.PostAsync($"{Entries}/geo-consent", null)).StatusCode.Should().Be(HttpStatusCode.OK, "a mesma versão não duplica");
            var consents = await DbAsync(db => db.TimeClockGeoConsents.AsNoTracking().Where(c => c.EmployeeId == ana.EmployeeId).ToListAsync());
            consents.Should().ContainSingle().Which.Should().Match<TimeClockGeoConsent>(c => c.TextVersion == TimeClockRules.GeoConsentVersion && c.UserId == ana.UserId);
            (await AuditAsync(nameof(TimeClockGeoConsent), consents[0].Id.ToString())).Should().ContainSingle(a => a.Action == AuditActions.Created && a.UserId == ana.UserId);
            (await JsonAsync(await ana.Client.GetAsync(Today))).GetProperty("geoConsentNeeded").GetBoolean().Should().BeFalse();

            var captured = await ana.Client.SendAsync(Mark(new { type = "BreakEnd", coords.latitude, coords.longitude, coords.accuracyM }));
            captured.StatusCode.Should().Be(HttpStatusCode.Created);
            var capturedText = await captured.Content.ReadAsStringAsync();
            capturedText.Should().Contain("\"location\":\"Captured\"").And.NotContain("23.55", "o comprovante não leva a localização");
            var denied = await JsonAsync(await ana.Client.SendAsync(Mark(new { type = "Out" })));
            denied.GetProperty("location").GetString().Should().Be("Missing", "negada: sem localização, nunca bloqueia");

            var rows = await EntriesOfAsync(ana.EmployeeId);
            var withLocation = rows.Single(e => e.Type == TimeEntryType.BreakEnd);
            withLocation.Latitude.Should().Be(-23.5505m);
            withLocation.Longitude.Should().Be(-46.6333m);
            withLocation.AccuracyM.Should().Be(19);
            withLocation.Location.Should().Be(TimeEntryLocation.Captured);

            // RH com hr.view: sem localização no JSON; com hr.view_sensitive, pela rota /sensitive.
            var hr = await PersonAsync("hr:view", employee: false);
            var listed = await (await hr.Client.GetAsync($"/api/v1/hr/employees/{ana.EmployeeId}/time-entries")).Content.ReadAsStringAsync();
            listed.Should().Contain("\"Missing\"").And.Contain("\"Captured\"").And.NotContain("23.55").And.NotContainEquivalentOf("latitude");
            var sensitiveHr = await PersonAsync("hr:view,view_sensitive", employee: false);
            var sensitive = await sensitiveHr.Client.GetAsync($"/api/v1/hr/employees/{ana.EmployeeId}/time-entries/sensitive");
            sensitive.StatusCode.Should().Be(HttpStatusCode.OK);
            sensitive.Headers.CacheControl!.NoStore.Should().BeTrue();
            var locations = (await JsonAsync(sensitive)).EnumerateArray().ToList();
            locations.Should().HaveCount(3, "as três marcações com a geolocalização ligada");
            locations.Single(l => l.GetProperty("id").GetGuid() == withLocation.Id).GetProperty("latitude").GetDecimal().Should().Be(-23.5505m);
            (await AuditAsync("TimeEntry.Location", ana.EmployeeId.ToString())).Should().ContainSingle(a => a.Action == AuditActions.SensitiveRead && a.UserId == sensitiveHr.UserId && !a.Changes.Contains("23.55"));

            // O próprio funcionário vê a própria localização em "Minhas marcações".
            var mine = (await JsonAsync(await ana.Client.GetAsync(Entries))).EnumerateArray().ToList();
            mine.Single(m => m.GetProperty("id").GetGuid() == withLocation.Id).GetProperty("latitude").GetDecimal().Should().Be(-23.5505m);

            // Canário (CA 16): na auditoria, quem, quando e o aparelho, com a localização mascarada; nada no log.
            var created = (await AuditAsync(nameof(TimeEntry), withLocation.Id.ToString())).Single();
            created.UserId.Should().Be(ana.UserId);
            created.Changes.Should().NotContain("23.55").And.NotContain("46.63").And.Contain(AuditSaveChangesInterceptor.Masked);
            LogText().Should().NotContain("23.55").And.NotContain("46.63");
        }
        finally
        {
            await SetGeolocationAsync("Off");
        }
    }
}
