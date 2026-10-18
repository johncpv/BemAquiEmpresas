using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>FND-05 CA 8 a 10: tenant que exige 2FA leva à configuração; desativar exige senha e código e é barrado quando obrigatório.</summary>
public partial class AuthTests
{
    private const string HostB = "b.localhost";

    private async Task RequireTwoFactorInTenantBAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantB.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
        if (!db.TenantSettings.Any(s => s.Key == TenantSetting.RequireTwoFactor))
        {
            db.TenantSettings.Add(new TenantSetting { Key = TenantSetting.RequireTwoFactor, Value = "true", UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Tenant_que_exige_2FA_so_libera_a_configuracao_e_nao_deixa_desativar()
    {
        await RequireTwoFactorInTenantBAsync();
        var email = NewEmail();
        await tenancy.SeedUserAsync(tenancy.TenantB, email);
        var b = tenancy.ClientFor(HostB);

        var login = await JsonAsync(await b.PostAsJsonAsync("/api/v1/auth/login", new { email, password = TenancyFixture.DefaultPassword }));
        login.GetProperty("accessToken").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        login.GetProperty("twoFactor").GetProperty("purpose").GetString().Should().Be("setup");
        var challenge = login.GetProperty("twoFactor").GetProperty("challenge").GetString()!;

        (await b.PostAsJsonAsync("/api/v1/auth/2fa/verify", new { challenge, code = "000000" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "desafio de configuração não serve para entrar");
        var setup = await JsonAsync(await b.PostAsJsonAsync("/api/v1/auth/2fa/setup", new { challenge }));
        var secret = Base32.Decode(setup.GetProperty("manualKey").GetString()!);
        var confirmed = await b.PostAsJsonAsync("/api/v1/auth/2fa/confirm", new { challenge, code = Totp.Code(secret, Totp.StepAt(DateTimeOffset.UtcNow)) });

        confirmed.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = (await JsonAsync(confirmed)).GetProperty("accessToken").GetString()!;
        (await JsonAsync(await Authorized(token, HostB).GetAsync("/api/v1/me/2fa"))).GetProperty("required").GetBoolean().Should().BeTrue();
        var disable = await Authorized(token, HostB).PostAsJsonAsync("/api/v1/me/2fa/disable", new { password = TenancyFixture.DefaultPassword, code = NextCode(secret) });
        disable.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(disable)).GetProperty("code").GetString().Should().Be("auth.2fa_required");
    }

    [Fact]
    public async Task Desativar_exige_senha_e_codigo_validos()
    {
        var user = await UserWithTwoFactorAsync();
        var me = Authorized(user.AccessToken);

        (await me.PostAsJsonAsync("/api/v1/me/2fa/disable", new { password = "senha errada bem longa", code = NextCode(user.Secret) }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var disabled = await me.PostAsJsonAsync("/api/v1/me/2fa/disable", new { password = TenancyFixture.DefaultPassword, code = NextCode(user.Secret) });

        disabled.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = (await JsonAsync(disabled)).GetProperty("accessToken").GetString()!;
        var status = await JsonAsync(await Authorized(token).GetAsync("/api/v1/me/2fa"));
        status.GetProperty("enabled").GetBoolean().Should().BeFalse();
        status.GetProperty("recoveryCodesLeft").GetInt32().Should().Be(0);
        (await JsonAsync(await LoginAsync(user.Email, TenancyFixture.DefaultPassword))).GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
    }
}
