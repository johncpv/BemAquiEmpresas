using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Application.Privacy;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Attachments;
using BemAqui.Integration.Tests.Messaging;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.SharedKernel;
using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.Integration.Tests.Settings;

/// <summary>
/// LGPD-01a: pedido do titular em Configurações → Privacidade, só do Proprietário; prazo em dias corridos; identidade conferida antes de
/// exportar; exportação do funcionário, do terceiro PF e do usuário pelos provedores de cada módulo (ZIP com JSON, PDF e anexos), link de uso
/// único que vence, <c>SensitiveRead</c> e canário; aviso do prazo e expurgos. Banco desta classe (tenant compartilhado pelos testes: as
/// contagens são relativas a cada teste).
/// </summary>
public partial class PrivacyRequestTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>, IDisposable
{
    private const string HostA = "a.localhost";
    private const string Api = "/api/v1/settings/privacy-requests";

    private readonly FakeScanner _scanner = new();
    private readonly CapturingJobClient _jobs = new();
    private readonly string _storage = Path.Combine(Path.GetTempPath(), "bqe-lgpd01a-" + Guid.NewGuid().ToString("N"));
    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory => _factory ??= tenancy.Factory.WithWebHostBuilder(b => b
        .UseSetting("Storage:Root", _storage)
        .ConfigureTestServices(s =>
        {
            s.AddSingleton<IVirusScanner>(_scanner);
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

    private sealed record Actor(Guid Id, HttpClient Client, string Email);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> CodeAsync(HttpResponseMessage response) => (await JsonAsync(response)).TryGetProperty("code", out var code) ? code.GetString() : null;

    private async Task<T> InTenantAsync<T>(Func<IServiceProvider, Task<T>> action, Guid? tenantId = null)
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenantId ?? tenancy.TenantA.Id))!;
        await using var scope = await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        return await action(scope.ServiceProvider);
    }

    private Task<T> DbAsync<T>(Func<TenantDbContext, Task<T>> action) => InTenantAsync(sp => action(sp.GetRequiredService<TenantDbContext>()));

    /// <summary>Usuário num grupo do seed ou num grupo próprio ("settings:view,edit;hr:view_sensitive"), com 2FA quando pedido.</summary>
    private async Task<Actor> ActorAsync(string? grants, bool totp = false, Guid? employeeId = null)
    {
        var email = "p" + Guid.NewGuid().ToString("N")[..10] + "@teste.com";
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, email);
        stamp = await InTenantAsync(async sp =>
        {
            var db = sp.GetRequiredService<IIdentityDb>();
            if (grants is not null)
            {
                Guid groupId;
                if (grants.Contains(':', StringComparison.Ordinal))
                {
                    var group = new Group { Name = "Privacidade " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
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
            user.EmployeeId = employeeId;
            if (totp)
            {
                user.BeginTotpSetup(Factory.Services.GetRequiredService<ISecretProtector>().Protect(SecretScope.ForTenant(tenancy.TenantA.Id), SecretKind.TotpSecret, new Secret(Base32.Encode(Totp.NewSecret()))));
                user.EnableTotp(Totp.StepAt(DateTimeOffset.UtcNow) - 10);
            }

            await db.SaveChangesAsync();
            await sp.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([id]);
            return user.SecurityStamp;
        });
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = HostA;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, HostA, userId: id, stamp: stamp, otpAt: DateTimeOffset.UtcNow));
        return new Actor(id, client, email);
    }

    private static HttpRequestMessage Post(string url, object? body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body ?? new { }) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return request;
    }

    private static string Today => DateOnly.FromDateTime(BemAqui.BuildingBlocks.Time.BusinessTime.ToBusiness(DateTimeOffset.UtcNow).DateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<JsonElement> CreateAsync(HttpClient owner, object body)
    {
        var created = await owner.SendAsync(Post(Api, body));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return await JsonAsync(created);
    }

    private static Task<JsonElement> UnknownAsync(HttpClient owner, string kind = "Access", string? requestedOn = null) =>
        CreateAsync(owner, new { subjectType = "Unknown", subjectId = (Guid?)null, subjectName = "Titular " + Guid.NewGuid().ToString("N")[..6] + " Souza", subjectContact = "titular@exemplo.com", kind, requestedOn = requestedOn ?? Today, channel = "Email", notes = "Pedido por e-mail" });

    private static async Task<JsonElement> VerifyAsync(HttpClient owner, JsonElement request)
    {
        var id = request.GetProperty("id").GetGuid();
        var verified = await owner.SendAsync(Post($"{Api}/{id}/identity", new { method = "DocumentPresented", checkedOn = Today, notes = "RG conferido", version = request.GetProperty("version").GetUInt32() }));
        verified.StatusCode.Should().Be(HttpStatusCode.OK, await verified.Content.ReadAsStringAsync());
        return await JsonAsync(verified);
    }

    /// <summary>Exporta, roda o job capturado (só ids nos argumentos) e devolve o pedido atualizado.</summary>
    private async Task<JsonElement> ExportAsync(Actor owner, JsonElement request)
    {
        var id = request.GetProperty("id").GetGuid();
        var started = await owner.Client.SendAsync(Post($"{Api}/{id}/export", new { version = request.GetProperty("version").GetUInt32() }));
        started.StatusCode.Should().Be(HttpStatusCode.Accepted, await started.Content.ReadAsStringAsync());
        var job = _jobs.Created.Last(j => j.Job.Type == typeof(PrivacyExportJob)).Job;
        job.Args[0].Should().Be(tenancy.TenantA.Id, "job com tenantId (§4.3)");
        job.Args[1].Should().BeEquivalentTo(new PrivacyExportArgs(id, owner.Id), "só ids nos argumentos");
        await ActivatorUtilities.CreateInstance<PrivacyExportJob>(Factory.Services).ExecuteAsync(tenancy.TenantA.Id, (PrivacyExportArgs)job.Args[1], CancellationToken.None);
        return await JsonAsync(await owner.Client.GetAsync($"{Api}/{id}"));
    }

    /// <summary>Baixa pelo link de uso único e abre o ZIP: o <c>dados.json</c> em texto e os nomes das entradas.</summary>
    private static async Task<(string Json, IReadOnlyList<string> Entries, HttpResponseMessage Response)> DownloadAsync(HttpClient owner, Guid id)
    {
        var link = await owner.SendAsync(Post($"{Api}/{id}/export/link", null));
        link.StatusCode.Should().Be(HttpStatusCode.OK, await link.Content.ReadAsStringAsync());
        var url = (await JsonAsync(link)).GetProperty("url").GetString()!;
        var response = await owner.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("dados.json")!.Open(), Encoding.UTF8);
        return (await reader.ReadToEndAsync(), zip.Entries.Select(e => e.FullName).ToList(), response);
    }

    private static async Task<Guid> PositionAsync(HttpClient hr)
    {
        var schedule = (await JsonAsync(await hr.GetAsync("/api/v1/hr/schedules"))).EnumerateArray().First().GetProperty("id").GetGuid();
        var created = await hr.PostAsJsonAsync("/api/v1/hr/positions", new { name = "Montador " + Guid.NewGuid().ToString("N")[..6], cbo = "7243-15", defaultWorkCenterId = (Guid?)null, workScheduleId = schedule, monthlyHours = (decimal?)null });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    private static async Task<(Guid Id, string Name, string Cpf)> EmployeeAsync(HttpClient owner)
    {
        var cpf = NewCpf();
        var name = "Funcionaria " + Guid.NewGuid().ToString("N")[..6] + " Lima";
        var created = await owner.PostAsJsonAsync("/api/v1/hr/employees", new { name, cpf, birthDate = "1990-05-10", admissionDate = "2020-01-02", positionId = await PositionAsync(owner), workScheduleId = (Guid?)null, baseSalary = 3_000m, registration = (string?)null });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return ((await JsonAsync(created)).GetProperty("employee").GetProperty("id").GetGuid(), name, cpf);
    }

    private static string NewCpf()
    {
        var digits = Guid.NewGuid().ToString("N").Where(char.IsAsciiDigit).Concat("98765432").Take(9).Select(c => c - '0').ToList();
        int Dv(int size) { var r = digits.Take(size).Select((x, i) => x * (size + 1 - i)).Sum() * 10 % 11; return r == 10 ? 0 : r; }
        digits.Add(Dv(9));
        digits.Add(Dv(10));
        return string.Concat(digits);
    }

    private Task<int> AuditCountAsync(Guid id, string action) =>
        InTenantAsync(sp => sp.GetRequiredService<BemAquiDbContext>().AuditEntries.CountAsync(a => a.Entity == nameof(PrivacyRequest) && a.EntityId == id.ToString() && a.Action == action));
}
