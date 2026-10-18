using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Security;
using BemAqui.Host.Cli;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace BemAqui.Integration.Tests.Platform;

/// <summary>FND-13: super admin no host da plataforma, com 2FA obrigatório e sessão em cookie preso ao host.</summary>
public partial class PlatformAuthTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string PlatformHost = "plataforma.bemaquiapp.com.br";
    private const string Password = "frase longa do super admin";

    private sealed record Admin(string Email, byte[] Secret, string Cookie);

    private HttpClient Client(string host = PlatformHost, WebApplicationFactory<Program>? factory = null)
    {
        var client = (factory ?? tenancy.Factory).CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Host = host;
        client.DefaultRequestHeaders.Add("Origin", "https://" + host);
        client.DefaultRequestHeaders.Add("X-CSRF", "1");
        return client;
    }

    private static HttpClient WithCookie(HttpClient client, string cookie)
    {
        client.DefaultRequestHeaders.Add("Cookie", "__Host-bq_pa=" + cookie);
        return client;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string SessionCookieOf(HttpResponseMessage response)
    {
        var header = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-bq_pa=", StringComparison.Ordinal));
        header.ToLowerInvariant().Should().Contain("httponly").And.Contain("secure").And.Contain("samesite=strict").And.Contain("path=/").And.NotContain("domain=");
        return header.Split(';')[0]["__Host-bq_pa=".Length..];
    }

    private static string NewEmail() => "admin-" + Guid.NewGuid().ToString("N")[..12] + "@bemaquiapp.com.br";

    private Task<int> RunCliAsync(TextWriter output, params string[] args) =>
        CliCommand.RunAsync(args, c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Catalog"] = tenancy.CatalogConnection,
            ["DataProtection:CertificatePath"] = TestCertificates.Primary,
            ["DataProtection:CertificatePassword"] = TestCertificates.Password,
            ["DataProtection:AllowKeyRingCreation"] = "false",
        }), output);

    /// <summary>CLI → definir senha → login (configuração obrigatória) → confirmar 2FA: devolve a sessão.</summary>
    private async Task<Admin> AdminWithSessionAsync()
    {
        var email = NewEmail();
        var output = new StringWriter();
        (await RunCliAsync(output, "platform-admin", "create", "--email", email)).Should().Be(0, output.ToString());
        var link = output.ToString().Split('\n').Single(l => l.Contains("/plataforma/definir-senha?token=", StringComparison.Ordinal)).Trim();
        link.Should().StartWith("https://" + PlatformHost + "/plataforma/definir-senha?token=");
        var token = Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..]);

        (await Client().PostAsJsonAsync("/api/v1/platform/auth/set-password", new { token, password = Password })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client().PostAsJsonAsync("/api/v1/platform/auth/set-password", new { token, password = Password })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "o link é de uso único");

        var login = await Client().PostAsJsonAsync("/api/v1/platform/auth/login", new { email, password = Password });
        login.Headers.Contains("Set-Cookie").Should().BeFalse("sem 2FA não há sessão");
        var challenge = await JsonAsync(login);
        challenge.GetProperty("purpose").GetString().Should().Be("setup");
        var setup = await JsonAsync(await Client().PostAsJsonAsync("/api/v1/platform/auth/2fa/setup", new { challenge = challenge.GetProperty("challenge").GetString() }));
        var secret = Base32.Decode(setup.GetProperty("manualKey").GetString()!);
        var confirmed = await Client().PostAsJsonAsync("/api/v1/platform/auth/2fa/confirm", new { challenge = challenge.GetProperty("challenge").GetString(), code = Totp.Code(secret, Totp.StepAt(DateTimeOffset.UtcNow)) });
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(confirmed)).GetProperty("recoveryCodes").GetArrayLength().Should().Be(10);
        return new Admin(email, secret, SessionCookieOf(confirmed));
    }

    [Fact]
    public async Task Super_admin_criado_pela_CLI_entra_so_com_2FA()
    {
        var admin = await AdminWithSessionAsync();

        (await WithCookie(Client(), admin.Cookie).GetAsync("/api/v1/platform/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client().GetAsync("/api/v1/platform/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var again = await JsonAsync(await Client().PostAsJsonAsync("/api/v1/platform/auth/login", new { email = admin.Email, password = Password }));
        again.GetProperty("purpose").GetString().Should().Be("verify");
        var verified = await Client().PostAsJsonAsync("/api/v1/platform/auth/2fa/verify", new
        {
            challenge = again.GetProperty("challenge").GetString(),
            code = Totp.Code(admin.Secret, Totp.StepAt(DateTimeOffset.UtcNow) + 1),
        });

        verified.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await WithCookie(Client(), SessionCookieOf(verified)).GetAsync("/api/v1/platform/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Tokens_cruzados_entre_plataforma_e_tenant_sao_rejeitados()
    {
        var admin = await AdminWithSessionAsync();

        var tenantClient = tenancy.ClientFor("a.localhost");
        tenantClient.DefaultRequestHeaders.Add("Cookie", "__Host-bq_pa=" + admin.Cookie);
        (await tenantClient.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var tenantToken = await tenancy.TokenAsync(tenancy.TenantA, "a.localhost");
        var platform = Client();
        platform.DefaultRequestHeaders.Authorization = new("Bearer", tenantToken);
        (await platform.GetAsync("/api/v1/platform/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Rotas_da_plataforma_nao_existem_nos_hosts_de_tenant_e_vice_versa()
    {
        (await Client("a.localhost").GetAsync("/hangfire")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Client("a.localhost").PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "x@y.com", password = Password })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Client().GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Client().GetAsync("/api/v1/public/tenant-context")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
