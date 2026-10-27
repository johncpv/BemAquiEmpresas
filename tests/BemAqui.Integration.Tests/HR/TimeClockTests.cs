using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Observability;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// RH-01a: "Meu ponto" como controle interno de jornada (faixa e comprovante), marcação com a hora do servidor e o corpo sem
/// <c>employeeId</c>, duplo toque e <c>Idempotency-Key</c>, vínculo ativo, sessão por PIN (só hoje e marcar; marcar encerra a sessão),
/// geolocalização desligada, negada e ligada com a ciência, localização só pela rota <c>/sensitive</c>, IDOR das próprias marcações, as
/// três camadas e o canário da localização. Banco desta classe; o parâmetro da geolocalização volta a <c>Off</c> no fim de cada teste.
/// </summary>
public partial class TimeClockTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>, IDisposable
{
    private const string HostA = "a.localhost";
    private const string Origin = "https://a.localhost";
    private const string DeviceCookie = "__Host-bq_dev";
    private const string RefreshCookie = "__Host-bq_rt";
    private const string GoodPin = "483920";
    private const string Entries = "/api/v1/me/hr/time-entries";
    private const string Today = "/api/v1/me/hr/time-entries/today";
    private const string Banner = "Controle interno de jornada. Este registro não substitui o registro legal de ponto (REP-P, Portaria 671).";

    private readonly MemorySink _sink = new();
    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory => _factory ??= tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<ILogEventSink>(_sink)));

    public void Dispose()
    {
        _factory?.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record Person(Guid UserId, string Email, Guid EmployeeId, HttpClient Client);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> CodeAsync(HttpResponseMessage response) => (await JsonAsync(response)).TryGetProperty("code", out var code) ? code.GetString() : null;

    private async Task<T> InTenantAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        await using var scope = await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        return await action(scope.ServiceProvider);
    }

    private Task<T> DbAsync<T>(Func<TenantDbContext, Task<T>> action) => InTenantAsync(sp => action(sp.GetRequiredService<TenantDbContext>()));

    private HttpClient Client(string? token = null)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = HostA;
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    /// <summary>
    /// Usuário ativo (senha padrão) em grupos do seed ou num grupo próprio ("hr:view;shopfloor:view,create", "|" separa grupos), ligado a um
    /// funcionário novo (com a jornada do seed) quando <paramref name="employee"/>; afastado ou desligado se pedir. O cliente usa um token de
    /// teste (sessão completa).
    /// </summary>
    private async Task<Person> PersonAsync(string? groups = null, bool employee = true, bool onLeave = false, bool terminated = false, string name = "Ana Souza")
    {
        var email = "ponto-" + Guid.NewGuid().ToString("N")[..10] + "@konebras.com.br";
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, email);
        var employeeId = await InTenantAsync(async sp =>
        {
            var db = sp.GetRequiredService<TenantDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == id);
            user.Name = name;
            foreach (var spec in (groups ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                Guid groupId;
                if (spec.Contains(':', StringComparison.Ordinal))
                {
                    var group = new Group { Name = "Ponto " + Guid.NewGuid().ToString("N")[..8], Description = "Teste" };
                    db.Groups.Add(group);
                    foreach (var part in spec.Split(';'))
                    {
                        var module = part.Split(':')[0];
                        db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
                        foreach (var action in part.Split(':')[1].Split(','))
                        {
                            db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = module, Action = action });
                        }
                    }

                    groupId = group.Id;
                }
                else
                {
                    groupId = await db.Groups.Where(g => g.SeedKey == spec).Select(g => g.Id).SingleAsync();
                }

                db.UserGroups.Add(new UserGroup { UserId = id, GroupId = groupId });
            }

            Guid? person = null;
            if (employee)
            {
                var schedule = await db.WorkSchedules.Where(s => s.SeedKey == "44h-seg-sex").Select(s => (Guid?)s.Id).FirstOrDefaultAsync();
                var position = new JobPosition { Name = "Montador " + Guid.NewGuid().ToString("N")[..6], MonthlyHours = 220m, WorkScheduleId = schedule, CreatedAt = DateTimeOffset.UtcNow };
                db.JobPositions.Add(position);
                var today = BusinessTime.Today(TimeProvider.System);
                var e = new Employee
                {
                    Name = name, PositionId = position.Id, WorkScheduleId = schedule, AdmissionDate = today.AddYears(-1),
                    Cpf = Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    BirthDate = new DateOnly(1985, 3, 15), BaseSalary = 3000m, CreatedAt = DateTimeOffset.UtcNow,
                };
                if (terminated)
                {
                    e.Status = EmployeeStatus.Terminated;
                    e.TerminationDate = today.AddDays(-1);
                }

                db.Employees.Add(e);
                if (onLeave)
                {
                    db.EmployeeHistory.Add(new EmployeeHistoryEntry
                    {
                        EmployeeId = e.Id, Kind = EmployeeHistoryKind.Leave, EffectiveDate = today.AddDays(-1), LeaveEndDate = today.AddDays(5),
                        LeaveType = EmployeeLeaveType.Illness, NewValue = "Afastado", Reason = "Atestado", CreatedAt = DateTimeOffset.UtcNow,
                    });
                }

                user.EmployeeId = e.Id;
                person = e.Id;
            }

            await db.SaveChangesAsync();
            await sp.GetRequiredService<IPermissionService>().BumpAsync([id]);
            return person ?? Guid.Empty;
        });
        stamp = await DbAsync(db => db.Users.Where(u => u.Id == id).Select(u => u.SecurityStamp).SingleAsync());
        return new Person(id, email, employeeId, Client(TestTokens.Create(tenancy.TenantA.Id, HostA, userId: id, stamp: stamp)));
    }

    private static HttpRequestMessage Mark(object body, Guid? key = null, string? token = null, string? device = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Entries) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (device is not null)
        {
            request.Headers.Add("Cookie", DeviceCookie + "=" + device);
        }

        return request;
    }

    private Task<List<TimeEntry>> EntriesOfAsync(Guid employeeId) =>
        DbAsync(db => db.TimeEntries.AsNoTracking().Where(e => e.EmployeeId == employeeId).OrderBy(e => e.At).ToListAsync());

    private async Task SetGeolocationAsync(string mode)
    {
        var owner = await PersonAsync(Group.OwnerSeedKey, employee: false);
        var saved = await owner.Client.PutAsJsonAsync("/api/v1/hr/parameters", new { changes = new[] { new { key = "hr.time_clock_geolocation", value = (decimal?)null, partyId = (Guid?)null, code = mode } } });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    private async Task SetModuleReleasedAsync(string module, bool released)
    {
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == module).ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    private string LogText()
    {
        lock (_sink.Events)
        {
            return string.Join("\n", _sink.Events.Select(e => e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + " " + string.Join(" ", e.Properties.Select(p => p.Value.ToString()))));
        }
    }

    /// <summary>CA 1, 2, 3 e 6: faixa, jornada e sugestão; a marcação nasce com a hora do servidor e o comprovante de controle interno.</summary>
    [Fact]
    public async Task Marcar_entrada_com_a_hora_do_servidor_e_o_comprovante_de_controle_interno()
    {
        var ana = await PersonAsync();

        var today = await ana.Client.GetAsync(Today);
        today.StatusCode.Should().Be(HttpStatusCode.OK, await today.Content.ReadAsStringAsync());
        today.Headers.CacheControl!.NoStore.Should().BeTrue();
        var clock = await JsonAsync(today);
        clock.GetProperty("banner").GetString().Should().Be(Banner);
        clock.GetProperty("firstName").GetString().Should().Be("Ana");
        clock.GetProperty("serverNow").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        clock.GetProperty("entries").GetArrayLength().Should().Be(0);
        clock.GetProperty("suggested").GetString().Should().Be("In");
        clock.GetProperty("suggestedVerb").GetString().Should().Be("Registrar entrada");
        clock.GetProperty("geolocation").GetString().Should().Be("Off");
        var weekday = BusinessTime.Today(TimeProvider.System).DayOfWeek;
        clock.GetProperty("schedule").GetString().Should().Be(weekday is DayOfWeek.Saturday or DayOfWeek.Sunday ? "Sem jornada hoje" : "08:00–17:48, intervalo 1 h");

        // O corpo só aceita o tipo (e a localização): employeeId, at ou outro campo, 400; sem a chave, recusado.
        (await ana.Client.SendAsync(Mark(new { type = "In", employeeId = Guid.NewGuid() }))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ana.Client.SendAsync(Mark(new { type = "In", at = "2026-10-21T08:00:00Z" }))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ana.Client.PostAsJsonAsync(Entries, new { type = "In" })).IsSuccessStatusCode.Should().BeFalse("o comando exige Idempotency-Key");
        (await EntriesOfAsync(ana.EmployeeId)).Should().BeEmpty();

        var before = DateTimeOffset.UtcNow;
        var created = await ana.Client.SendAsync(Mark(new { type = "In" }));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var receipt = await JsonAsync(created);
        receipt.GetProperty("title").GetString().Should().Be("Comprovante de controle interno");
        receipt.GetProperty("message").GetString().Should().Be("Entrada registrada");
        receipt.GetProperty("source").GetString().Should().Be("App");
        receipt.GetProperty("sessionEnded").GetBoolean().Should().BeFalse();
        var at = receipt.GetProperty("at").GetDateTimeOffset();
        at.Should().BeOnOrAfter(before.AddSeconds(-1)).And.BeOnOrBefore(DateTimeOffset.UtcNow.AddSeconds(1));
        var local = BusinessTime.ToBusiness(at);
        receipt.GetProperty("text").GetString().Should().Be($"Entrada registrada às {local:HH:mm} de {local:dd/MM/yyyy}");
        receipt.TryGetProperty("latitude", out _).Should().BeFalse();

        var row = (await EntriesOfAsync(ana.EmployeeId)).Single();
        row.Type.Should().Be(TimeEntryType.In);
        row.Source.Should().Be(TimeEntrySource.App);
        row.RecordedByUserId.Should().Be(ana.UserId);
        row.DeviceId.Should().BeNull();
        row.Location.Should().Be(TimeEntryLocation.NotAsked);
        row.Day.Should().Be(DateOnly.FromDateTime(local.DateTime));

        var after = await JsonAsync(await ana.Client.GetAsync(Today));
        after.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("type").GetString()).Should().Equal("In");
        after.GetProperty("suggested").GetString().Should().Be("BreakStart");
        after.GetProperty("suggestedVerb").GetString().Should().Be("Registrar saída para o intervalo");
        after.GetProperty("outOfSequence").GetBoolean().Should().BeFalse();
    }

    /// <summary>CA 4 e 5: duplo toque responde 409 com a hora da primeira; a mesma chave devolve o comprovante original; fora de ordem é aceito.</summary>
    [Fact]
    public async Task Duplo_toque_responde_409_e_o_reenvio_com_a_mesma_chave_devolve_o_comprovante()
    {
        var ana = await PersonAsync();
        var key = Guid.NewGuid();
        var first = await ana.Client.SendAsync(Mark(new { type = "In" }, key));
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var original = await JsonAsync(first);

        var replay = await ana.Client.SendAsync(Mark(new { type = "In" }, key));
        replay.StatusCode.Should().Be(HttpStatusCode.Created);
        (await JsonAsync(replay)).GetProperty("id").GetGuid().Should().Be(original.GetProperty("id").GetGuid(), "o reenvio devolve o comprovante original");

        var twice = await ana.Client.SendAsync(Mark(new { type = "In" }));
        twice.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(twice);
        problem.GetProperty("code").GetString().Should().Be("time_entry_duplicate");
        var local = BusinessTime.ToBusiness(original.GetProperty("at").GetDateTimeOffset());
        problem.GetProperty("detail").GetString().Should().Be($"Você já registrou entrada às {local:HH:mm}.");
        (await EntriesOfAsync(ana.EmployeeId)).Should().HaveCount(1);

        // Dois toques ao mesmo tempo com chaves diferentes: só um grava (trava por funcionário).
        var both = await Task.WhenAll(ana.Client.SendAsync(Mark(new { type = "Out" })), ana.Client.SendAsync(Mark(new { type = "Out" })));
        both.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.Created, HttpStatusCode.Conflict]);

        // Saída logo depois da entrada: fora da sequência sugerida (pulou o intervalo), aceita e com o atalho do ajuste.
        var outReceipt = await JsonAsync(both.Single(r => r.StatusCode == HttpStatusCode.Created));
        outReceipt.GetProperty("outOfSequence").GetBoolean().Should().BeTrue();
        outReceipt.GetProperty("hint").GetString().Should().Be("Esqueceu uma marcação? Peça o ajuste ao RH.");
        (await JsonAsync(await ana.Client.GetAsync(Today))).GetProperty("outOfSequence").GetBoolean().Should().BeTrue();
        (await EntriesOfAsync(ana.EmployeeId)).Select(e => e.Type).Should().Equal(TimeEntryType.In, TimeEntryType.Out);
    }

    /// <summary>CA 6 e 15: sem vínculo, desligado ou afastado, 403 employee_not_active; módulo não liberado, 403 module_not_released.</summary>
    [Fact]
    public async Task Sem_vinculo_desligado_ou_afastado_nao_marca()
    {
        var stranger = await PersonAsync(employee: false);
        var terminated = await PersonAsync(terminated: true);
        var onLeave = await PersonAsync(onLeave: true);
        foreach (var who in new[] { stranger, terminated, onLeave })
        {
            var refused = await who.Client.SendAsync(Mark(new { type = "In" }));
            refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var problem = await JsonAsync(refused);
            problem.GetProperty("code").GetString().Should().Be("employee_not_active");
            problem.GetProperty("detail").GetString().Should().Be("Seu usuário não está ligado a um funcionário ativo. Fale com o RH.");
            (await CodeAsync(await who.Client.GetAsync(Today))).Should().Be("employee_not_active");
        }

        (await onLeave.Client.GetAsync(Entries)).StatusCode.Should().Be(HttpStatusCode.OK, "o afastado consulta as próprias marcações");
        (await DbAsync(db => db.TimeEntries.CountAsync(e => e.EmployeeId == terminated.EmployeeId || e.EmployeeId == onLeave.EmployeeId))).Should().Be(0);

        var ana = await PersonAsync();
        var hr = await PersonAsync("hr:view", employee: false);
        await SetModuleReleasedAsync("hr", false);
        try
        {
            (await CodeAsync(await ana.Client.SendAsync(Mark(new { type = "In" })))).Should().Be("module_not_released");
            (await CodeAsync(await ana.Client.GetAsync(Today))).Should().Be("module_not_released");
            (await CodeAsync(await hr.Client.GetAsync("/api/v1/hr/time-entries"))).Should().Be("module_not_released");
        }
        finally
        {
            await SetModuleReleasedAsync("hr", true);
        }
    }

    /// <summary>CA 12, 13 e 15: o RH vê por dia com hr.view (sem localização); sem hr.view, 403; cada funcionário vê só as próprias (IDOR).</summary>
    [Fact]
    public async Task RH_consulta_por_funcionario_e_dia_e_cada_um_ve_so_as_proprias()
    {
        var ana = await PersonAsync(name: "Ana Lima");
        var bia = await PersonAsync(name: "Bia Rocha");
        (await ana.Client.SendAsync(Mark(new { type = "In" }))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await bia.Client.SendAsync(Mark(new { type = "In" }))).StatusCode.Should().Be(HttpStatusCode.Created);

        var mine = await ana.Client.GetAsync(Entries);
        mine.StatusCode.Should().Be(HttpStatusCode.OK);
        mine.Headers.CacheControl!.NoStore.Should().BeTrue();
        var anaIds = (await EntriesOfAsync(ana.EmployeeId)).Select(e => e.Id).ToList();
        (await JsonAsync(mine)).EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).Should().BeEquivalentTo(anaIds, "nunca as de outro funcionário");
        (await ana.Client.GetAsync($"{Entries}?employeeId={bia.EmployeeId}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ana.Client.GetAsync($"/api/v1/hr/employees/{bia.EmployeeId}/time-entries")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "sem grupo com hr.view");

        var hr = await PersonAsync("hr:view", employee: false);
        var day = BusinessTime.Today(TimeProvider.System).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var list = await hr.Client.GetAsync($"/api/v1/hr/time-entries?from={day}&to={day}&employeeId={ana.EmployeeId}");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        var body = await list.Content.ReadAsStringAsync();
        body.Should().NotContainEquivalentOf("latitude").And.NotContainEquivalentOf("accuracy");
        var items = JsonDocument.Parse(body).RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Should().ContainSingle();
        items[0].GetProperty("employeeName").GetString().Should().Be("Ana Lima");
        items[0].GetProperty("source").GetString().Should().Be("App");
        items[0].GetProperty("location").GetString().Should().Be("NotAsked");
        var employee = await hr.Client.GetAsync($"/api/v1/hr/employees/{bia.EmployeeId}/time-entries");
        (await JsonAsync(employee)).GetProperty("items").EnumerateArray().Should().ContainSingle(i => i.GetProperty("employeeId").GetGuid() == bia.EmployeeId);
        (await hr.Client.GetAsync($"/api/v1/hr/employees/{bia.EmployeeId}/time-entries/sensitive")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "localização exige hr.view_sensitive");
        (await hr.Client.GetAsync("/api/v1/hr/time-entries?from=2026-01-01&to=2026-06-30")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "período de até 62 dias");
        (await hr.Client.GetAsync($"/api/v1/hr/employees/{Guid.NewGuid()}/time-entries")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var noHr = await PersonAsync("parties:view", employee: false);
        (await CodeAsync(await noHr.Client.GetAsync("/api/v1/hr/time-entries"))).Should().Be("forbidden");
        var noView = await PersonAsync("hr:create", employee: false);
        (await noView.Client.GetAsync("/api/v1/hr/time-entries")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // CA 14: não existe rota para editar nem apagar a marcação.
        var id = anaIds[0];
        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete, HttpMethod.Patch })
        {
            var owner = await PersonAsync(Group.OwnerSeedKey, employee: false);
            var response = await owner.Client.SendAsync(new HttpRequestMessage(method, $"{Entries}/{id}") { Content = JsonContent.Create(new { type = "Out" }) });
            response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        }
    }
}
