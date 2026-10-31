using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Domain;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BemAqui.Integration.Tests.Identity;

/// <summary>
/// PRD-03b (ADR 0026): aparelho da fábrica registrado, PIN e sessão por PIN, com os testes de segurança do gate (CA 2, 7, 10, 13, 14,
/// 15, 16, 17, 19 e 21). Banco desta classe (fixture própria): o teto de aparelhos fica alto, e o teste do teto o ajusta e devolve.
/// </summary>
public partial class ShopfloorPinTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Origin = "https://a.localhost";
    private const string DeviceCookie = "__Host-bq_dev";
    private const string RefreshCookie = "__Host-bq_rt";
    private const string GoodPin = "483920";

    private static DateOnly Today => BusinessTime.Today(TimeProvider.System);

    private static string NewEmail() => "pin-" + Guid.NewGuid().ToString("N")[..10] + "@konebras.com.br";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private static List<string> Amr(string token) => new JsonWebToken(token).Claims.Where(c => c.Type == "amr").Select(c => c.Value).ToList();

    private static string? CookieOf(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values.LastOrDefault(c => c.StartsWith(name + "=", StringComparison.Ordinal)) : null;

    private static string ValueOf(string setCookie) => setCookie.Split(';')[0][(setCookie.IndexOf('=', StringComparison.Ordinal) + 1)..];

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task<T> WithDbAsync<T>(Func<TenantDbContext, Task<T>> action)
    {
        await using var scope = await ScopeAsync();
        return await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    private async Task WithDbAsync(Func<TenantDbContext, Task> action)
    {
        await using var scope = await ScopeAsync();
        await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    private HttpClient Client(WebApplicationFactory<Program>? factory = null)
    {
        var client = (factory ?? tenancy.Factory).CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        return client;
    }

    /// <summary>Teto alto para esta classe: cada teste registra o próprio aparelho.</summary>
    /// <summary>
    /// Um Proprietário ativo criado pelo teste: os avisos de segurança vão aos Proprietários ativos, e outros testes do mesmo tenant
    /// desativam usuários por um tempo (segregação da qualidade).
    /// </summary>
    private Task<(Guid Id, string Email)> OwnerOnDutyAsync() => UserAsync(Group.OwnerSeedKey, employee: false, name: "Proprietário de plantão");

    private Task HighCapAsync(string value = "100") => WithDbAsync(async db =>
    {
        var row = await db.TenantSettings.FirstOrDefaultAsync(s => s.Key == TenantSetting.MaxShopfloorDevices);
        if (row is null)
        {
            db.TenantSettings.Add(new TenantSetting { Key = TenantSetting.MaxShopfloorDevices, Value = value, UpdatedAt = DateTimeOffset.UtcNow });
        }
        else
        {
            row.Value = value;
        }

        await db.SaveChangesAsync();
    });

    /// <summary>Usuário ativo com a senha padrão, em grupos do seed ou num grupo com as ações ("production:view,edit;shopfloor:view,create").</summary>
    private async Task<(Guid Id, string Email)> UserAsync(string? groups, bool employee = true, bool onLeave = false, string name = "Diego Santos")
    {
        var email = NewEmail();
        var (id, _) = await tenancy.SeedUserAsync(tenancy.TenantA, email);
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == id);
        user.Name = name;
        foreach (var spec in (groups ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            Guid groupId;
            if (spec.Contains(':', StringComparison.Ordinal))
            {
                var group = new Group { Name = "Grupo " + Guid.NewGuid().ToString("N")[..8], Description = "Teste" };
                db.Groups.Add(group);
                foreach (var part in spec.Split(';'))
                {
                    var (module, actions) = (part.Split(':')[0], part.Split(':')[1].Split(','));
                    db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
                    foreach (var action in actions)
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

        if (employee)
        {
            var position = new JobPosition { Name = "Montador " + Guid.NewGuid().ToString("N")[..6], MonthlyHours = 220m, CreatedAt = DateTimeOffset.UtcNow };
            db.JobPositions.Add(position);
            var person = new Employee
            {
                Name = name, PositionId = position.Id, AdmissionDate = Today.AddYears(-1),
                Cpf = Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture),
                BirthDate = new DateOnly(1985, 3, 15), BaseSalary = 3000m, CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Employees.Add(person);
            if (onLeave)
            {
                db.EmployeeHistory.Add(new EmployeeHistoryEntry
                {
                    EmployeeId = person.Id, Kind = EmployeeHistoryKind.Leave, EffectiveDate = Today.AddDays(-1), LeaveEndDate = Today.AddDays(5),
                    LeaveType = AbsenceReason.MedicalCertificate, NewValue = "Afastado", Reason = "Atestado", CreatedAt = DateTimeOffset.UtcNow,
                });
            }

            user.EmployeeId = person.Id;
        }

        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IPermissionService>().BumpAsync([id]);
        return (id, email);
    }

    private const string Operator = "operators";
    private const string Supervisor = "production:view,edit;shopfloor:view,create";

    /// <summary>Login completo pela API (o token real tem o <c>amr</c>); <paramref name="device"/> é o cookie do aparelho, se houver.</summary>
    private async Task<(string Token, HttpResponseMessage Response)> FullLoginAsync(string email, bool ephemeral = true, string? device = null, WebApplicationFactory<Program>? factory = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email, password = TenancyFixture.DefaultPassword, ephemeral }) };
        if (device is not null)
        {
            request.Headers.Add("Cookie", DeviceCookie + "=" + device);
        }

        var response = await Client(factory).SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return ((await JsonAsync(response)).GetProperty("accessToken").GetString()!, response);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string token, string? device = null, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (device is not null)
        {
            request.Headers.Add("Cookie", DeviceCookie + "=" + device);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    /// <summary>Registra um aparelho com um supervisor novo (login completo no tablet) e devolve o segredo do cookie e o id.</summary>
    private async Task<(string Secret, Guid DeviceId)> DeviceAsync(string name = "Tablet Linha 1")
    {
        await HighCapAsync();
        await OwnerOnDutyAsync();
        var (_, email) = await UserAsync(Supervisor);
        var (token, _) = await FullLoginAsync(email);
        var response = await Client().SendAsync(Authorized(HttpMethod.Post, "/api/v1/shopfloor/device", token, body: new { name }));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var secret = ValueOf(CookieOf(response, DeviceCookie)!);
        return (secret, (await JsonAsync(response)).GetProperty("deviceId").GetGuid());
    }

    /// <summary>Operador com PIN criado no aparelho (login completo no tablet, depois "Criar PIN").</summary>
    private async Task<(Guid UserId, string Email)> OperatorWithPinAsync(string device, string groups = Operator, string pin = GoodPin)
    {
        var (id, email) = await UserAsync(groups);
        var (token, _) = await FullLoginAsync(email, device: device);
        var created = await Client().SendAsync(Authorized(HttpMethod.Post, "/api/v1/shopfloor/device/pin", token, device, new { pin, confirmation = pin }));
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        return (id, email);
    }

    private static HttpRequestMessage PinRequest(string? device, Guid userId, string pin, bool csrf = true, string? origin = Origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/pin") { Content = JsonContent.Create(new { userId, pin }) };
        if (device is not null)
        {
            request.Headers.Add("Cookie", DeviceCookie + "=" + device);
        }

        if (csrf)
        {
            request.Headers.Add("X-CSRF", "1");
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return request;
    }

    private static HttpRequestMessage PinUsersRequest(string? device, bool csrf = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/pin/users");
        if (device is not null)
        {
            request.Headers.Add("Cookie", DeviceCookie + "=" + device);
        }

        if (csrf)
        {
            request.Headers.Add("X-CSRF", "1");
        }

        return request;
    }

    /// <summary>Entrada por PIN aceita: token, segredo novo do aparelho e o refresh da sessão por PIN.</summary>
    private async Task<(string Token, string Device, string Refresh)> PinLoginAsync(string device, Guid userId, string pin = GoodPin, WebApplicationFactory<Program>? factory = null)
    {
        var response = await Client(factory).SendAsync(PinRequest(device, userId, pin));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await JsonAsync(response);
        return (body.GetProperty("accessToken").GetString()!, ValueOf(CookieOf(response, DeviceCookie)!), ValueOf(CookieOf(response, RefreshCookie)!));
    }

    private static HttpRequestMessage RefreshRequest(string refresh, string? device)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Cookie", RefreshCookie + "=" + refresh + (device is null ? string.Empty : "; " + DeviceCookie + "=" + device));
        request.Headers.Add("X-CSRF", "1");
        request.Headers.Add("Origin", Origin);
        return request;
    }

    private async Task<List<string>> NotificationsAsync(string startsWith) =>
        await WithDbAsync(db => db.Notifications.Where(n => n.Title.StartsWith(startsWith)).Select(n => n.Title).ToListAsync());

    private async Task SetShopfloorReleasedAsync(bool released)
    {
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules
                .Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == "shopfloor").ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    [Fact]
    public async Task Registro_grava_so_o_hash_cookie_Host_por_180_dias_e_avisa_o_Proprietario()
    {
        await HighCapAsync();
        await OwnerOnDutyAsync();
        var (_, email) = await UserAsync(Supervisor, name: "Rita Prado");
        var (token, _) = await FullLoginAsync(email);

        var response = await Client().SendAsync(Authorized(HttpMethod.Post, "/api/v1/shopfloor/device", token, body: new { name = "Tablet Linha 7" }));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var cookie = CookieOf(response, DeviceCookie)!;
        cookie.ToLowerInvariant().Should().Contain("httponly").And.Contain("secure").And.Contain("samesite=strict").And.Contain("path=/").And.NotContain("domain=");
        var secret = ValueOf(cookie);
        var id = (await JsonAsync(response)).GetProperty("deviceId").GetGuid();
        var device = await WithDbAsync(db => db.ShopfloorDevices.AsNoTracking().SingleAsync(d => d.Id == id));
        device.SecretHash.Should().Be(OpaqueToken.Hash(secret)).And.NotBe(secret);
        device.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(180), TimeSpan.FromMinutes(1));
        device.Name.Should().Be("Tablet Linha 7");
        (await NotificationsAsync("Aparelho Tablet Linha 7 registrado por Rita Prado")).Should().NotBeEmpty("todos os membros do Proprietário são avisados (CA 4)");

        var again = await Client().SendAsync(Authorized(HttpMethod.Post, "/api/v1/shopfloor/device", token, secret, new { name = "Outro" }));
        again.StatusCode.Should().Be(HttpStatusCode.Conflict, "o mesmo tablet não registra duas vezes");
        (await Client().SendAsync(Authorized(HttpMethod.Post, "/api/v1/shopfloor/device", token, body: new { name = new string('x', 41) }))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Teto_de_aparelhos_ativos_responde_409()
    {
        var active = await WithDbAsync(db => db.ShopfloorDevices.CountAsync(d => d.RevokedAt == null && d.ExpiresAt > DateTimeOffset.UtcNow));
        var (_, email) = await UserAsync(Supervisor);
        var (token, _) = await FullLoginAsync(email);
        await HighCapAsync(Math.Max(active, 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            if (active == 0)
            {
                (await Client().SendAsync(Authorized(HttpMethod.Post, "/api/v1/shopfloor/device", token, body: new { name = "Primeiro" }))).StatusCode.Should().Be(HttpStatusCode.Created);
            }

            var refused = await Client().SendAsync(Authorized(HttpMethod.Post, "/api/v1/shopfloor/device", token, body: new { name = "Um a mais" }));

            refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var problem = await JsonAsync(refused);
            Code(problem).Should().Be("device_limit");
            problem.GetProperty("detail").GetString().Should().MatchRegex("^Limite de \\d+ aparelhos ativos. Revogue um antes de registrar outro.$");
        }
        finally
        {
            await HighCapAsync();
        }
    }

    [Fact]
    public async Task Login_completo_no_tablet_nao_deixa_sessao_persistente()
    {
        var (id, email) = await UserAsync(Supervisor);

        var (token, response) = await FullLoginAsync(email, ephemeral: true);

        CookieOf(response, RefreshCookie).Should().Match(c => c == null || c.Contains("expires=Thu, 01 Jan 1970", StringComparison.Ordinal), "nenhum refresh emitido (CA 6)");
        (await WithDbAsync(db => db.RefreshTokens.CountAsync(t => t.UserId == id))).Should().Be(0);
        Amr(token).Should().Equal(["pwd"]);

        var (device, _) = await DeviceAsync();
        var (_, onTablet) = await FullLoginAsync(email, ephemeral: false, device: device);
        CookieOf(onTablet, RefreshCookie).Should().Match(c => c == null || c.Contains("expires=Thu, 01 Jan 1970", StringComparison.Ordinal), "com o cookie do aparelho, o login completo nunca persiste");
        (await WithDbAsync(db => db.RefreshTokens.CountAsync(t => t.UserId == id))).Should().Be(0);

        var (_, normal) = await FullLoginAsync(email, ephemeral: false);
        CookieOf(normal, RefreshCookie).Should().NotBeNull().And.NotContain("1970", "fora do tablet a sessão completa continua como está");
    }

    [Fact]
    public async Task Entrada_por_PIN_troca_o_segredo_e_reapresentar_o_anterior_revoga_o_aparelho()
    {
        await using var strict = tenancy.Factory.WithWebHostBuilder(b => b.UseSetting("Auth:RefreshReuseTolerance", "00:00:00"));
        var (device, deviceId) = await DeviceAsync();
        var (userId, _) = await OperatorWithPinAsync(device);

        var (_, rotated, _) = await PinLoginAsync(device, userId, factory: strict);

        rotated.Should().NotBe(device);
        var row = await WithDbAsync(db => db.ShopfloorDevices.AsNoTracking().SingleAsync(d => d.Id == deviceId));
        row.SecretHash.Should().Be(OpaqueToken.Hash(rotated), "só o hash do segredo novo (CA 7)");
        row.PreviousSecretHash.Should().Be(OpaqueToken.Hash(device));
        await Task.Delay(50);

        (await Client(strict).SendAsync(PinUsersRequest(device))).StatusCode.Should().Be(HttpStatusCode.NotFound, "o segredo anterior reapresentado");
        row = await WithDbAsync(db => db.ShopfloorDevices.AsNoTracking().SingleAsync(d => d.Id == deviceId));
        row.RevokedAt.Should().NotBeNull();
        row.RevokedReason.Should().Be("secret_reused");
        (await Client(strict).SendAsync(PinUsersRequest(rotated))).StatusCode.Should().Be(HttpStatusCode.NotFound, "o aparelho inteiro caiu, como a família de refresh");
    }

    [Fact]
    public async Task Job_diario_revoga_aparelho_sem_uso_ha_30_dias_e_o_vencido_com_as_sessoes()
    {
        var (unused, unusedId) = await DeviceAsync("Sem uso");
        var (expired, expiredId) = await DeviceAsync("Vencido");
        var (fresh, freshId) = await DeviceAsync("Em uso");
        var (userId, _) = await OperatorWithPinAsync(unused);
        var (token, rotated, _) = await PinLoginAsync(unused, userId);
        await WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlAsync($"UPDATE identity.shopfloor_devices SET last_seen_at = now() - interval '31 days' WHERE id = {unusedId}");
            await db.Database.ExecuteSqlAsync($"UPDATE identity.shopfloor_devices SET expires_at = now() - interval '1 minute' WHERE id = {expiredId}");
        });

        await using (var scope = await ScopeAsync())
        {
            (await scope.ServiceProvider.GetRequiredService<BemAqui.Identity.Application.Shopfloor.ShopfloorDeviceService>().RevokeStaleAsync()).Should().BeGreaterThanOrEqualTo(2);
        }

        var devices = await WithDbAsync(db => db.ShopfloorDevices.AsNoTracking().Where(d => d.Id == unusedId || d.Id == expiredId || d.Id == freshId).ToDictionaryAsync(d => d.Id));
        devices[unusedId].RevokedReason.Should().Be("unused");
        devices[expiredId].RevokedReason.Should().Be("expired");
        devices[freshId].RevokedAt.Should().BeNull();
        (await WithDbAsync(db => db.RefreshTokens.CountAsync(t => t.DeviceId == unusedId && t.RevokedAt == null))).Should().Be(0, "as sessões por PIN do aparelho caem (CA 8)");
        (await Client().SendAsync(Authorized(HttpMethod.Get, "/api/v1/shopfloor/queue", token, rotated))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Client().SendAsync(PinUsersRequest(expired))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Client().SendAsync(PinUsersRequest(fresh))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Tema_fica_no_aparelho_para_todos_os_operadores()
    {
        var (device, _) = await DeviceAsync();
        var (first, _) = await OperatorWithPinAsync(device);
        var (token, rotated, _) = await PinLoginAsync(device, first);

        var saved = await Client().SendAsync(Authorized(HttpMethod.Put, "/api/v1/shopfloor/device/theme", token, rotated, new { theme = "Dark" }));

        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        var users = await JsonAsync(await Client().SendAsync(PinUsersRequest(rotated)));
        users.GetProperty("device").GetProperty("theme").GetString().Should().Be("Dark", "padrão Automático; vale para todos os operadores do tablet (CA 11)");
    }
}