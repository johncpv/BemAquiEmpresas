using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Webhooks;
using BemAqui.Integration.Tests.Observability;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Webhooks;

/// <summary>FND-16 CA 5, 6, 8, 9, 10 e 13: origem, limites, evento e outbox juntos, deduplicação, corrida e log sem segredo.</summary>
public partial class WebhookTests
{
    [Fact]
    public async Task Evento_valido_grava_o_corpo_cru_e_a_mensagem_do_outbox_e_responde_202()
    {
        var (key, secret) = await TestProviderAsync();
        var delivery = "d-" + Guid.NewGuid().ToString("N");
        var body = WebhookFixture.Body();

        var response = await fixture.Platform().SendAsync(WebhookFixture.SignedPost(Path("test", key), secret, body, delivery));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var evt = await fixture.EventByDeliveryAsync(fixture.Tenancy.TenantA, "test", delivery);
        evt.Should().NotBeNull();
        evt!.Status.Should().Be(WebhookEventStatus.Received);
        evt.EventType.Should().Be("nota.autorizada");
        Encoding.UTF8.GetString(evt.Payload!).Should().Be(body, "o corpo é guardado como chegou");
        evt.BodySha256.Should().Equal(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(body)));
        var messages = await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.OutboxMessages.AsNoTracking().Where(m => m.Type == IntegrationEventRegistry.NameOf(typeof(WebhookReceived))).ToListAsync());
        messages.Should().Contain(m => m.Payload.Contains(evt.Id.ToString(), StringComparison.Ordinal), "evento e mensagem na mesma transação (ADR 0031 §3)");
        messages.Should().NotContain(m => m.Payload.Contains("Maria", StringComparison.Ordinal), "a mensagem leva só ids");
    }

    [Fact]
    public async Task Corpo_alterado_em_um_byte_e_recusado()
    {
        var (key, secret) = await TestProviderAsync();
        var request = WebhookFixture.SignedPost(Path("test", key), secret, "{\"valor\":10}");
        request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes("{\"valor\":11}"));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        (await fixture.Platform().SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Replay_com_timestamp_antigo_e_recusado()
    {
        var (key, secret) = await TestProviderAsync();
        var old = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

        (await fixture.Platform().SendAsync(WebhookFixture.SignedPost(Path("test", key), secret, WebhookFixture.Body(), timestamp: old))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Entrega_repetida_pelo_id_ou_pelo_mesmo_corpo_responde_200_sem_mensagem_nova()
    {
        var (key, secret) = await TestProviderAsync();
        var delivery = "d-" + Guid.NewGuid().ToString("N");
        var body = WebhookFixture.Body();
        var client = fixture.Platform();

        (await client.SendAsync(WebhookFixture.SignedPost(Path("test", key), secret, body, delivery))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var outbox = await OutboxCountAsync();
        (await client.SendAsync(WebhookFixture.SignedPost(Path("test", key), secret, body, delivery))).StatusCode.Should().Be(HttpStatusCode.OK, "mesmo id de entrega");
        (await client.SendAsync(WebhookFixture.SignedPost(Path("test", key), secret, body, "d-outro-" + Guid.NewGuid().ToString("N")))).StatusCode
            .Should().Be(HttpStatusCode.OK, "mesmo corpo com outro id (o Inter reenvia sem id estável): pelo SHA-256");

        (await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.WebhookEvents.CountAsync(e => e.Provider == "test" && e.ExternalId == delivery))).Should().Be(1);
        (await OutboxCountAsync()).Should().Be(outbox, "nenhuma mensagem nova vai ao outbox");
    }

    [Fact]
    public async Task Corrida_da_mesma_entrega_grava_uma_linha_e_uma_mensagem()
    {
        var (key, secret) = await TestProviderAsync();
        var delivery = "d-" + Guid.NewGuid().ToString("N");
        var body = WebhookFixture.Body();
        var outbox = await OutboxCountAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => fixture.Platform().SendAsync(WebhookFixture.SignedPost(Path("test", key), secret, body, delivery))));

        responses.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Accepted || s == HttpStatusCode.OK, "corrida nunca dá 500");
        responses.Count(r => r.StatusCode == HttpStatusCode.Accepted).Should().Be(1);
        (await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.WebhookEvents.CountAsync(e => e.Provider == "test" && e.ExternalId == delivery))).Should().Be(1);
        (await OutboxCountAsync()).Should().Be(outbox + 1);
    }

    [Fact]
    public async Task Token_na_query_e_no_caminho_conforme_o_modo_do_provedor()
    {
        var key = await fixture.KeyAsync(fixture.Tenancy.TenantA);
        var queryToken = await fixture.ConfigureAsync(fixture.Tenancy.TenantA, WebhookFixture.Token);
        var pathToken = await fixture.ConfigureAsync(fixture.Tenancy.TenantA, WebhookFixture.PathToken);
        var client = fixture.Platform();

        (await client.SendAsync(WebhookFixture.Json(Path("test-token", key, "?t=" + queryToken), WebhookFixture.Body()))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.SendAsync(WebhookFixture.Json(Path("test-token", key, "/" + queryToken), WebhookFixture.Body()))).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "modo query não aceita token no caminho");
        (await client.SendAsync(WebhookFixture.Json(Path("test-token", key, "?t=" + queryToken + "&t=" + queryToken), WebhookFixture.Body()))).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "token repetido");
        (await client.SendAsync(WebhookFixture.Json(Path("test-path", key, "/" + pathToken), WebhookFixture.Body()))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.SendAsync(WebhookFixture.Json(Path("test-path", key, "?t=" + pathToken), WebhookFixture.Body()))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var stored = await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.WebhookCredentials.AsNoTracking().SingleAsync(c => c.Provider == "test-token" && c.ScopeId == Guid.Empty));
        stored.TokenHash.Should().NotBe(queryToken).And.HaveLength(64, "só o SHA-256 fica guardado");
    }

    [Fact]
    public async Task Corpo_grande_tipo_errado_e_excesso_de_requisicoes_sao_recusados_antes_do_banco()
    {
        var (key, secret) = await TestProviderAsync();
        var client = fixture.Platform();
        var before = await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.WebhookEvents.CountAsync());

        var big = "{\"x\":\"" + new string('a', WebhookOptions.MaxBodyBytes) + "\"}";
        (await client.SendAsync(WebhookFixture.SignedPost(Path("test", key), secret, big))).StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);

        var chunked = WebhookFixture.SignedPost(Path("test", key), secret, "{}");
        chunked.Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(big)));
        chunked.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        (await client.SendAsync(chunked)).StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge, "sem Content-Length, para ao passar do limite");

        var xml = WebhookFixture.SignedPost(Path("test", key), secret, "{}");
        xml.Content!.Headers.ContentType = new MediaTypeHeaderValue("application/xml");
        (await client.SendAsync(xml)).StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);

        (await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.WebhookEvents.CountAsync())).Should().Be(before);

        await using var limited = WebhookFixture.Build(fixture.Tenancy.Factory, perMinute: 3, new MemorySink(), new CapturingAlertSink(), new CapturingJobClient());
        var limitedClient = limited.CreateClient();
        limitedClient.DefaultRequestHeaders.Host = WebhookFixture.PlatformHost;
        var statuses = new List<HttpResponseMessage>();
        for (var i = 0; i < 5; i++)
        {
            statuses.Add(await limitedClient.SendAsync(WebhookFixture.SignedPost(Path("test", key), secret, WebhookFixture.Body())));
        }

        statuses.Take(3).Should().OnlyContain(r => r.StatusCode != HttpStatusCode.TooManyRequests);
        statuses[^1].StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        statuses[^1].Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task Falha_no_meio_da_transacao_responde_503_e_nada_fica_gravado()
    {
        var (key, secret) = await TestProviderAsync();
        await using var broken = WebhookFixture.Build(fixture.Tenancy.Factory, 100_000, new MemorySink(), new CapturingAlertSink(), new CapturingJobClient(),
            s => s.AddScoped<IEventPublisher, BrokenPublisher>());
        var client = broken.CreateClient();
        client.DefaultRequestHeaders.Host = WebhookFixture.PlatformHost;
        var delivery = "d-" + Guid.NewGuid().ToString("N");

        (await client.SendAsync(WebhookFixture.SignedPost(Path("test", key), secret, WebhookFixture.Body(), delivery))).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        (await fixture.EventByDeliveryAsync(fixture.Tenancy.TenantA, "test", delivery)).Should().BeNull("evento e mensagem caem juntos; o provedor reentrega");
    }

    /// <summary>Mensagem que o PostgreSQL recusa (jsonb inválido): derruba o SaveChanges inteiro, no meio da gravação.</summary>
    private sealed class BrokenPublisher(BemAquiDbContext db) : IEventPublisher
    {
        public void Publish<TEvent>(TEvent integrationEvent, Guid? correlationId = null)
            where TEvent : IIntegrationEvent =>
            db.OutboxMessages.Add(new OutboxMessage { Type = "quebrado", Payload = "isto não é json", OccurredAt = DateTimeOffset.UtcNow, NextAttemptAt = DateTimeOffset.UtcNow });
    }

    [Fact]
    public async Task Resposta_sai_em_ate_2_segundos_no_p95_sem_esperar_o_processamento()
    {
        var (key, secret) = await TestProviderAsync();
        TestWebhookHandler.DelayMs = 5_000; // processar é lento; a resposta não espera
        try
        {
            var client = fixture.Platform();
            var times = new List<double>();
            for (var i = 0; i < 20; i++)
            {
                var watch = Stopwatch.StartNew();
                (await client.SendAsync(WebhookFixture.SignedPost(Path("test", key), secret, WebhookFixture.Body()))).StatusCode.Should().Be(HttpStatusCode.Accepted);
                times.Add(watch.Elapsed.TotalMilliseconds);
            }

            times.Order().ElementAt(18).Should().BeLessThan(2_000);
        }
        finally
        {
            TestWebhookHandler.DelayMs = 0;
        }
    }

    [Fact]
    public async Task Log_e_auditoria_nao_tem_corpo_token_assinatura_nem_segredo()
    {
        var key = await fixture.KeyAsync(fixture.Tenancy.TenantA);
        var secret = await fixture.ConfigureAsync(fixture.Tenancy.TenantA, WebhookFixture.Hmac);
        var token = await fixture.ConfigureAsync(fixture.Tenancy.TenantA, WebhookFixture.Token);
        var canary = "CANARIO-" + Guid.NewGuid().ToString("N");
        var client = fixture.Platform();
        var signed = WebhookFixture.SignedPost(Path("test", key), secret, WebhookFixture.Body(canary));
        var signature = signed.Headers.GetValues(WebhookFixture.SignatureHeader).Single();

        (await client.SendAsync(signed)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.SendAsync(WebhookFixture.Json(Path("test-token", key, "?t=" + token), WebhookFixture.Body(canary)))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await client.SendAsync(WebhookFixture.SignedPost(Path("test", key), OpaqueToken.New(), WebhookFixture.Body(canary)))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        string logs;
        lock (fixture.Logs.Events)
        {
            logs = string.Join("\n", fixture.Logs.Events.Select(e => e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + " " + string.Join(" ", e.Properties.Select(p => p.Value.ToString()))));
        }

        logs.Should().Contain("Webhook test", "o log traz provedor, tenant, id, tipo, resultado e duração");
        foreach (var leaked in new[] { canary, secret, token, signature[7..], key })
        {
            logs.Should().NotContain(leaked);
        }

        var audit = await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.AuditEntries.AsNoTracking().Select(a => a.Changes).ToListAsync());
        string.Join("\n", audit).Should().NotContain(canary).And.NotContain(secret).And.NotContain(token);
    }

    private Task<int> OutboxCountAsync() =>
        fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.OutboxMessages.CountAsync(m => m.Type == IntegrationEventRegistry.NameOf(typeof(WebhookReceived))));
}
