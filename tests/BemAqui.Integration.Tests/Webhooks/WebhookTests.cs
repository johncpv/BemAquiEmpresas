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
        // Os testes da classe rodam em sequência e dividem os dois bancos: "nada gravado" é o total de cada tenant igual antes e
        // depois, e nenhuma das entregas recusadas aparece em A nem em B (gravação no tenant errado também reprova).
        var deliveries = Enumerable.Range(0, 4).Select(i => "d-recusa-" + i + "-" + Guid.NewGuid().ToString("N")).ToList();
        var eventsA = await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.WebhookEvents.CountAsync());
        var eventsB = await fixture.QueryAsync(fixture.Tenancy.TenantB, db => db.WebhookEvents.CountAsync());
        var outboxA = await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.OutboxMessages.CountAsync());
        var outboxB = await fixture.QueryAsync(fixture.Tenancy.TenantB, db => db.OutboxMessages.CountAsync());

        var unknownKey = await client.SendAsync(WebhookFixture.SignedPost(Path("test", "AAAAAAAAAAAAAAAAAAAAAA"), secret, WebhookFixture.Body(), deliveries[0]));
        var unknownProvider = await client.SendAsync(WebhookFixture.SignedPost(Path("nao-existe", key), secret, WebhookFixture.Body(), deliveries[1]));
        var badSignature = await client.SendAsync(WebhookFixture.SignedPost(Path("test", key), OpaqueToken.New(), WebhookFixture.Body(), deliveries[2]));
        await fixture.Tenancy.SetStatusAsync(fixture.Tenancy.TenantB.Id, suspended: true);
        fixture.Factory.Services.GetRequiredService<BemAqui.BuildingBlocks.Tenancy.ITenantResolver>().Invalidate();
        HttpResponseMessage suspended;
        try
        {
            suspended = await client.SendAsync(WebhookFixture.SignedPost(Path("test", key), secret, WebhookFixture.Body(), deliveries[3]));
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

        foreach (var tenant in new[] { fixture.Tenancy.TenantA, fixture.Tenancy.TenantB })
        {
            (await fixture.QueryAsync(tenant, db => db.WebhookEvents.CountAsync(e => deliveries.Contains(e.ExternalId))))
                .Should().Be(0, "nada é gravado numa recusa, em nenhum tenant");
        }

        (await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.WebhookEvents.CountAsync())).Should().Be(eventsA, "nada é gravado numa recusa");
        (await fixture.QueryAsync(fixture.Tenancy.TenantB, db => db.WebhookEvents.CountAsync())).Should().Be(eventsB, "nada é gravado numa recusa");
        (await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.OutboxMessages.CountAsync())).Should().Be(outboxA, "nenhuma mensagem vai ao outbox");
        (await fixture.QueryAsync(fixture.Tenancy.TenantB, db => db.OutboxMessages.CountAsync())).Should().Be(outboxB, "nenhuma mensagem vai ao outbox");
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
    public async Task Host_interno_da_API_nao_recebe_webhook_de_nenhum_provedor()
    {
        // Sem rede privada com o EmissorFiscal (ADR 0027, adendo de 27/10/2026): todo aviso chega pelo host da plataforma, pelo stack-nginx.
        var (key, secret) = await TestProviderAsync();
        var client = fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = "bqe-homolog-api";

        (await client.SendAsync(WebhookFixture.SignedPost(Path("test", key), secret, WebhookFixture.Body()))).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
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

        // O build publicado só registra os provedores reais: o inter (BNK-02) e o emissor-fiscal (FIS-01); os de teste só existem aqui.
        fixture.Tenancy.Factory.Services.GetRequiredService<WebhookProviderRegistry>().All.Select(p => p.Code).Should().BeEquivalentTo("inter", "emissor-fiscal");
    }
}
