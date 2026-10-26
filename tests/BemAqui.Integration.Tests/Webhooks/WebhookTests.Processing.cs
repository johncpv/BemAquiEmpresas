using System.Net;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Webhooks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Webhooks;

/// <summary>FND-16 CA 11, 12 e 14: fila com o tenantId, módulo conferido, execução dupla, dois tenants, dead letter, varredor e expurgo.</summary>
public partial class WebhookTests
{
    private async Task<Guid> ReceiveAsync(BemAqui.Platform.Domain.Tenant tenant, string eventType = "nota.autorizada", WebhookProvider? provider = null)
    {
        provider ??= WebhookFixture.Hmac;
        var key = await fixture.KeyAsync(tenant);
        var secret = await fixture.ConfigureAsync(tenant, provider);
        var delivery = "d-" + Guid.NewGuid().ToString("N");
        (await fixture.Platform().SendAsync(WebhookFixture.SignedPost(Path(provider.Code, key), secret, WebhookFixture.Body(), delivery, eventType))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        return (await fixture.EventByDeliveryAsync(tenant, provider.Code, delivery))!.Id;
    }

    [Fact]
    public async Task Outbox_enfileira_o_job_na_fila_do_provedor_so_com_tenantId_e_id_do_evento()
    {
        var id = await ReceiveAsync(fixture.Tenancy.TenantA);
        var dispatcher = fixture.Factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<OutboxDispatcher>().Single();

        await dispatcher.DispatchOnceAsync(CancellationToken.None);

        fixture.Jobs.Enqueued.Should().Contain((fixture.Tenancy.TenantA.Id, id, WebhookFixture.Hmac.Queue));
        var run = await fixture.RunJobAsync(fixture.Tenancy.TenantA, id);
        run.Should().BeNull();
        var evt = await fixture.EventAsync(fixture.Tenancy.TenantA, id);
        evt.Status.Should().Be(WebhookEventStatus.Processed);
        evt.Attempts.Should().Be(1);
        evt.ProcessedAt.Should().NotBeNull();
        TestWebhookHandler.Calls[id].Should().Be(1);
    }

    [Fact]
    public async Task Job_executado_duas_vezes_ao_mesmo_tempo_faz_o_efeito_uma_vez()
    {
        var id = await ReceiveAsync(fixture.Tenancy.TenantA);
        TestWebhookHandler.DelayMs = 300;
        try
        {
            var results = await Task.WhenAll(fixture.RunJobAsync(fixture.Tenancy.TenantA, id), fixture.RunJobAsync(fixture.Tenancy.TenantA, id));
            results.Should().OnlyContain(r => r == null);
        }
        finally
        {
            TestWebhookHandler.DelayMs = 0;
        }

        (await fixture.RunJobAsync(fixture.Tenancy.TenantA, id)).Should().BeNull("já Processed: nada a fazer");
        TestWebhookHandler.Calls[id].Should().Be(1);
        (await fixture.EventAsync(fixture.Tenancy.TenantA, id)).Status.Should().Be(WebhookEventStatus.Processed);
    }

    [Fact]
    public async Task Dois_tenants_ao_mesmo_tempo_nao_se_misturam_e_chave_ou_segredo_de_um_nao_vale_no_outro()
    {
        var a = ReceiveAsync(fixture.Tenancy.TenantA);
        var b = ReceiveAsync(fixture.Tenancy.TenantB);
        var ids = await Task.WhenAll(a, b);

        (await fixture.QueryAsync(fixture.Tenancy.TenantB, db => db.WebhookEvents.AnyAsync(e => e.Id == ids[0]))).Should().BeFalse("o evento de A não está no banco de B");
        (await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.WebhookEvents.AnyAsync(e => e.Id == ids[1]))).Should().BeFalse();
        (await fixture.RunJobAsync(fixture.Tenancy.TenantB, ids[0])).Should().BeNull();
        TestWebhookHandler.Calls.ContainsKey(ids[0]).Should().BeFalse("job de B com o id de A não acha nada");

        var keyB = await fixture.KeyAsync(fixture.Tenancy.TenantB);
        var secretA = await fixture.ConfigureAsync(fixture.Tenancy.TenantA, WebhookFixture.Hmac);
        (await fixture.Platform().SendAsync(WebhookFixture.SignedPost(Path("test", keyB), secretA, WebhookFixture.Body()))).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "o segredo de A não vale com a chave de B");
        var tokenA = await fixture.ConfigureAsync(fixture.Tenancy.TenantA, WebhookFixture.Token);
        (await fixture.Platform().SendAsync(WebhookFixture.Json(Path("test-token", keyB, "?t=" + tokenA), WebhookFixture.Body()))).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "o token de A não vale com a chave de B");
    }

    [Fact]
    public async Task Modulo_dono_nao_liberado_marca_Ignored_com_o_motivo()
    {
        var id = await ReceiveAsync(fixture.Tenancy.TenantB, provider: WebhookFixture.Fiscal);
        await fixture.SetModuleAsync(fixture.Tenancy.TenantB, "fiscal", released: false);
        try
        {
            (await fixture.RunJobAsync(fixture.Tenancy.TenantB, id)).Should().BeNull();
        }
        finally
        {
            await fixture.SetModuleAsync(fixture.Tenancy.TenantB, "fiscal", released: true);
        }

        var evt = await fixture.EventAsync(fixture.Tenancy.TenantB, id);
        evt.Status.Should().Be(WebhookEventStatus.Ignored);
        evt.LastError.Should().Be(ProcessWebhookEventJob.ModuleNotReleased);
        TestWebhookHandler.Calls.ContainsKey(id).Should().BeFalse();
    }

    [Fact]
    public async Task Handler_que_ignora_marca_Ignored()
    {
        var id = await ReceiveAsync(fixture.Tenancy.TenantA, "ignorar");

        (await fixture.RunJobAsync(fixture.Tenancy.TenantA, id)).Should().BeNull();

        var evt = await fixture.EventAsync(fixture.Tenancy.TenantA, id);
        evt.Status.Should().Be(WebhookEventStatus.Ignored);
        evt.LastError.Should().Be("Evento sem efeito");
    }

    [Fact]
    public async Task Falha_marca_Failed_sem_dado_pessoal_e_na_decima_vai_para_dead_letter_com_aviso_e_alerta()
    {
        var (editor, _) = await fixture.ActorAsync(fixture.Tenancy.TenantA, "a.localhost", grants: ["finance.view", "finance.edit"]);
        var id = await ReceiveAsync(fixture.Tenancy.TenantA, "falhar");

        var first = await fixture.RunJobAsync(fixture.Tenancy.TenantA, id);
        first.Should().BeOfType<WebhookProcessingFailedException>("o Hangfire tenta de novo");
        var failed = await fixture.EventAsync(fixture.Tenancy.TenantA, id);
        failed.Status.Should().Be(WebhookEventStatus.Failed);
        failed.LastError.Should().NotContain("529").And.Contain("InvalidOperationException");

        for (var i = 2; i <= ProcessWebhookEventJob.MaxAttempts; i++)
        {
            await fixture.RunJobAsync(fixture.Tenancy.TenantA, id);
        }

        var dead = await fixture.EventAsync(fixture.Tenancy.TenantA, id);
        dead.Status.Should().Be(WebhookEventStatus.DeadLettered);
        dead.Attempts.Should().Be(ProcessWebhookEventJob.MaxAttempts);
        (await fixture.RunJobAsync(fixture.Tenancy.TenantA, id)).Should().BeNull("parado não roda mais sozinho");
        fixture.Alerts.Raised.Should().Contain(a => a.Kind == WebhookAlerts.DeadLetter && a.Tenant == fixture.Tenancy.TenantA.Key);
        (await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.Notifications.AnyAsync(n => n.UserId == editor && n.Type == ProcessWebhookEventJob.DeadLetterNotification && n.Link == WebhookLinks.Stalled)))
            .Should().BeTrue("quem edita o módulo dono recebe o aviso com o Reprocessar");
    }

    [Fact]
    public async Task Varredor_recoloca_evento_parado_no_outbox_e_alerta_quando_continua_parado()
    {
        var id = await ReceiveAsync(fixture.Tenancy.TenantA);
        await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.WebhookEvents.Where(e => e.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.LastQueuedAt, DateTimeOffset.UtcNow.AddMinutes(-10))));
        var sweep = ActivatorUtilities.CreateInstance<WebhookStalledSweepJob>(fixture.Factory.Services);

        for (var i = 0; i < WebhookStalledSweepJob.StalledAfter; i++)
        {
            await sweep.ExecuteAsync(fixture.Tenancy.TenantA.Id, new WebhookStalledSweepArgs(), CancellationToken.None);
            await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.WebhookEvents.Where(e => e.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.LastQueuedAt, DateTimeOffset.UtcNow.AddMinutes(-10))));
        }

        var evt = await fixture.EventAsync(fixture.Tenancy.TenantA, id);
        evt.Requeues.Should().Be(WebhookStalledSweepJob.StalledAfter);
        (await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.OutboxMessages.CountAsync(m => m.Type == IntegrationEventRegistry.NameOf(typeof(WebhookReceived)) && m.Payload.Contains(id.ToString()))))
            .Should().BeGreaterThan(1, "mensagem nova a cada volta");
        fixture.Alerts.Raised.Should().Contain(a => a.Kind == WebhookAlerts.Stalled && a.Tenant == fixture.Tenancy.TenantA.Key);
    }

    [Fact]
    public async Task Expurgo_apaga_o_corpo_depois_de_90_dias_e_a_deduplicacao_continua()
    {
        var key = await fixture.KeyAsync(fixture.Tenancy.TenantA);
        var secret = await fixture.ConfigureAsync(fixture.Tenancy.TenantA, WebhookFixture.Hmac);
        var body = WebhookFixture.Body();
        var delivery = "d-" + Guid.NewGuid().ToString("N");
        (await fixture.Platform().SendAsync(WebhookFixture.SignedPost(Path("test", key), secret, body, delivery))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var processed = (await fixture.EventByDeliveryAsync(fixture.Tenancy.TenantA, "test", delivery))!.Id;
        await fixture.RunJobAsync(fixture.Tenancy.TenantA, processed);
        var failing = await ReceiveAsync(fixture.Tenancy.TenantA, "falhar");
        await fixture.RunJobAsync(fixture.Tenancy.TenantA, failing);
        await fixture.QueryAsync(fixture.Tenancy.TenantA, db => db.WebhookEvents.Where(e => e.Id == processed || e.Id == failing)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.ReceivedAt, DateTimeOffset.UtcNow.AddDays(-91))));

        var purge = ActivatorUtilities.CreateInstance<WebhookEventsPurgeJob>(fixture.Factory.Services);
        await purge.ExecuteAsync(fixture.Tenancy.TenantA.Id, new WebhookEventsPurgeArgs(), CancellationToken.None);
        await purge.ExecuteAsync(fixture.Tenancy.TenantA.Id, new WebhookEventsPurgeArgs(), CancellationToken.None); // idempotente

        var purged = await fixture.EventAsync(fixture.Tenancy.TenantA, processed);
        purged.Payload.Should().BeNull();
        purged.BodySha256.Should().HaveCount(32, "a linha fica para a deduplicação");
        (await fixture.EventAsync(fixture.Tenancy.TenantA, failing)).Payload.Should().NotBeNull("Failed não é expurgado");
        (await fixture.Platform().SendAsync(WebhookFixture.SignedPost(Path("test", key), secret, body, "d-novo-" + Guid.NewGuid().ToString("N")))).StatusCode
            .Should().Be(HttpStatusCode.OK, "o mesmo corpo continua deduplicado pelo hash");
    }
}
