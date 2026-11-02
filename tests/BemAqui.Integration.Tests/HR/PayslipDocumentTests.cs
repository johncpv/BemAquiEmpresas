using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Host.Persistence;
using BemAqui.HR.Domain;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Attachments;
using BemAqui.Integration.Tests.Messaging;
using BemAqui.Integration.Tests.Observability;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Infrastructure.Catalog;
using BemAqui.SharedKernel;
using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Core;

namespace BemAqui.Integration.Tests.HR;

/// <summary>
/// RH-02b: holerite em PDF pelo RH (rascunho com marca d'água, fechado guardado como arquivo do sistema), "Baixar todos" (ZIP na hora ou
/// em segundo plano), "Meus holerites" pelo vínculo (IDOR, no-store, "Visto em"), aviso por e-mail sem anexo nem valor, "Alterado depois
/// do aviso" e o canário. Banco desta classe; cada teste usa competências próprias (Férias com os funcionários escolhidos).
/// </summary>
public partial class PayslipDocumentTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>, IDisposable
{
    private const string HostA = "a.localhost";
    private const string Runs = "/api/v1/hr/payroll-runs";
    private const string Mine = "/api/v1/me/hr/payslips";

    private readonly FakeScanner _scanner = new();
    private readonly MemorySink _sink = new();
    private readonly CapturingJobClient _jobs = new();
    private readonly string _storage = Path.Combine(Path.GetTempPath(), "bqe-rh02b-" + Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory => _factory ??= tenancy.Factory.WithWebHostBuilder(b => b
        .UseSetting("Storage:Root", _storage)
        .ConfigureTestServices(s =>
        {
            s.AddSingleton<IVirusScanner>(_scanner);
            s.AddSingleton<ILogEventSink>(_sink);
            s.RemoveAll<IBackgroundJobClient>();
            s.AddSingleton<IBackgroundJobClient>(_jobs);
        }));

    public void Dispose()
    {
        _factory?.Dispose();
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, true);
        }

        GC.SuppressFinalize(this);
    }

    private sealed class Authenticator(byte[] secret)
    {
        private long _last = long.MinValue;

        public string Next()
        {
            // O servidor aceita o passo anterior, o atual e o próximo; o código sai do passo anterior (sobram dois à frente). Nos últimos 10 s
            // do passo, ele venceria no meio da requisição quando o relógio vira (2FA "inválido" intermitente): espera o passo virar antes.
            var wait = Totp.StepSeconds - (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % (Totp.StepSeconds * 1000L)) / 1000.0;
            if (wait < 10)
            {
                Thread.Sleep(TimeSpan.FromSeconds(wait + 0.2));
            }

            var step = Math.Max(_last + 1, Totp.StepAt(DateTimeOffset.UtcNow) - 1);
            _last = step;
            return Totp.Code(secret, step);
        }
    }

    private sealed record Actor(Guid Id, HttpClient Client, Authenticator? Codes, string Email);

    private sealed record Person(Guid Id, string Registration, string Name, string Cpf);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> CodeAsync(HttpResponseMessage response) => (await JsonAsync(response)).TryGetProperty("code", out var code) ? code.GetString() : null;

    private async Task<T> InTenantAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        await using var scope = await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        return await action(scope.ServiceProvider);
    }

    private Task<T> DbAsync<T>(Func<TenantDbContext, Task<T>> action) => InTenantAsync(sp => action(sp.GetRequiredService<TenantDbContext>()));

    /// <summary>Usuário num grupo do seed ou num grupo próprio ("hr:view,approve"), ou sem grupo (null); com o 2FA se pedir.</summary>
    private async Task<Actor> ActorAsync(string? grants, bool totp = false)
    {
        var email = "h" + Guid.NewGuid().ToString("N")[..10] + "@teste.com";
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, email);
        var secret = Totp.NewSecret();
        stamp = await InTenantAsync(async sp =>
        {
            var db = sp.GetRequiredService<IIdentityDb>();
            if (grants is not null)
            {
                Guid groupId;
                if (grants.Contains(':', StringComparison.Ordinal))
                {
                    var group = new Group { Name = "Holerite " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
                    db.Groups.Add(group);
                    foreach (var part in grants.Split(';'))
                    {
                        var module = part[..part.IndexOf(':', StringComparison.Ordinal)];
                        db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
                        foreach (var action in part[(module.Length + 1)..].Split(','))
                        {
                            db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = module, Action = action });
                        }
                    }

                    groupId = group.Id;
                }
                else
                {
                    groupId = await db.Groups.Where(g => g.SeedKey == grants).Select(g => g.Id).SingleAsync();
                }

                db.UserGroups.Add(new UserGroup { UserId = id, GroupId = groupId });
            }

            var user = await db.Users.SingleAsync(u => u.Id == id);
            if (totp)
            {
                user.BeginTotpSetup(Factory.Services.GetRequiredService<ISecretProtector>().Protect(SecretScope.ForTenant(tenancy.TenantA.Id), SecretKind.TotpSecret, new Secret(Base32.Encode(secret))));
                user.EnableTotp(Totp.StepAt(DateTimeOffset.UtcNow) - 10);
            }

            await db.SaveChangesAsync();
            await sp.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([id]);
            return user.SecurityStamp;
        });
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = HostA;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, HostA, userId: id, stamp: stamp, otpAt: DateTimeOffset.UtcNow));
        return new Actor(id, client, totp ? new Authenticator(secret) : null, email);
    }

    /// <summary>Usuário sem grupo ligado ao funcionário (o vínculo da CAD-04b, gravado direto).</summary>
    private async Task<Actor> LinkedAsync(Guid employeeId)
    {
        var actor = await ActorAsync(null);
        await InTenantAsync(async sp =>
        {
            var db = sp.GetRequiredService<IIdentityDb>();
            (await db.Users.SingleAsync(u => u.Id == actor.Id)).EmployeeId = employeeId;
            return await db.SaveChangesAsync();
        });
        return actor;
    }

    private static HttpRequestMessage Post(string url, object? body, Guid? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body ?? new { }) };
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        return request;
    }

    private static async Task<Guid> PositionAsync(HttpClient hr)
    {
        var schedule = (await JsonAsync(await hr.GetAsync("/api/v1/hr/schedules"))).EnumerateArray().First().GetProperty("id").GetGuid();
        var created = await hr.PostAsJsonAsync("/api/v1/hr/positions", new { name = "Soldador " + Guid.NewGuid().ToString("N")[..6], cbo = "7243-15", defaultWorkCenterId = (Guid?)null, workScheduleId = schedule, monthlyHours = (decimal?)null });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    private static async Task<Person> EmployeeAsync(HttpClient owner, Guid position, bool registration = true)
    {
        var cpf = NewCpf();
        var reg = registration ? "R" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant() : null;
        var name = "Pessoa " + Guid.NewGuid().ToString("N")[..6];
        var created = await owner.PostAsJsonAsync("/api/v1/hr/employees", new { name, cpf, birthDate = "1990-05-10", admissionDate = "2020-01-02", positionId = position, workScheduleId = (Guid?)null, baseSalary = 3_000m, registration = reg });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return new Person((await JsonAsync(created)).GetProperty("employee").GetProperty("id").GetGuid(), reg ?? string.Empty, name, cpf);
    }

    private static string NewCpf()
    {
        var digits = Guid.NewGuid().ToString("N").Where(char.IsAsciiDigit).Concat("98765432").Take(9).Select(c => c - '0').ToList();
        int Dv(int size) { var r = digits.Take(size).Select((x, i) => x * (size + 1 - i)).Sum() * 10 % 11; return r == 10 ? 0 : r; }
        digits.Add(Dv(9));
        digits.Add(Dv(10));
        return string.Concat(digits);
    }

    private static async Task<Guid> RunAsync(HttpClient owner, string period, IEnumerable<Guid> employees)
    {
        var created = await owner.SendAsync(Post(Runs, new { period, type = "Vacation", employeeIds = employees.ToArray() }));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    /// <summary>Holerites com o salário (valor por funcionário), o FGTS informativo e as bases.</summary>
    private static async Task FillAsync(HttpClient owner, Guid run, decimal amount)
    {
        var items = (await JsonAsync(await owner.GetAsync("/api/v1/hr/payroll-items"))).EnumerateArray().ToDictionary(i => i.GetProperty("code").GetString()!, i => i.GetProperty("id").GetGuid());
        foreach (var slip in (await JsonAsync(await owner.GetAsync($"{Runs}/{run}/payslips/sensitive"))).EnumerateArray().ToList())
        {
            var saved = await owner.PutAsJsonAsync($"{Runs}/{run}/payslips/{slip.GetProperty("id").GetGuid()}",
                new { lines = new[] { new { payrollItemId = items["SAL"], reference = "30 dias", amount } }, inssBase = amount, fgtsBase = amount, irrfBase = amount, fgtsAmount = 0m, version = slip.GetProperty("version").GetUInt32() });
            saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }
    }

    private static async Task<List<JsonElement>> SlipsAsync(HttpClient owner, Guid run) =>
        (await JsonAsync(await owner.GetAsync($"{Runs}/{run}/payslips/sensitive"))).EnumerateArray().ToList();

    /// <summary>Folha fechada direto no banco (o fechamento real tem os próprios testes; aqui só importa a situação).</summary>
    private Task<int> SetStatusAsync(Guid run, PayrollRunStatus status) =>
        DbAsync(async db =>
        {
            var r = await db.PayrollRuns.SingleAsync(x => x.Id == run);
            r.Status = status;
            r.ClosedAt = status == PayrollRunStatus.Draft ? null : DateTimeOffset.UtcNow;
            return await db.SaveChangesAsync();
        });

    private Task<int> AuditCountAsync(string entity, string id, string action) =>
        InTenantAsync(sp => sp.GetRequiredService<BemAquiDbContext>().AuditEntries.CountAsync(a => a.Entity == entity && a.EntityId == id && a.Action == action));

    private async Task SetHrReleasedAsync(bool released)
    {
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == "hr").ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
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
}
