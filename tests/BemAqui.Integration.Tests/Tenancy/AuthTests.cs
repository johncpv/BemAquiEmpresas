using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>FND-04a CA 1, 2, 9 e 10: login com cookie seguro, token mínimo, bloqueio e resposta uniforme.</summary>
public partial class AuthTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";

    private static string NewEmail() => "auth-" + Guid.CreateVersion7().ToString("N")[..10] + "@konebras.com.br";

    private Task<HttpResponseMessage> LoginAsync(string email, string password) =>
        tenancy.ClientFor(Host).PostAsJsonAsync("/api/v1/auth/login", new { email, password });

    private static string RefreshCookieOf(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-bq_rt=", StringComparison.Ordinal));

    private static string CookieValue(string setCookie) => setCookie.Split(';')[0]["__Host-bq_rt=".Length..];

    private async Task<List<string>> AuditActionsAsync(Guid userId)
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        return await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().AuditEntries
            .Where(a => a.Entity == "Auth" && a.EntityId == userId.ToString()).OrderBy(a => a.At).Select(a => a.Action).ToListAsync();
    }

    [Fact]
    public async Task Login_devolve_access_token_minimo_e_refresh_em_cookie_seguro()
    {
        var email = NewEmail();
        var (userId, _) = await tenancy.SeedUserAsync(tenancy.TenantA, email);

        var response = await LoginAsync(email, TenancyFixture.DefaultPassword);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookie = RefreshCookieOf(response).ToLowerInvariant();
        cookie.Should().Contain("httponly").And.Contain("secure").And.Contain("samesite=strict").And.Contain("path=/").And.NotContain("domain=");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var jwt = new JsonWebToken(body.GetProperty("accessToken").GetString());
        jwt.Claims.Select(c => c.Type).Should().BeSubsetOf(["sub", "tid", "aud", "sstamp", "iat", "exp", "jti", "iss", "nbf"]);
        jwt.Subject.Should().Be(userId.ToString());
        jwt.GetClaim("tid").Value.Should().Be(tenancy.TenantA.Id.ToString());
        jwt.Audiences.Should().Equal(Host);
        (jwt.ValidTo - jwt.IssuedAt).Should().Be(TimeSpan.FromMinutes(15));
        (await AuditActionsAsync(userId)).Should().Contain("LoginSucceeded");

        var me = tenancy.ClientFor(Host);
        me.DefaultRequestHeaders.Authorization = new("Bearer", body.GetProperty("accessToken").GetString());
        (await me.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Email_inexistente_usuario_inativo_e_senha_errada_tem_a_mesma_resposta()
    {
        var inactive = NewEmail();
        var (inactiveId, _) = await tenancy.SeedUserAsync(tenancy.TenantA, inactive);
        await DeactivateAsync(inactiveId);
        var existing = NewEmail();
        await tenancy.SeedUserAsync(tenancy.TenantA, existing);

        var responses = new[]
        {
            await LoginAsync(NewEmail(), TenancyFixture.DefaultPassword),
            await LoginAsync(inactive, TenancyFixture.DefaultPassword),
            await LoginAsync(existing, "senha errada mas longa"),
        };

        responses.Select(r => r.StatusCode).Distinct().Should().ContainSingle();
        var bodies = await Task.WhenAll(responses.Select(async r => (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString()));
        bodies.Should().AllBe("E-mail ou senha inválidos.");
    }

    [Fact]
    public async Task Cinco_senhas_erradas_bloqueiam_a_conta_mesmo_com_a_senha_certa_depois()
    {
        var email = NewEmail();
        var (userId, _) = await tenancy.SeedUserAsync(tenancy.TenantA, email);

        for (var i = 0; i < 5; i++)
        {
            (await LoginAsync(email, "senha errada numero " + i)).IsSuccessStatusCode.Should().BeFalse();
        }

        var blocked = await LoginAsync(email, TenancyFixture.DefaultPassword);

        blocked.IsSuccessStatusCode.Should().BeFalse();
        (await blocked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString().Should().Be("E-mail ou senha inválidos.");
        (await AuditActionsAsync(userId)).Should().Contain("LockedOut").And.Contain("LoginBlocked");
    }
}
