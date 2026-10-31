using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BemAqui.Host.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>FND-02 CA 6, 9, 10, 11 e 12: token preso ao tenant e ao host (REST e hub), proxy confiável, cabeçalhos e OpenAPI.</summary>
public class HttpHardeningTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private HttpClient Authorized(string host, string token)
    {
        var client = tenancy.ClientFor(host);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Token_do_tenant_A_vale_no_host_A()
    {
        var response = await Authorized("a.localhost", await tenancy.TokenAsync(tenancy.TenantA, "a.localhost")).GetAsync("/api/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("tenantId").GetString().Should().Be(tenancy.TenantA.Id.ToString());
    }

    public static TheoryData<string> RejectedCases => ["tenant-a-no-host-b", "aud-de-b-no-host-a", "sem-tid", "expirado", "outra-chave", "outro-emissor", "sem-token"];

    [Theory]
    [MemberData(nameof(RejectedCases))]
    public async Task Token_fora_do_tenant_ou_do_host_e_401(string scenario)
    {
        var (host, token) = scenario switch
        {
            "tenant-a-no-host-b" => ("b.localhost", TestTokens.Create(tenancy.TenantA.Id, "b.localhost")),
            "aud-de-b-no-host-a" => ("a.localhost", TestTokens.Create(tenancy.TenantA.Id, "b.localhost")),
            "sem-tid" => ("a.localhost", TestTokens.Create(null, "a.localhost")),
            "expirado" => ("a.localhost", TestTokens.Create(tenancy.TenantA.Id, "a.localhost", expires: DateTime.UtcNow.AddMinutes(-5))),
            "outra-chave" => ("a.localhost", TestTokens.Create(tenancy.TenantA.Id, "a.localhost", key: new byte[32])),
            "outro-emissor" => ("a.localhost", TestTokens.Create(tenancy.TenantA.Id, "a.localhost", issuer: "outro")),
            _ => ("a.localhost", string.Empty),
        };
        var client = token.Length == 0 ? tenancy.ClientFor(host) : Authorized(host, token);

        (await client.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private HubConnection Hub(string host, string token) =>
        new HubConnectionBuilder()
            .WithUrl($"http://{host}{PingHub.Path}", o =>
            {
                o.HttpMessageHandlerFactory = _ => tenancy.Factory.Server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult<string?>(token);
                o.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            })
            .Build();

    [Fact]
    public async Task Hub_aceita_token_do_proprio_tenant_e_host()
    {
        await using var hub = Hub("a.localhost", await tenancy.TokenAsync(tenancy.TenantA, "a.localhost"));

        await hub.StartAsync();

        (await hub.InvokeAsync<string>("Ping")).Should().Be(tenancy.TenantA.Id.ToString());
    }

    [Fact]
    public async Task Hub_recusa_token_de_outro_tenant()
    {
        await using var hub = Hub("b.localhost", TestTokens.Create(tenancy.TenantA.Id, "b.localhost"));

        var conectar = () => hub.StartAsync();

        (await conectar.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Origem_de_outro_site_nao_recebe_CORS()
    {
        var client = tenancy.ClientFor("a.localhost");
        client.DefaultRequestHeaders.Add("Origin", "https://golpe.example.com");

        var response = await client.GetAsync("/api/v1/public/tenant-context");

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Theory]
    [InlineData("a.localhost", "/api/v1/public/tenant-context")]
    [InlineData("desconhecido.localhost", "/api/v1/me")]
    public async Task Toda_resposta_tem_cabecalhos_de_seguranca(string host, string path)
    {
        var response = await tenancy.ClientFor(host).GetAsync(path);

        response.Headers.GetValues("Strict-Transport-Security").Single().Should().Contain("max-age=");
        response.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("frame-ancestors 'none'");
        response.Headers.GetValues("X-Content-Type-Options").Single().Should().Be("nosniff");
        response.Headers.GetValues("Referrer-Policy").Should().ContainSingle();
    }

    [Fact]
    public async Task Producao_nao_expoe_OpenAPI_e_desenvolvimento_expoe()
    {
        await using var production = tenancy.Factory.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        (await production.CreateClient().GetAsync("/openapi/v1.json")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var development = tenancy.Factory.WithWebHostBuilder(b => b.UseEnvironment("Development"));
        (await development.CreateClient().GetAsync("/openapi/v1.json")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Com_proxy_obrigatorio_acesso_direto_nao_resolve_tenant()
    {
        await using var strict = tenancy.Factory.WithWebHostBuilder(b => b.UseSetting("ReverseProxy:RequireTrustedProxy", "true"));
        var client = strict.CreateClient();
        client.DefaultRequestHeaders.Host = "a.localhost";
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.50");

        (await client.GetAsync("/api/v1/public/tenant-context")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK, "o healthcheck do Docker continua respondendo");

        // Sem exceção para webhook (ADR 0027, adendo de 27/10/2026): o EmissorFiscal também chega só pelo stack-nginx.
        foreach (var host in new[] { "plataforma.bemaquiapp.com.br", "bqe-homolog-api" })
        {
            var direct = strict.CreateClient();
            direct.DefaultRequestHeaders.Host = host;
            using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            (await direct.PostAsync("/webhooks/emissor-fiscal/AbCdEfGhIjKlMnOpQrStUv", content)).StatusCode.Should().Be(HttpStatusCode.NotFound, host);
        }
    }

    [Fact]
    public void Cabecalho_encaminhado_so_vale_da_rede_do_proxy()
    {
        var trusted = new TrustedProxies(new ReverseProxyOptions { KnownNetworks = ["172.18.0.0/16"] }, NullLogger<TrustedProxies>.Instance);
        var options = new ForwardedHeadersOptions();

        ReverseProxySetup.Configure(options);

        options.ForwardedHeaders.Should().NotHaveFlag(ForwardedHeaders.XForwardedHost, "o Host nunca vem de cabeçalho");
        options.ForwardLimit.Should().Be(1);
        trusted.IsTrusted(IPAddress.Parse("172.18.0.5")).Should().BeTrue();
        trusted.IsTrusted(IPAddress.Parse("10.0.0.9")).Should().BeFalse();
        trusted.IsTrusted(null).Should().BeFalse();
    }

    [Fact]
    public void Nome_do_proxy_resolve_na_subida_e_de_novo_quando_o_IP_muda()
    {
        var clock = new ManualClock();
        var ip = IPAddress.Parse("172.18.0.7");
        var calls = 0;
        var trusted = new TrustedProxies(
            new ReverseProxyOptions { KnownProxyHosts = ["stack-nginx"] }, NullLogger<TrustedProxies>.Instance, clock,
            host => { calls++; host.Should().Be("stack-nginx"); return [ip]; });

        calls.Should().Be(1, "resolve na subida");
        trusted.ResolvedAddresses.Should().BeEquivalentTo([IPAddress.Parse("172.18.0.7")]);
        trusted.IsTrusted(IPAddress.Parse("172.18.0.7")).Should().BeTrue();
        trusted.IsTrusted(IPAddress.Parse("172.18.0.9")).Should().BeFalse("outro container da stack_stack não é o proxy");
        calls.Should().Be(1, "não resolve de novo antes do intervalo");

        ip = IPAddress.Parse("172.18.0.9"); // stack-nginx recriado
        clock.Advance(TrustedProxies.RefreshInterval);
        trusted.IsTrusted(IPAddress.Parse("172.18.0.9")).Should().BeTrue();
        trusted.IsTrusted(IPAddress.Parse("172.18.0.7")).Should().BeFalse("o IP antigo deixa de valer");
        calls.Should().Be(2);
    }

    [Fact]
    public void Nome_que_nao_resolve_nao_derruba_a_subida()
    {
        var trusted = new TrustedProxies(
            new ReverseProxyOptions { KnownProxyHosts = ["stack-nginx"], KnownProxies = ["10.1.1.1"] }, NullLogger<TrustedProxies>.Instance, null,
            _ => throw new System.Net.Sockets.SocketException(11001));

        trusted.ResolvedAddresses.Should().BeEmpty();
        trusted.IsTrusted(IPAddress.Parse("10.1.1.1")).Should().BeTrue("IP fixo continua valendo");
        trusted.IsTrusted(IPAddress.Parse("172.18.0.7")).Should().BeFalse();
    }

    [Theory]
    [InlineData("172.18.0.7", "203.0.113.50", "https")]
    [InlineData("172.18.0.9", "172.18.0.9", "http")]
    public async Task X_Forwarded_For_so_e_aplicado_quando_vem_do_proxy(string socketIp, string expectedIp, string expectedScheme)
    {
        var trusted = new TrustedProxies(
            new ReverseProxyOptions { KnownProxyHosts = ["stack-nginx"] }, NullLogger<TrustedProxies>.Instance, null, _ => [IPAddress.Parse("172.18.0.7")]);
        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(s => s.AddSingleton(trusted))
                .Configure(app =>
                {
                    app.Use((context, next) =>
                    {
                        context.Connection.RemoteIpAddress = IPAddress.Parse(socketIp);
                        return next(context);
                    });
                    app.UseTrustedForwardedHeaders();
                    app.Run(context => context.Response.WriteAsync($"{context.Connection.RemoteIpAddress}|{context.Request.Scheme}"));
                }))
            .StartAsync();
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.50");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        var body = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        body.Should().Be($"{expectedIp}|{expectedScheme}");
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }
}
