using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Identity.Application;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>FND-05 CA 1 a 7: ativação, desafio no login, anti-replay, limite de tentativas e código de recuperação.</summary>
public partial class AuthTests
{
    private sealed record TwoFactorUser(string Email, byte[] Secret, List<string> RecoveryCodes, string AccessToken);

    private HttpClient Authorized(string accessToken, string host = Host)
    {
        var client = tenancy.ClientFor(host);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Código do próximo intervalo: o da ativação já foi consumido pelo anti-replay.</summary>
    private static string NextCode(byte[] secret) => Totp.Code(secret, Totp.StepAt(DateTimeOffset.UtcNow) + 1);

    private async Task<TwoFactorUser> UserWithTwoFactorAsync()
    {
        var email = NewEmail();
        await tenancy.SeedUserAsync(tenancy.TenantA, email);
        var token = (await JsonAsync(await LoginAsync(email, TenancyFixture.DefaultPassword))).GetProperty("accessToken").GetString()!;
        var setup = await JsonAsync(await Authorized(token).PostAsync("/api/v1/me/2fa/setup", null));
        var secret = Base32.Decode(setup.GetProperty("manualKey").GetString()!);
        var confirmed = await Authorized(token).PostAsJsonAsync("/api/v1/me/2fa/confirm", new { code = Totp.Code(secret, Totp.StepAt(DateTimeOffset.UtcNow)) });
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await JsonAsync(confirmed);
        return new TwoFactorUser(email, secret, body.GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!).ToList(), body.GetProperty("accessToken").GetString()!);
    }

    private async Task<string> ChallengeAsync(string email)
    {
        var login = await LoginAsync(email, TenancyFixture.DefaultPassword);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        login.Headers.Contains("Set-Cookie").Should().BeFalse("sem o código não há sessão");
        var body = await JsonAsync(login);
        body.GetProperty("accessToken").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("twoFactor").GetProperty("purpose").GetString().Should().Be("verify");
        return body.GetProperty("twoFactor").GetProperty("challenge").GetString()!;
    }

    private Task<HttpResponseMessage> VerifyAsync(string challenge, string? code = null, string? recoveryCode = null) =>
        PostAsync("/api/v1/auth/2fa/verify", new { challenge, code, recoveryCode });

    [Fact]
    public async Task Ativacao_so_liga_com_codigo_valido_e_mostra_10_codigos_de_recuperacao()
    {
        var email = NewEmail();
        await tenancy.SeedUserAsync(tenancy.TenantA, email);
        var token = (await JsonAsync(await LoginAsync(email, TenancyFixture.DefaultPassword))).GetProperty("accessToken").GetString()!;

        var setup = await JsonAsync(await Authorized(token).PostAsync("/api/v1/me/2fa/setup", null));
        setup.GetProperty("otpAuthUri").GetString().Should().StartWith("otpauth://totp/");
        (await JsonAsync(await Authorized(token).GetAsync("/api/v1/me/2fa"))).GetProperty("enabled").GetBoolean().Should().BeFalse();
        (await Authorized(token).PostAsJsonAsync("/api/v1/me/2fa/confirm", new { code = "000000" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var secret = Base32.Decode(setup.GetProperty("manualKey").GetString()!);
        var confirmed = await JsonAsync(await Authorized(token).PostAsJsonAsync("/api/v1/me/2fa/confirm", new { code = Totp.Code(secret, Totp.StepAt(DateTimeOffset.UtcNow)) }));

        confirmed.GetProperty("recoveryCodes").GetArrayLength().Should().Be(10);
        var fresh = confirmed.GetProperty("accessToken").GetString()!;
        var status = await JsonAsync(await Authorized(fresh).GetAsync("/api/v1/me/2fa"));
        status.GetProperty("enabled").GetBoolean().Should().BeTrue();
        status.GetProperty("recoveryCodesLeft").GetInt32().Should().Be(10);
        (await Authorized(token).GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "o stamp mudou e a sessão antiga caiu");
    }

    [Fact]
    public async Task Desafio_nao_abre_outras_rotas_e_codigo_repetido_e_recusado()
    {
        var user = await UserWithTwoFactorAsync();
        var challenge = await ChallengeAsync(user.Email);

        (await Authorized(challenge).GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var code = NextCode(user.Secret);
        var verified = await VerifyAsync(challenge, code);
        verified.StatusCode.Should().Be(HttpStatusCode.OK);
        RefreshCookieOf(verified).Should().NotBeNullOrEmpty();
        (await Authorized((await JsonAsync(verified)).GetProperty("accessToken").GetString()!).GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await VerifyAsync(challenge, code)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "o desafio já foi usado");

        var replay = await VerifyAsync(await ChallengeAsync(user.Email), code);

        replay.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(replay)).GetProperty("code").GetString().Should().Be("auth.2fa_invalid_code");
    }

    [Fact]
    public async Task Cinco_codigos_errados_derrubam_o_desafio()
    {
        var user = await UserWithTwoFactorAsync();
        var challenge = await ChallengeAsync(user.Email);

        for (var i = 0; i < 4; i++)
        {
            (await JsonAsync(await VerifyAsync(challenge, "000000"))).GetProperty("detail").GetString().Should().Contain("Restam " + (4 - i));
        }

        (await JsonAsync(await VerifyAsync(challenge, "000000"))).GetProperty("code").GetString().Should().Be("auth.2fa_too_many_attempts");
        (await JsonAsync(await VerifyAsync(challenge, NextCode(user.Secret)))).GetProperty("code").GetString().Should().Be("auth.2fa_challenge_expired");
    }

    [Fact]
    public async Task Codigo_de_recuperacao_entra_uma_vez_so()
    {
        var user = await UserWithTwoFactorAsync();
        var code = user.RecoveryCodes[3];

        (await VerifyAsync(await ChallengeAsync(user.Email), recoveryCode: code.ToLowerInvariant())).StatusCode.Should().Be(HttpStatusCode.OK);
        (await VerifyAsync(await ChallengeAsync(user.Email), recoveryCode: code)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
