using System.Globalization;
using System.Net;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Security;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Observability;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace BemAqui.Integration.Tests.Identity;

public partial class ShopfloorPinTests
{
    [Fact]
    public async Task Rotas_do_PIN_sem_cookie_do_aparelho_respondem_404_e_exigem_origem_e_cabecalho()
    {
        var (device, _) = await DeviceAsync();
        var (userId, _) = await OperatorWithPinAsync(device);
        var client = Client();

        (await client.SendAsync(PinUsersRequest(null))).StatusCode.Should().Be(HttpStatusCode.NotFound, "sem o cookie a lista não existe (CA 13)");
        (await client.SendAsync(PinRequest(null, userId, GoodPin))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.SendAsync(PinUsersRequest("segredo-qualquer"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.SendAsync(PinUsersRequest(device, csrf: false))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.SendAsync(PinRequest(device, userId, GoodPin, csrf: false))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.SendAsync(PinRequest(device, userId, GoodPin, origin: "https://golpe.example.com"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var list = await JsonAsync(await client.SendAsync(PinUsersRequest(device)));
        var users = list.GetProperty("users").EnumerateArray().ToList();
        users.Should().ContainSingle(u => u.GetProperty("userId").GetGuid() == userId);
        users.Single(u => u.GetProperty("userId").GetGuid() == userId).GetProperty("displayName").GetString().Should().Be("Diego S.", "só primeiro nome e inicial (ponto 69)");
        list.ToString().Should().NotContain("@", "nenhum e-mail no tablet");
    }

    [Fact]
    public async Task Usuario_sem_PIN_e_PIN_errado_tem_a_mesma_resposta()
    {
        var (device, _) = await DeviceAsync();
        var (userId, _) = await OperatorWithPinAsync(device);

        var unknown = await Client().SendAsync(PinRequest(device, Guid.NewGuid(), GoodPin));
        var wrong = await Client().SendAsync(PinRequest(device, userId, "705218"));

        unknown.StatusCode.Should().Be(wrong.StatusCode);
        (await JsonAsync(unknown)).GetProperty("detail").GetString().Should().Be((await JsonAsync(wrong)).GetProperty("detail").GetString()).And.Be("PIN incorreto. Confira e tente de novo.");
    }

    [Fact]
    public async Task Rotas_do_PIN_entram_no_rate_limit_do_login()
    {
        await using var limited = tenancy.Factory.WithWebHostBuilder(b => b.UseSetting("RateLimit:Auth:PerIpPerMinute", "3"));
        var (device, _) = await DeviceAsync();
        var client = Client(limited);
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            statuses.Add((await client.SendAsync(PinUsersRequest(device))).StatusCode);
        }

        statuses.Should().Contain(HttpStatusCode.TooManyRequests, "mesmo limite do login (ADR 0010 item 6)");
    }

    [Fact]
    public async Task Cinco_erros_bloqueiam_o_par_e_o_decimo_em_24h_apaga_o_PIN()
    {
        var (device, deviceId) = await DeviceAsync();
        var (userId, _) = await OperatorWithPinAsync(device);
        for (var i = 0; i < 4; i++)
        {
            (await JsonAsync(await Client().SendAsync(PinRequest(device, userId, "705218")))).GetProperty("code").GetString().Should().Be("auth.pin_invalid");
        }

        Code(await JsonAsync(await Client().SendAsync(PinRequest(device, userId, "705218")))).Should().Be("auth.pin_locked");
        var locked = await JsonAsync(await Client().SendAsync(PinRequest(device, userId, GoodPin)));
        Code(locked).Should().Be("auth.pin_locked", "bloqueado mesmo com o PIN certo (CA 14)");
        locked.GetProperty("detail").GetString().Should().Be("PIN bloqueado por 15 minutos. Entre com e-mail e senha ou espere.");

        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE identity.user_pins SET locked_until = now() - interval '1 minute' WHERE user_id = {userId}"));
        for (var i = 0; i < 4; i++)
        {
            await Client().SendAsync(PinRequest(device, userId, "705218"));
        }

        Code(await JsonAsync(await Client().SendAsync(PinRequest(device, userId, "705218")))).Should().Be("auth.pin_erased", "10º erro em 24 h");
        (await WithDbAsync(db => db.UserPins.CountAsync(p => p.UserId == userId && p.DeviceId == deviceId))).Should().Be(0);
        var actions = await WithDbAsync(db => db.AuditEntries.Where(a => a.EntityId == userId.ToString()).Select(a => a.Action).ToListAsync());
        actions.Should().Contain(["PinLoginFailed", "PinLocked", "PinLoginBlocked", "PinErased"]);
    }

    [Fact]
    public async Task Vinte_erros_no_aparelho_em_15_minutos_bloqueiam_e_avisam_o_Proprietario()
    {
        var (device, deviceId) = await DeviceAsync("Tablet do abuso");
        var (userId, _) = await OperatorWithPinAsync(device);
        for (var i = 0; i < 20; i++)
        {
            await Client().SendAsync(PinRequest(device, Guid.NewGuid(), "705218"));
        }

        var refused = await JsonAsync(await Client().SendAsync(PinRequest(device, userId, GoodPin)));

        Code(refused).Should().Be("auth.device_pin_locked", "somando todos os usuários (CA 14)");
        (await NotificationsAsync("Entrada por PIN bloqueada no aparelho Tablet do abuso")).Should().NotBeEmpty();
        (await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.EntityId == deviceId.ToString() && a.Action == "DevicePinLocked"))).Should().Be(1);
    }

    [Fact]
    public async Task Sessao_por_PIN_tem_amr_pin_refresh_preso_ao_aparelho_e_12_horas_sem_renovar_alem()
    {
        var (device, deviceId) = await DeviceAsync();
        var (userId, _) = await OperatorWithPinAsync(device);

        var (token, rotated, refresh) = await PinLoginAsync(device, userId);

        Amr(token).Should().Equal(["pin"], "amr = [\"pin\"] (ADR 0008, adendo)");
        var row = await WithDbAsync(db => db.RefreshTokens.AsNoTracking().SingleAsync(t => t.TokenHash == OpaqueToken.Hash(refresh)));
        row.SessionKind.Should().Be(SessionKind.Pin);
        row.DeviceId.Should().Be(deviceId);
        row.AbsoluteExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(12), TimeSpan.FromMinutes(1));
        row.ExpiresAt.Should().Be(row.AbsoluteExpiresAt!.Value, "não renovável além das 12 h");

        var renewed = await Client().SendAsync(RefreshRequest(refresh, rotated));
        renewed.StatusCode.Should().Be(HttpStatusCode.OK);
        Amr((await JsonAsync(renewed)).GetProperty("accessToken").GetString()!).Should().Equal(["pin"], "o refresh por PIN não vira sessão completa");
        var next = ValueOf(CookieOf(renewed, RefreshCookie)!);
        (await WithDbAsync(db => db.RefreshTokens.AsNoTracking().SingleAsync(t => t.TokenHash == OpaqueToken.Hash(next)))).AbsoluteExpiresAt.Should().Be(row.AbsoluteExpiresAt);

        (await Client().SendAsync(RefreshRequest(next, null))).IsSuccessStatusCode.Should().BeFalse("sem o cookie do aparelho, a sessão por PIN não renova");
        (await Client().SendAsync(Authorized(System.Net.Http.HttpMethod.Get, "/api/v1/shopfloor/queue", token, rotated))).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a família foi revogada");
    }

    [Fact]
    public async Task Sessao_por_PIN_passada_das_12_horas_nao_renova()
    {
        var (device, _) = await DeviceAsync();
        var (userId, _) = await OperatorWithPinAsync(device);
        var (_, rotated, refresh) = await PinLoginAsync(device, userId);
        var hash = OpaqueToken.Hash(refresh);
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE identity.refresh_tokens SET absolute_expires_at = now() - interval '1 minute' WHERE token_hash = {hash}"));

        (await Client().SendAsync(RefreshRequest(refresh, rotated))).IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task Sessao_completa_tem_prazo_absoluto_de_30_dias_tambem_nas_familias_antigas()
    {
        var (id, email) = await UserAsync(null, employee: false);
        var (_, login) = await FullLoginAsync(email, ephemeral: false);
        var refresh = ValueOf(CookieOf(login, RefreshCookie)!);
        var row = await WithDbAsync(db => db.RefreshTokens.AsNoTracking().SingleAsync(t => t.TokenHash == OpaqueToken.Hash(refresh)));
        row.SessionKind.Should().Be(SessionKind.Full);
        row.AbsoluteExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(30), TimeSpan.FromMinutes(1));
        row.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(7), TimeSpan.FromMinutes(1), "a renovação de 7 dias continua como está (CA 21)");

        var hash = OpaqueToken.Hash(refresh);
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE identity.refresh_tokens SET absolute_expires_at = NULL, created_at = now() - interval '31 days' WHERE token_hash = {hash}"));
        (await Client().SendAsync(RefreshRequest(refresh, null))).IsSuccessStatusCode.Should().BeFalse("família aberta antes do deploy conta do início dela");
        (await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.EntityId == id.ToString() && a.Action == "SessionAbsoluteExpired"))).Should().Be(1);

        var (_, recent) = await FullLoginAsync(email, ephemeral: false);
        var fresh = ValueOf(CookieOf(recent, RefreshCookie)!);
        var freshHash = OpaqueToken.Hash(fresh);
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE identity.refresh_tokens SET absolute_expires_at = NULL, created_at = now() - interval '1 day' WHERE token_hash = {freshHash}"));
        var renewed = await Client().SendAsync(RefreshRequest(fresh, null));
        renewed.StatusCode.Should().Be(HttpStatusCode.OK, "dentro do prazo, renova");
        var child = ValueOf(CookieOf(renewed, RefreshCookie)!);
        (await WithDbAsync(db => db.RefreshTokens.AsNoTracking().SingleAsync(t => t.TokenHash == OpaqueToken.Hash(child)))).AbsoluteExpiresAt
            .Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(29), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task Refresh_de_outro_host_do_tenant_nao_renova()
    {
        var (_, email) = await UserAsync(null, employee: false);
        var (_, login) = await FullLoginAsync(email, ephemeral: false);
        var refresh = ValueOf(CookieOf(login, RefreshCookie)!);
        var hash = OpaqueToken.Hash(refresh);
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE identity.refresh_tokens SET host = 'outro.konebras.com.br' WHERE token_hash = {hash}"));

        (await Client().SendAsync(RefreshRequest(refresh, null))).IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task Criar_PIN_so_depois_do_login_completo_num_aparelho_registrado_e_PIN_obvio_e_recusado()
    {
        var (device, deviceId) = await DeviceAsync();
        var (userId, email) = await UserAsync(Operator);
        var (token, _) = await FullLoginAsync(email, device: device);

        Code(await JsonAsync(await Client().SendAsync(Authorized(System.Net.Http.HttpMethod.Post, "/api/v1/shopfloor/device/pin", token, null, new { pin = GoodPin, confirmation = GoodPin }))))
            .Should().Be("device_not_registered", "sem aparelho registrado não há PIN (CA 12)");
        (await Client().SendAsync(Authorized(System.Net.Http.HttpMethod.Post, "/api/v1/shopfloor/device/pin", token, device, new { pin = "123456", confirmation = "123456" }))).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity);
        (await Client().SendAsync(Authorized(System.Net.Http.HttpMethod.Post, "/api/v1/shopfloor/device/pin", token, device, new { pin = "150385", confirmation = "150385" }))).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "data de nascimento do funcionário");
        (await Client().SendAsync(Authorized(System.Net.Http.HttpMethod.Post, "/api/v1/shopfloor/device/pin", token, device, new { pin = GoodPin, confirmation = GoodPin }))).StatusCode
            .Should().Be(HttpStatusCode.OK);

        var pin = await WithDbAsync(db => db.UserPins.AsNoTracking().SingleAsync(p => p.UserId == userId && p.DeviceId == deviceId));
        pin.PinHash.Should().NotContain(GoodPin);
        await using var scope = await ScopeAsync();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var user = await scope.ServiceProvider.GetRequiredService<BemAqui.Identity.Application.IIdentityDb>().Users.SingleAsync(u => u.Id == userId);
        hasher.VerifyHashedPassword(user, pin.PinHash, GoodPin).Should().NotBe(PasswordVerificationResult.Failed, "PBKDF2 do PasswordHasher");

        var (pinToken, rotated, _) = await PinLoginAsync(device, userId);
        var byPin = await Client().SendAsync(Authorized(System.Net.Http.HttpMethod.Post, "/api/v1/shopfloor/device/pin", pinToken, rotated, new { pin = "705218", confirmation = "705218" }));
        byPin.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(byPin)).Should().Be("pin_session_scope", "criar PIN exige login completo");
    }

    [Fact]
    public async Task Modulo_nao_liberado_e_sem_permissao_seguem_as_tres_camadas()
    {
        var (device, deviceId) = await DeviceAsync();
        var (userId, _) = await OperatorWithPinAsync(device);
        await SetShopfloorReleasedAsync(false);
        try
        {
            var users = await Client().SendAsync(PinUsersRequest(device));
            users.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(users)).Should().Be("module_not_released");
            Code(await JsonAsync(await Client().SendAsync(PinRequest(device, userId, GoodPin)))).Should().Be("module_not_released");
        }
        finally
        {
            await SetShopfloorReleasedAsync(true);
        }

        var (_, viewer) = await UserAsync("production:view;shopfloor:view");
        var (viewerToken, _) = await FullLoginAsync(viewer);
        var register = await Client().SendAsync(Authorized(System.Net.Http.HttpMethod.Post, "/api/v1/shopfloor/device", viewerToken, body: new { name = "Sem permissão" }));
        register.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(register)).Should().Be("forbidden", "registrar exige production.edit e shopfloor.create (CA 22)");
        var revoke = await Client().SendAsync(Authorized(System.Net.Http.HttpMethod.Post, $"/api/v1/settings/security/shopfloor-devices/{deviceId}/revoke", viewerToken));
        revoke.StatusCode.Should().Be(HttpStatusCode.Forbidden, "revogar exige settings.edit ou production.approve");
    }

    [Fact]
    public async Task Auditoria_e_log_registram_quem_qual_aparelho_e_IP_sem_o_PIN_nem_o_segredo()
    {
        var sink = new MemorySink();
        await using var factory = tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<ILogEventSink>(sink)));
        const string canary = "938271";
        var (device, deviceId) = await DeviceAsync();
        var (userId, email) = await UserAsync(Operator);
        var (token, _) = await FullLoginAsync(email, device: device, factory: factory);
        (await Client(factory).SendAsync(Authorized(System.Net.Http.HttpMethod.Post, "/api/v1/shopfloor/device/pin", token, device, new { pin = canary, confirmation = canary }))).EnsureSuccessStatusCode();
        await Client(factory).SendAsync(PinRequest(device, userId, "705218"));
        var (_, rotated, refresh) = await PinLoginAsync(device, userId, canary, factory);

        var audit = await WithDbAsync(db => db.AuditEntries.AsNoTracking()
            .Where(a => a.EntityId == userId.ToString() || a.EntityId == deviceId.ToString()).Select(a => new { a.Action, a.Changes, a.Ip, a.UserId }).ToListAsync());
        audit.Select(a => a.Action).Should().Contain(["DeviceRegistered", "PinCreated", "PinLoginFailed", "PinLoginSucceeded"]);
        audit.Should().Contain(a => a.Action == "PinLoginSucceeded" && a.Changes.Contains(deviceId.ToString()) && a.UserId == userId, "quem, quando e qual aparelho (CA 23)");
        var text = string.Join("\n", audit.Select(a => a.Changes));
        var all = string.Join("\n", await WithDbAsync(db => db.AuditEntries.AsNoTracking().Select(a => a.Changes).ToListAsync()));
        foreach (var secret in new[] { canary, device, rotated, refresh, OpaqueToken.Hash(device), OpaqueToken.Hash(rotated) })
        {
            all.Should().NotContain(secret, "nem o PIN nem o segredo vão para audit.audit_log");
        }

        text.Should().Contain("***", "o hash entra só como alterado");
        string events;
        lock (sink.Events)
        {
            events = string.Join("\n", sink.Events.Select(e => e.RenderMessage(CultureInfo.InvariantCulture) + " " + string.Join(" ", e.Properties.Select(p => p.Value.ToString()))));
        }

        foreach (var secret in new[] { canary, device, rotated, refresh })
        {
            events.Should().NotContain(secret, "nem no log");
        }
    }
}