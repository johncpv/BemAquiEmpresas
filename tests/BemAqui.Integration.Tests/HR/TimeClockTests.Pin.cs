using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.HR.Domain;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.HR;

public partial class TimeClockTests
{
    private const string Supervisor = "production:view,edit;shopfloor:view,create";

    private static string? CookieOf(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values.LastOrDefault(c => c.StartsWith(name + "=", StringComparison.Ordinal)) : null;

    private static string ValueOf(string setCookie) => setCookie.Split(';')[0][(setCookie.IndexOf('=', StringComparison.Ordinal) + 1)..];

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

    private async Task<string> FullLoginAsync(string email, string? device = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email, password = TenancyFixture.DefaultPassword, ephemeral = true }) };
        if (device is not null)
        {
            request.Headers.Add("Cookie", DeviceCookie + "=" + device);
        }

        var response = await Client().SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await JsonAsync(response)).GetProperty("accessToken").GetString()!;
    }

    /// <summary>Aparelho registrado por um supervisor (login completo); teto alto e um Proprietário ativo para os avisos de segurança.</summary>
    private async Task<(string Secret, Guid DeviceId)> DeviceAsync()
    {
        await DbAsync(async db =>
        {
            var row = await db.TenantSettings.FirstOrDefaultAsync(s => s.Key == TenantSetting.MaxShopfloorDevices);
            if (row is null)
            {
                db.TenantSettings.Add(new TenantSetting { Key = TenantSetting.MaxShopfloorDevices, Value = "100", UpdatedAt = DateTimeOffset.UtcNow });
            }
            else
            {
                row.Value = "100";
            }

            return await db.SaveChangesAsync();
        });
        await PersonAsync(Group.OwnerSeedKey, employee: false, name: "Proprietário de plantão");
        var supervisor = await PersonAsync(Supervisor, employee: false);
        var token = await FullLoginAsync(supervisor.Email);
        var response = await Client().SendAsync(Authorized(HttpMethod.Post, "/api/v1/shopfloor/device", token, body: new { name = "Tablet Ponto" }));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (ValueOf(CookieOf(response, DeviceCookie)!), (await JsonAsync(response)).GetProperty("deviceId").GetGuid());
    }

    /// <summary>Operador ligado a funcionário, com PIN no aparelho, e a entrada por PIN: token, segredo novo do aparelho e o refresh.</summary>
    private async Task<(Person Person, string Token, string Device, string Refresh)> PinSessionAsync(string device)
    {
        var person = await PersonAsync("operators", name: "Diego Santos");
        var full = await FullLoginAsync(person.Email, device);
        var pin = await Client().SendAsync(Authorized(HttpMethod.Post, "/api/v1/shopfloor/device/pin", full, device, new { pin = GoodPin, confirmation = GoodPin }));
        pin.StatusCode.Should().Be(HttpStatusCode.OK, await pin.Content.ReadAsStringAsync());
        var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/pin") { Content = JsonContent.Create(new { userId = person.UserId, pin = GoodPin }) };
        login.Headers.Add("Cookie", DeviceCookie + "=" + device);
        login.Headers.Add("X-CSRF", "1");
        login.Headers.Add("Origin", Origin);
        var response = await Client().SendAsync(login);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (person, (await JsonAsync(response)).GetProperty("accessToken").GetString()!, ValueOf(CookieOf(response, DeviceCookie)!), ValueOf(CookieOf(response, RefreshCookie)!));
    }

    /// <summary>
    /// CA 7 e 11: na sessão por PIN só "hoje" e "marcar"; o aparelho registrado não pede localização (mesmo com o parâmetro ligado); a
    /// marcação grava a origem PIN e o aparelho, e encerra a sessão por PIN no servidor (refresh revogado, cookie apagado, 401 depois).
    /// </summary>
    [Fact]
    public async Task No_tablet_por_PIN_so_hoje_e_marcar_e_marcar_encerra_a_sessao()
    {
        var (device, deviceId) = await DeviceAsync();
        var (diego, token, rotated, refresh) = await PinSessionAsync(device);
        await SetGeolocationAsync("Ask");
        try
        {
            var today = await Client().SendAsync(Authorized(HttpMethod.Get, Today, token, rotated));
            today.StatusCode.Should().Be(HttpStatusCode.OK, await today.Content.ReadAsStringAsync());
            var clock = await JsonAsync(today);
            clock.GetProperty("registeredDevice").GetBoolean().Should().BeTrue();
            clock.GetProperty("geolocation").GetString().Should().Be("Off", "o tablet registrado não pede localização");
            clock.GetProperty("banner").GetString().Should().Be(Banner);

            foreach (var url in new[] { Entries, "/api/v1/me/hr/payslips", "/api/v1/hr/time-entries" })
            {
                (await CodeAsync(await Client().SendAsync(Authorized(HttpMethod.Get, url, token, rotated)))).Should().Be("pin_session_scope", url);
            }

            (await CodeAsync(await Client().SendAsync(Authorized(HttpMethod.Post, $"{Entries}/geo-consent", token, rotated)))).Should().Be("pin_session_scope");

            var marked = await Client().SendAsync(Mark(new { type = "In", latitude = -23.5505m, longitude = -46.6333m, accuracyM = 10m }, token: token, device: rotated));
            marked.StatusCode.Should().Be(HttpStatusCode.Created, await marked.Content.ReadAsStringAsync());
            var receipt = await JsonAsync(marked);
            receipt.GetProperty("source").GetString().Should().Be("Pin");
            receipt.GetProperty("sessionEnded").GetBoolean().Should().BeTrue();
            receipt.GetProperty("title").GetString().Should().Be("Comprovante de controle interno");
            CookieOf(marked, RefreshCookie).Should().NotBeNull().And.Contain("1970", "o refresh do tablet é apagado na mesma resposta");

            var row = (await EntriesOfAsync(diego.EmployeeId)).Single();
            row.Source.Should().Be(TimeEntrySource.Pin);
            row.DeviceId.Should().Be(deviceId);
            row.Location.Should().Be(TimeEntryLocation.NotAsked);
            row.Latitude.Should().BeNull();
            (await DbAsync(db => db.RefreshTokens.CountAsync(t => t.UserId == diego.UserId && t.DeviceId == deviceId && t.RevokedAt == null))).Should().Be(0);
            (await InTenantAsync(sp => sp.GetRequiredService<BemAquiDbContext>().AuditEntries.CountAsync(a => a.Entity == "Auth" && a.EntityId == diego.UserId.ToString() && a.Action == "PinSessionEnded")))
                .Should().Be(1);

            var after = await Client().SendAsync(Authorized(HttpMethod.Get, Today, token, rotated));
            after.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await CodeAsync(after)).Should().Be("pin_session_ended");
            var renew = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
            renew.Headers.Add("Cookie", RefreshCookie + "=" + refresh + "; " + DeviceCookie + "=" + rotated);
            renew.Headers.Add("X-CSRF", "1");
            renew.Headers.Add("Origin", Origin);
            (await Client().SendAsync(renew)).IsSuccessStatusCode.Should().BeFalse("a família do refresh foi revogada");

            // O PIN continua no aparelho: o próximo turno entra de novo e a origem mostra o aparelho para o RH.
            var hr = await PersonAsync("hr:view", employee: false);
            var listed = await JsonAsync(await hr.Client.GetAsync($"/api/v1/hr/employees/{diego.EmployeeId}/time-entries"));
            listed.GetProperty("items")[0].GetProperty("deviceName").GetString().Should().Be("Tablet Ponto");
        }
        finally
        {
            await SetGeolocationAsync("Off");
        }
    }

    /// <summary>CA 7 (ADR 0026 §2): quem ganha hr.view_sensitive perde a sessão por PIN na hora e o PIN na próxima entrada.</summary>
    [Fact]
    public async Task Quem_ganha_hr_view_sensitive_perde_o_PIN()
    {
        var (device, deviceId) = await DeviceAsync();
        var (diego, token, rotated, _) = await PinSessionAsync(device);
        (await Client().SendAsync(Authorized(HttpMethod.Get, Today, token, rotated))).StatusCode.Should().Be(HttpStatusCode.OK);

        await InTenantAsync(async sp =>
        {
            var db = sp.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
            var group = new Group { Name = "Sensível " + Guid.NewGuid().ToString("N")[..8], Description = "Teste" };
            db.Groups.Add(group);
            db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = "hr", Enabled = true });
            db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = "hr", Action = "view_sensitive" });
            db.UserGroups.Add(new UserGroup { UserId = diego.UserId, GroupId = group.Id });
            await db.SaveChangesAsync();
            await sp.GetRequiredService<IPermissionService>().BumpAsync([diego.UserId]);
            return 0;
        });

        var refused = await Client().SendAsync(Mark(new { type = "In" }, token: token, device: rotated));
        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeAsync(refused)).Should().Be("pin_session_ended");
        var login = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/pin") { Content = JsonContent.Create(new { userId = diego.UserId, pin = GoodPin }) };
        login.Headers.Add("Cookie", DeviceCookie + "=" + rotated);
        login.Headers.Add("X-CSRF", "1");
        login.Headers.Add("Origin", Origin);
        (await Client().SendAsync(login)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await DbAsync(db => db.UserPins.CountAsync(p => p.UserId == diego.UserId && p.DeviceId == deviceId))).Should().Be(0, "o PIN é apagado");
        (await EntriesOfAsync(diego.EmployeeId)).Should().BeEmpty();
    }
}
