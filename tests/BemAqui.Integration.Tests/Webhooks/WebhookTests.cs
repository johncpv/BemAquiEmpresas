using System.Net;
using System.Text;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Webhooks;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Webhooks;

/// <summary>
/// FND-16 CA 1 a 3 (ADR 0031 §1): rota no host da plataforma, fora do tenant por host e da allowlist do painel, tenant só pelo
/// <c>tenantKey</c> opaco, resposta uniforme de recusa, GET de verificação só para quem declara e 405 para o resto.
/// </summary>
public partial class WebhookTests(WebhookFixture fixture) : IClassFixture<WebhookFixture>
{
    private static string Path(string provider, string key, string? suffix = null) => "/webhooks/" + provider + "/" + key + (suffix ?? string.Empty);

    private async Task<(string Key, string Secret)> TestProviderAsync(BemAqui.Platform.Domain.Tenant? tenant = null)
    {
        tenant ??= fixture.Tenancy.TenantA;
        return (await fixture.KeyAsync(tenant), await fixture.ConfigureAsync(tenant, WebhookFixture.Hmac));
    }

    [Fact]
    public async Task Rota_atende_no_host_da_plataforma_fora_da_allowlist_e_nao_existe_no_host_do_tenant()
    {
        var (key, secret) = await TestProviderAsync();

        var platform = await fixture.Platform().SendAsync(WebhookFixture.SignedPost(Path("test", key), secret, WebhookFixture.Body()));
        platform.StatusCode.Should().Be(HttpStatusCode.Accepted, "a allowlist de IP do painel (ligada no teste) não vale para /webhooks");

        (await fixture.Platform().GetAsync("/api/v1/platform/tenants")).StatusCode.Should().Be(HttpStatusCode.NotFound, "o painel continua atrás da allowlist");

        var tenantHost = fixture.Factory.CreateClient();
        tenantHost.DefaultRequestHeaders.Host = "a.localhost";
        (await tenantHost.SendAsync(WebhookFixture.SignedPost(Path("test", key), secret, WebhookFixture.Body()))).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "no host do tenant a rota não existe: o tenant nunca vem do host");
    }

    [Fact]
    public async Task TenantKey_e_opaco_unico_e_estavel()
    {
        var a = await fixture.KeyAsync(fixture.Tenancy.TenantA);
        var b = await fixture.KeyAsync(fixture.Tenancy.TenantB);

        a.Should().HaveLength(CatalogWebhookTenantKeys.KeyLength).And.NotBe(b);
        a.Should().NotContain(fixture.Tenancy.TenantA.Key).And.NotContain(fixture.Tenancy.TenantA.Id.ToString("N")[..8]);
        (await fixture.KeyAsync(fixture.Tenancy.TenantA)).Should().Be(a, "a URL cadastrada no provedor não muda");
        Convert.FromBase64String(a.Replace('-', '+').Replace('_', '/') + "==").Should().HaveCount(16, "128 bits aleatórios");
    }

    [Fact]
    public async Task Chave_inexistente_tenant_suspenso_provedor_desconhecido_e_assinatura_errada_tem_a_mesma_resposta()
    {
        var (key, secret) = await TestProviderAsync(fixture.Tenancy.TenantB);
        var before = fixture.Alerts.Raised.Count;
        var client = fixture.Platform();

        var unknownKey = await client.SendAsync(WebhookFixture.SignedPost(Path("test", "AAAAAAAAAAAAAAAAAAAAAA"), secret, WebhookFixture.Body()));
        var unknownProvider = await client.SendAsync(WebhookFixture.SignedPost(Path("nao-existe", key), secret, WebhookFixture.Body()));
        var badSignature = await client.SendAsync(WebhookFixture.SignedPost(Path("test", key), OpaqueToken.New(), WebhookFixture.Body()));
        await fixture.Tenancy.SetStatusAsync(fixture.Tenancy.TenantB.Id, suspended: true);
        fixture.Factory.Services.GetRequiredService<BemAqui.BuildingBlocks.Tenancy.ITenantResolver>().Invalidate();
        HttpResponseMessage suspended;
        try
        {
            suspended = await client.SendAsync(WebhookFixture.SignedPost(Path("test", key), secret, WebhookFixture.Body()));
        }
        finally
        {
            await fixture.Tenancy.SetStatusAsync(fixture.Tenancy.TenantB.Id, suspended: false);
            fixture.Factory.Services.GetRequiredService<BemAqui.BuildingBlocks.Tenancy.ITenantResolver>().Invalidate();
        }

        foreach (var response in new[] { unknownKey, unknownProvider, badSignature, suspended })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await response.Content.ReadAsByteArrayAsync()).Should().BeEmpty("a recusa não diz qual parte falhou");
            response.Headers.CacheControl?.NoStore.Should().BeTrue();
        }

        (await fixture.QueryAsync(fixture.Tenancy.TenantB, db => db.WebhookEvents.CountAsync(e => e.Payload != null && e.Status == WebhookEventStatus.Received && e.ReceivedAt > DateTimeOffset.UtcNow.AddMinutes(-1))))
            .Should().Be(0, "nada é gravado numa recusa");
        fixture.Alerts.Raised.Count.Should().Be(before, "4 recusas não chegam ao limite de 20");
    }

    [Fact]
    public async Task Vinte_recusas_em_5_minutos_geram_o_alerta_uma_vez()
    {
        var (key, _) = await TestProviderAsync();
        var client = fixture.Platform();

        for (var i = 0; i < WebhookRejectionMonitor.Threshold + 3; i++)
        {
            (await client.SendAsync(WebhookFixture.SignedPost(Path("test", key), OpaqueToken.New(), WebhookFixture.Body()))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        fixture.Alerts.Raised.Where(a => a.Kind == WebhookAlerts.Rejections && a.Tenant == fixture.Tenancy.TenantA.Key).Should().ContainSingle();
    }

    [Fact]
    public async Task Outros_metodos_respondem_405()
    {
        var (key, _) = await TestProviderAsync();
        var client = fixture.Platform();

        (await client.PutAsync(Path("test", key), new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        (await client.DeleteAsync(Path("test", key))).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task GET_de_verificacao_devolve_o_challenge_so_com_o_token_certo_e_sem_gravar_evento()
    {
        var key = await fixture.KeyAsync(fixture.Tenancy.TenantA);
        var token = await fixture.ConfigureAsync(fixture.Tenancy.TenantA, WebhookFixture.Verify);
        var client = fixture.Platform();

        var ok = await client.GetAsync(Path("test-verify", key) + "?hub.mode=subscribe&hub.challenge=1158201444&hub.verify_token=" + token);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        ok.Content.Headers.ContentType!.MediaType.Should().Be("text/plain");
        (await ok.Content.ReadAsStringAsync()).Should().Be("1158201444");

        var wrong = await client.GetAsync(Path("test-verify", key) + "?hub.mode=subscribe&hub.challenge=1&hub.verify_token=" + OpaqueToken.New());
        wrong.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await wrong.Content.ReadAsByteArrayAsync()).Should().BeEmpty();

        var undeclared = await client.GetAsync(Path("test", key) + "?hub.mode=subscribe&hub.challenge=1&hub.verify_token=" + token);
        undeclared.StatusCode.Should().Be(HttpStatusCode.Forbidden, "provedor sem SupportsVerificationGet");

        (await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.WebhookEvents.CountAsync(e => e.Provider == "test-verify"))).Should().Be(0);
    }

    [Fact]
    public async Task Provedor_sem_host_interno_declarado_e_recusado_no_host_interno()
    {
        var (key, secret) = await TestProviderAsync();
        await using var factory = fixture.Factory.WithWebHostBuilder(b => b.UseSetting("Webhooks:InternalHosts:0", "bqe-test-api"));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = "bqe-test-api";

        (await client.SendAsync(WebhookFixture.SignedPost(Path("test", key), secret, WebhookFixture.Body()))).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "só o EmissorFiscal (AllowInternalHost, FIS-01) entra pela rede interna");
    }

    [Fact]
    public async Task Registro_do_Host_so_tem_provedor_com_validador_e_handler_no_DI()
    {
        var registry = fixture.Factory.Services.GetRequiredService<WebhookProviderRegistry>();

        registry.All.Should().NotBeEmpty();
        foreach (var provider in registry.All)
        {
            WebhookProviderRegistry.Problems(provider).Should().BeEmpty(provider.Code);
            await using var scope = await fixture.ScopeAsync(fixture.Tenancy.TenantA);
            scope.ServiceProvider.GetService(provider.HandlerType).Should().NotBeNull(provider.Code);
        }

        // O build publicado não registra nenhum provedor nesta story (o de teste só existe aqui).
        fixture.Tenancy.Factory.Services.GetRequiredService<WebhookProviderRegistry>().All.Should().BeEmpty();
    }
}
