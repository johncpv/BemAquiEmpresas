using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Application.TimeClock;
using BemAqui.HR.Domain;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Observability;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Infrastructure.Catalog;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// Base dos testes da RH-01b: pessoas (grupos do seed ou próprios, com funcionário), marcações gravadas direto no banco, pedidos pela API,
/// o período fechado simulado (a RH-01c registra o real) e o log capturado para o canário da justificativa.
/// </summary>
public abstract class TimeAdjustmentTestBase(TenancyFixture tenancy) : IDisposable
{
    protected const string HostA = "a.localhost";
    protected const string Mine = "/api/v1/me/hr/time-adjustments";
    protected const string Adjustments = "/api/v1/hr/time-adjustments";
    protected const string Canary = "Atestado do Dr Canario 77";

    private readonly MemorySink _sink = new();
    private WebApplicationFactory<Program>? _factory;

    protected TenancyFixture Tenancy { get; } = tenancy;

    /// <summary>Período fechado simulado (CA 16): o mês fechado e o início do período mais antigo em aberto.</summary>
    protected FakePeriodLock Periods { get; } = new();

    /// <summary>A RH-01c usa a trava real (os espelhos fechados); a RH-01b, a simulada.</summary>
    protected virtual bool FakePeriods => true;

    protected WebApplicationFactory<Program> Factory => _factory ??= Tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
    {
        s.AddSingleton<ILogEventSink>(_sink);
        if (FakePeriods)
        {
            s.AddSingleton<ITimesheetPeriodLock>(Periods);
        }

        s.AddSingleton<BemAqui.BuildingBlocks.Attachments.IVirusScanner>(new Attachments.FakeScanner());
    }));

    public void Dispose()
    {
        _factory?.Dispose();
        GC.SuppressFinalize(this);
    }

    protected sealed record Person(Guid UserId, Guid EmployeeId, HttpClient Client);

    protected sealed class FakePeriodLock : ITimesheetPeriodLock
    {
        public DateOnly? ClosedMonth { get; set; }

        public DateOnly? OldestOpen { get; set; }

        public Task<DateOnly?> OldestOpenStartAsync(CancellationToken cancellationToken = default) => Task.FromResult(OldestOpen);

        public Task<Error?> CheckAsync(DateOnly day, CancellationToken cancellationToken = default) =>
            Task.FromResult(ClosedMonth is { } m && m.Year == day.Year && m.Month == day.Month ? TimesheetPeriods.Closed(new DateOnly(day.Year, day.Month, 1)) : null);
    }

    protected static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    protected static async Task<string?> CodeAsync(HttpResponseMessage response) => (await JsonAsync(response)).TryGetProperty("code", out var code) ? code.GetString() : null;

    protected async Task<T> InTenantAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(Tenancy.TenantA.Id))!;
        await using var scope = await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        return await action(scope.ServiceProvider);
    }

    protected Task<T> DbAsync<T>(Func<TenantDbContext, Task<T>> action) => InTenantAsync(sp => action(sp.GetRequiredService<TenantDbContext>()));

    protected Task<List<AuditEntry>> AuditAsync(string entity, string id) =>
        InTenantAsync(sp => sp.GetRequiredService<BemAquiDbContext>().AuditEntries.AsNoTracking().Where(a => a.Entity == entity && a.EntityId == id).ToListAsync());

    /// <summary>Último dia útil (segunda a sexta) antes de hoje, dentro do vínculo (admissão há um ano).</summary>
    protected static DateOnly LastWorkday()
    {
        var day = BusinessTime.Today(TimeProvider.System).AddDays(-1);
        while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            day = day.AddDays(-1);
        }

        return day;
    }

    protected HttpClient Client(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = HostA;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Usuário ativo nos grupos do seed (pela <c>seed_key</c>) ou num grupo próprio ("hr:view,create", ";" separa módulos, "|" separa
    /// grupos), ligado a um funcionário novo com a jornada do seed quando <paramref name="employee"/>.
    /// </summary>
    protected async Task<Person> PersonAsync(string? groups = null, bool employee = true, string name = "Ana Souza")
    {
        var email = "ajuste-" + Guid.NewGuid().ToString("N")[..10] + "@konebras.com.br";
        var (id, _) = await Tenancy.SeedUserAsync(Tenancy.TenantA, email);
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
                    var group = new Group { Name = "Ajuste " + Guid.NewGuid().ToString("N")[..8], Description = "Teste" };
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
                var e = new Employee
                {
                    Name = name, PositionId = position.Id, WorkScheduleId = schedule, AdmissionDate = BusinessTime.Today(TimeProvider.System).AddYears(-1),
                    Cpf = Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    BirthDate = new DateOnly(1985, 3, 15), BaseSalary = 3000m, CreatedAt = DateTimeOffset.UtcNow,
                };
                db.Employees.Add(e);
                user.EmployeeId = e.Id;
                person = e.Id;
            }

            await db.SaveChangesAsync();
            await sp.GetRequiredService<IPermissionService>().BumpAsync([id]);
            return person ?? Guid.Empty;
        });
        var stamp = await DbAsync(db => db.Users.Where(u => u.Id == id).Select(u => u.SecurityStamp).SingleAsync());
        return new Person(id, employeeId, Client(TestTokens.Create(Tenancy.TenantA.Id, HostA, userId: id, stamp: stamp)));
    }

    /// <summary>Funcionário sem usuário (o RH lança por ele).</summary>
    protected Task<Guid> EmployeeAsync(string name = "Bruno Lima") => DbAsync(async db =>
    {
        var schedule = await db.WorkSchedules.Where(s => s.SeedKey == "44h-seg-sex").Select(s => (Guid?)s.Id).FirstOrDefaultAsync();
        var position = new JobPosition { Name = "Soldador " + Guid.NewGuid().ToString("N")[..6], MonthlyHours = 220m, WorkScheduleId = schedule, CreatedAt = DateTimeOffset.UtcNow };
        db.JobPositions.Add(position);
        var e = new Employee
        {
            Name = name, PositionId = position.Id, WorkScheduleId = schedule, AdmissionDate = BusinessTime.Today(TimeProvider.System).AddYears(-1),
            Cpf = Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture),
            BirthDate = new DateOnly(1990, 5, 1), BaseSalary = 2800m, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Employees.Add(e);
        await db.SaveChangesAsync();
        return e.Id;
    });

    /// <summary>Marcação válida gravada direto (como a do app), no dia e hora local informados.</summary>
    protected Task<Guid> EntryAsync(Guid employeeId, DateOnly day, int hour, int minute, TimeEntryType type) => DbAsync(async db =>
    {
        var local = day.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Unspecified);
        var at = new DateTimeOffset(local, BusinessTime.Zone.GetUtcOffset(local)).ToUniversalTime();
        var entry = new TimeEntry { EmployeeId = employeeId, At = at, Day = day, Type = type, Source = TimeEntrySource.App, CreatedAt = DateTimeOffset.UtcNow };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    });

    protected static HttpRequestMessage Post(string url, object body, Guid? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        return request;
    }

    protected static string Of(Guid employeeId) => $"/api/v1/hr/employees/{employeeId}/time-adjustments";

    /// <summary>"Lançar marcação" do RH: o pedido criado (id e versão).</summary>
    protected static async Task<JsonElement> LaunchAsync(HttpClient client, Guid employeeId, DateOnly day, string time, string type, string justification = "Esqueceu de marcar no app")
    {
        var response = await client.SendAsync(Post(Of(employeeId), new { kind = "AddEntry", day, time, entryType = type, justification }));
        if (response.StatusCode != System.Net.HttpStatusCode.Created)
        {
            throw new InvalidOperationException("Lançamento recusado: " + await response.Content.ReadAsStringAsync());
        }

        return await JsonAsync(response);
    }

    protected static Task<HttpResponseMessage> ApproveAsync(HttpClient client, JsonElement adjustment) =>
        client.PostAsJsonAsync($"{Adjustments}/{adjustment.GetProperty("id").GetGuid()}/approve", new { version = adjustment.GetProperty("version").GetUInt32() });

    protected async Task SetModuleReleasedAsync(string module, bool released)
    {
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == Tenancy.TenantA.Id && m.ModuleCode == module).ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
        Tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    protected string LogText()
    {
        lock (_sink.Events)
        {
            return string.Join("\n", _sink.Events.Select(e => e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + " " + string.Join(" ", e.Properties.Select(p => p.Value.ToString()))));
        }
    }
}
