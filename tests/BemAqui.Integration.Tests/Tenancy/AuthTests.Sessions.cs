using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Tenancy;

public partial class AuthTests
{
    private static HttpRequestMessage Refresh(string cookie, bool csrf = true, string? origin = "https://a.localhost")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Cookie", "__Host-bq_rt=" + cookie);
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

    private async Task<string> LoggedInCookieAsync(WebApplicationFactory<Program>? factory = null)
    {
        var email = NewEmail();
        await tenancy.SeedUserAsync(tenancy.TenantA, email);
        var client = (factory ?? tenancy.Factory).CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        return CookieValue(RefreshCookieOf(await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = TenancyFixture.DefaultPassword })));
    }

    private async Task DeactivateAsync(Guid userId)
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        (await scope.ServiceProvider.GetRequiredService<UserLifecycle>().DeactivateAsync(userId, null)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_troca_o_par_e_exige_origem_e_cabecalho_anti_CSRF()
    {
        var cookie = await LoggedInCookieAsync();
        var client = tenancy.ClientFor(Host);

        (await client.SendAsync(Refresh(cookie, csrf: false))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.SendAsync(Refresh(cookie, origin: null))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.SendAsync(Refresh(cookie, origin: "https://golpe.example.com"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var renewed = await client.SendAsync(Refresh(cookie));

        renewed.StatusCode.Should().Be(HttpStatusCode.OK);
        CookieValue(RefreshCookieOf(renewed)).Should().NotBe(cookie);
        (await renewed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Duas_abas_renovando_juntas_dentro_da_tolerancia_recebem_sessao_valida()
    {
        var cookie = await LoggedInCookieAsync();
        var client = tenancy.ClientFor(Host);

        var both = await Task.WhenAll(client.SendAsync(Refresh(cookie)), client.SendAsync(Refresh(cookie)));

        both.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_reapresentado_fora_da_tolerancia_revoga_a_familia()
    {
        await using var strict = tenancy.Factory.WithWebHostBuilder(b => b.UseSetting("Auth:RefreshReuseTolerance", "00:00:00"));
        var cookie = await LoggedInCookieAsync(strict);
        var client = strict.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        var renewed = CookieValue(RefreshCookieOf(await client.SendAsync(Refresh(cookie))));
        await Task.Delay(50);

        (await client.SendAsync(Refresh(cookie))).IsSuccessStatusCode.Should().BeFalse("o refresh antigo foi reapresentado");
        (await client.SendAsync(Refresh(renewed))).IsSuccessStatusCode.Should().BeFalse("a família inteira foi revogada");
    }

    [Fact]
    public async Task Logout_revoga_o_refresh_e_apaga_o_cookie()
    {
        var cookie = await LoggedInCookieAsync();
        var client = tenancy.ClientFor(Host);
        var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logout.Headers.Add("Cookie", "__Host-bq_rt=" + cookie);
        logout.Headers.Add("X-CSRF", "1");
        logout.Headers.Add("Origin", "https://a.localhost");

        var response = await client.SendAsync(logout);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        RefreshCookieOf(response).Should().Contain("expires=Thu, 01 Jan 1970");
        (await client.SendAsync(Refresh(cookie))).IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task Usuario_desativado_recebe_401_no_proximo_request()
    {
        var (userId, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, NewEmail());
        var client = tenancy.ClientFor(Host);
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: userId, stamp: stamp));
        (await client.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        await DeactivateAsync(userId);

        (await client.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
