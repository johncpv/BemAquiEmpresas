using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Integration;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Webhooks;
using BemAqui.Fiscal.Contracts;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Application.ConnectedApps;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Observability;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Application.Modules;
using BemAqui.Platform.Domain;
using FluentAssertions;
using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Core;

namespace BemAqui.Integration.Tests.Webhooks;

/// <summary>Recebedor falso dos avisos (substitui o handler do <c>HttpClient</c> <c>outbound-webhooks</c>): guarda cada pedido e responde o código pedido.</summary>
public sealed class FakeReceiver
{
    public sealed record Captured(string Type, string EventId, string Timestamp, string Signature, string UserAgent, byte[] Body);

    public ConcurrentQueue<Captured> Requests { get; } = new();

    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    public int DelayMs { get; set; }

    public async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
        if (DelayMs > 0)
        {
            await Task.Delay(DelayMs, cancellationToken);
        }

        Requests.Enqueue(new Captured(Header(request, OutboundWebhookSigner.EventTypeHeader), Header(request, OutboundWebhookSigner.EventIdHeader),
            Header(request, OutboundWebhookSigner.TimestampHeader), Header(request, OutboundWebhookSigner.SignatureHeader), request.Headers.UserAgent.ToString(), body));
        return new HttpResponseMessage(Status) { Content = new StringContent("{\"segredo-da-resposta\":true}") };
    }

    private static string Header(HttpRequestMessage request, string name) => request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : string.Empty;

    public sealed class Handler(FakeReceiver receiver) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => receiver.HandleAsync(request, cancellationToken);
    }
}

/// <summary>
/// INT-02: avisos de saída com PostgreSQL real e o recebedor falso. A conexão é gravada direto (o fluxo OAuth é da INT-01); os testes da classe
/// rodam em sequência e cada um encerra as inscrições que sobraram antes de criar a sua.
/// </summary>
public class OutboundWebhookTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string HostA = "a.localhost";
    private const string HostB = "b.localhost";
    private const string Url = "https://conecta.test/avisos";

    private FakeReceiver Receiver => tenancy.State<FakeReceiver>();

    private MemorySink Logs => tenancy.State<MemorySink>();

    private CapturingAlertSink Alerts => tenancy.State<CapturingAlertSink>();

    private CapturingJobClient Jobs => tenancy.State<CapturingJobClient>();

    private WebApplicationFactory<Program> Factory => tenancy.Shared("outbound", b =>
    {
        b.UseSetting("Integrations:Conecta:WebhookUrl", Url);
        b.UseSetting("Integrations:Conecta:WebhookAllowedHosts:0", "conecta.test");
        b.ConfigureTestServices(s =>
        {
            s.AddHttpClient(OutboundWebhookDeliveryJob.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new FakeReceiver.Handler(tenancy.State<FakeReceiver>()));
            s.AddSingleton<ILogEventSink>(tenancy.State<MemorySink>());
            s.RemoveAll<IAlertSink>();
            s.AddSingleton<IAlertSink>(tenancy.State<CapturingAlertSink>());
            s.RemoveAll<IBackgroundJobClient>();
            s.AddSingleton<IBackgroundJobClient>(tenancy.State<CapturingJobClient>());
        });
    });

    private Tenant TenantOf(string host) => host == HostA ? tenancy.TenantA : tenancy.TenantB;

    private async Task<AsyncServiceScope> ScopeAsync(string host = HostA)
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(TenantOf(host).Id))!;
        return await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task<T> DbAsync<T>(Func<TenantDbContext, Task<T>> query, string host = HostA)
    {
        await using var scope = await ScopeAsync(host);
        return await query(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    private async Task ReleaseAsync(string host, params string[] modules)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<TenantModuleService>().SaveAsync(TenantOf(host).Id, [.. modules.Select(m => new ModuleReleaseChange(m, true))], null, null))
            .IsSuccess.Should().BeTrue();
        Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    private sealed record Owner(Guid Id, HttpClient Client);

    private async Task<Owner> OwnerAsync(string host = HostA)
    {
        await ReleaseAsync(host, "conecta", "fiscal", "finance");
        var tenant = TenantOf(host);
        var (id, stamp) = await tenancy.SeedUserAsync(tenant, "out" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using (var scope = await ScopeAsync(host))
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            var groupId = await db.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = groupId });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IPermissionService>().BumpAsync([id]);
        }

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenant.Id, host, userId: id, stamp: stamp));
        return new Owner(id, client);
    }

    private sealed record Connected(Guid ConnectionId, Guid SubscriptionId, string Secret);

    /// <summary>Conexão ativa gravada direto e a inscrição pela <see cref="IOutboundSubscriptions"/>, na mesma transação (CA 1).</summary>
    private async Task<Connected> ConnectAsync(Owner owner, string[] scopes, string host = HostA, Guid? replaced = null)
    {
        await using var scope = await ScopeAsync(host);
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<TenantDbContext>();
        var now = DateTimeOffset.UtcNow;
        foreach (var leftover in await db.OutboundSubscriptions.Where(s => s.Status != OutboundSubscriptionStatus.Ended && s.ConnectionId != replaced).ToListAsync())
        {
            leftover.End(now);
        }

        var app = await sp.GetRequiredService<OAuthAuthorizeService>().EnsureAppAsync(CancellationToken.None);
        foreach (var old in await sp.GetRequiredService<IIdentityDb>().AppConnections.Where(c => c.AppId == app.Id && c.Status != AppConnectionStatus.Revoked).ToListAsync())
        {
            old.Revoke(AppConnectionReason.Replaced, "teste", null, now);
        }

        await db.SaveChangesAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var connection = AppConnection.Create(app.Id, owner.Id, scopes, "Loja de teste", now);
        sp.GetRequiredService<IIdentityDb>().AppConnections.Add(connection);
        await db.SaveChangesAsync();
        var secret = await sp.GetRequiredService<IOutboundSubscriptions>().CreateForConnectionAsync(connection.Id, app.ClientId, replaced, scopes);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        secret.Should().NotBeNullOrEmpty();
        var subscriptionId = await db.OutboundSubscriptions.Where(s => s.ConnectionId == connection.Id).Select(s => s.Id).SingleAsync();
        return new Connected(connection.Id, subscriptionId, secret!);
    }

    private static async Task<Guid> PingAsync(Owner owner, Guid subscriptionId)
    {
        var response = await owner.Client.PostAsync($"/api/v1/outbound-webhooks/subscriptions/{subscriptionId}/test", null);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.GetProperty("deliveryId").GetGuid();
    }

    private async Task RunJobAsync(Guid deliveryId, string host = HostA, bool due = true)
    {
        if (due)
        {
            await DbAsync(db => db.OutboundDeliveries.Where(d => d.Id == deliveryId).ExecuteUpdateAsync(s => s.SetProperty(d => d.NextAttemptAt, DateTimeOffset.UtcNow.AddMinutes(-1))), host);
        }

        await ActivatorUtilities.CreateInstance<OutboundWebhookDeliveryJob>(Factory.Services).ExecuteAsync(TenantOf(host).Id, new OutboundDeliveryJobArgs(deliveryId), CancellationToken.None);
    }

    private Task<OutboundDelivery> DeliveryAsync(Guid id, string host = HostA) => DbAsync(db => db.OutboundDeliveries.AsNoTracking().SingleAsync(d => d.Id == id), host);

    private FakeReceiver.Captured LastFor(Guid eventId) => Receiver.Requests.Last(r => r.EventId == eventId.ToString());

    [Fact]
    public async Task Inscricao_nasce_na_transacao_da_conexao_limitada_a_concessao_e_o_segredo_sai_uma_vez()
    {
        var owner = await OwnerAsync();
        var connected = await ConnectAsync(owner, ["finance.view", "parties.view"]);
        var subscription = await DbAsync(db => db.OutboundSubscriptions.AsNoTracking().SingleAsync(s => s.Id == connected.SubscriptionId));
        subscription.Url.Should().Be(Url);
        subscription.Events.Should().BeEquivalentTo("billing_slip.issued", "billing_slip.cancelled", "billing_slip.paid", "app.disconnected");
        subscription.SecretCipher.Should().NotContain(connected.Secret, "o segredo fica cifrado");

        await using (var scope = await ScopeAsync())
        {
            (await scope.ServiceProvider.GetRequiredService<IOutboundSubscriptions>().TakePendingSecretAsync(connected.ConnectionId)).Should().BeNull("o segredo da criação já saiu na troca");
        }

        // CA 1: falha ao criar a inscrição desfaz a conexão (URL fora da regra).
        await using var broken = Factory.WithWebHostBuilder(b => b.UseSetting("Integrations:Conecta:WebhookUrl", "http://conecta.test/avisos"));
        var info = (await broken.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        await using var brokenScope = await broken.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        var act = () => brokenScope.ServiceProvider.GetRequiredService<IOutboundSubscriptions>().CreateForConnectionAsync(Guid.NewGuid(), "conecta", null, ["finance.view"]);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Substituicao_encerra_a_anterior_e_as_pendentes_ficam_ignoradas()
    {
        var owner = await OwnerAsync();
        var first = await ConnectAsync(owner, ["finance.view"]);
        var pending = await PingAsync(owner, first.SubscriptionId);
        var second = await ConnectAsync(owner, ["finance.view"], replaced: first.ConnectionId);

        (await DbAsync(db => db.OutboundSubscriptions.AsNoTracking().SingleAsync(s => s.Id == first.SubscriptionId))).Status.Should().Be(OutboundSubscriptionStatus.Ended);
        (await DbAsync(db => db.OutboundSubscriptions.AsNoTracking().SingleAsync(s => s.Id == second.SubscriptionId))).Status.Should().Be(OutboundSubscriptionStatus.Active);
        var skipped = await DeliveryAsync(pending);
        skipped.Status.Should().Be(OutboundDeliveryStatus.Skipped);
        skipped.LastError.Should().Be(OutboundSubscriptionService.ReplacedReason);
    }

    [Fact]
    public async Task Nota_autorizada_vira_aviso_entregue_assinado_sem_imposto_e_reentrega_nao_duplica()
    {
        var owner = await OwnerAsync();
        var connected = await ConnectAsync(owner, ["fiscal.view"]);
        var invoiceId = Guid.CreateVersion7();
        var partyId = Guid.CreateVersion7();
        await using (var scope = await ScopeAsync())
        {
            scope.ServiceProvider.GetRequiredService<IEventPublisher>().Publish(new InvoiceAuthorized(invoiceId, InvoiceType.NFe, null, null, partyId, new DateOnly(2026, 11, 27), 1500.50m,
                [new InvoiceTaxAmount("ICMS", 270.09m, false)], 33.33m));
            await scope.ServiceProvider.GetRequiredService<TenantDbContext>().SaveChangesAsync();
        }

        await DrainAsync(invoiceId);
        var evt = await DbAsync(db => db.OutboundEvents.AsNoTracking().SingleAsync(e => e.ResourceId == invoiceId));
        var delivery = await DbAsync(db => db.OutboundDeliveries.AsNoTracking().SingleAsync(d => d.EventId == evt.Id && d.SubscriptionId == connected.SubscriptionId));
        Jobs.All.Should().Contain(j => j.Type == typeof(OutboundWebhookDeliveryJob) && j.Args.Count >= 2 && j.Args[0] is Guid && j.Args[1] is OutboundDeliveryJobArgs);

        await RunJobAsync(delivery.Id);
        (await DeliveryAsync(delivery.Id)).Status.Should().Be(OutboundDeliveryStatus.Delivered);
        var sent = LastFor(evt.Id);
        sent.Type.Should().Be("invoice.authorized");
        sent.UserAgent.Should().Be(OutboundWebhookSigner.UserAgent);
        sent.Signature.Should().Be(OutboundWebhookSigner.Sign(connected.Secret, long.Parse(sent.Timestamp, System.Globalization.CultureInfo.InvariantCulture), sent.Body));
        var json = JsonDocument.Parse(sent.Body).RootElement;
        json.GetProperty("contract").GetInt32().Should().Be(1);
        json.GetProperty("resource").GetString().Should().Be("/api/v1/integration/invoices/" + invoiceId);
        json.GetProperty("data").EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("id", "type", "issued_on", "total", "party_id");
        Encoding.UTF8.GetString(sent.Body).Should().NotContain("270.09").And.NotContain("33.33").And.NotContain("ICMS");

        // CA 7: a mesma mensagem do outbox processada de novo não cria outro aviso (inbox).
        await DbAsync(db => db.Database.ExecuteSqlRawAsync("UPDATE shared.outbox_messages SET processed_at = NULL, next_attempt_at = now() WHERE type LIKE '%InvoiceAuthorized' AND payload::text LIKE {0}", "%" + invoiceId + "%"));
        await DrainAsync(invoiceId);
        (await DbAsync(db => db.OutboundEvents.CountAsync(e => e.ResourceId == invoiceId))).Should().Be(1);
    }

    /// <summary>Processa o outbox até a mensagem da nota estar processada pelo tradutor (o dispatcher do host pode pegar parte; SKIP LOCKED).</summary>
    private async Task DrainAsync(Guid invoiceId)
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        var like = "%" + invoiceId + "%";
        for (var round = 0; round < 60; round++)
        {
            await using (var scope = await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info))
            {
                await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(info);
            }

            var done = await DbAsync(db => db.Database.SqlQuery<int>(
                $"SELECT count(*)::int AS \"Value\" FROM shared.inbox_messages i JOIN shared.outbox_messages m ON m.id = i.message_id WHERE m.payload::text LIKE {like} AND i.handler LIKE '%InvoiceWebhooks'").SingleAsync());
            if (done > 0)
            {
                return;
            }

            await Task.Delay(250);
        }

        var errors = await DbAsync(db => db.Database.SqlQuery<string>(
            $"SELECT coalesce(m.error, 'sem erro') AS \"Value\" FROM shared.outbox_messages m WHERE m.payload::text LIKE {like}").ToListAsync());
        errors.Should().BeEmpty("a mensagem da nota devia ter sido processada");
    }

    [Fact]
    public async Task Sem_o_escopo_a_entrega_e_ignorada_com_o_motivo_e_nada_sai()
    {
        var owner = await OwnerAsync();
        var connected = await ConnectAsync(owner, ["finance.view"]);
        Guid deliveryId;
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var subscription = await db.OutboundSubscriptions.SingleAsync(s => s.Id == connected.SubscriptionId);
            subscription.Events = [.. subscription.Events, "invoice.rejected"]; // força a inscrição; o envio reavalia a concessão (CA 8)
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<IOutboundWebhookPublisher>().PublishAsync(new OutboundWebhookMessage("invoice.rejected", Guid.CreateVersion7(), DateTimeOffset.UtcNow,
                new Dictionary<string, object?> { ["id"] = Guid.NewGuid(), ["type"] = "nfe" }));
            await db.SaveChangesAsync();
            deliveryId = await db.OutboundDeliveries.Where(d => d.SubscriptionId == connected.SubscriptionId).OrderByDescending(d => d.CreatedAt).Select(d => d.Id).FirstAsync();
        }

        var before = Receiver.Requests.Count;
        await RunJobAsync(deliveryId);
        var delivery = await DeliveryAsync(deliveryId);
        delivery.Status.Should().Be(OutboundDeliveryStatus.Skipped);
        delivery.LastError.Should().Be("Sem permissão para ver notas");
        Receiver.Requests.Count.Should().Be(before);

        // CA 5: campo fora do catálogo nunca sai.
        await using var other = await ScopeAsync();
        var act = () => other.ServiceProvider.GetRequiredService<IOutboundWebhookPublisher>().PublishAsync(new OutboundWebhookMessage("invoice.rejected", Guid.NewGuid(), DateTimeOffset.UtcNow,
            new Dictionary<string, object?> { ["id"] = Guid.NewGuid(), ["party_name"] = "Maria" }));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Oito_falhas_param_alertam_e_Reenviar_comeca_nova_serie_com_o_mesmo_corpo()
    {
        var owner = await OwnerAsync();
        var connected = await ConnectAsync(owner, ["finance.view"]);
        var deliveryId = await PingAsync(owner, connected.SubscriptionId);
        Receiver.Status = HttpStatusCode.InternalServerError;
        try
        {
            for (var i = 0; i < OutboundRetryPolicy.MaxAttempts; i++)
            {
                await RunJobAsync(deliveryId);
                var current = await DeliveryAsync(deliveryId);
                current.Status.Should().Be(i < OutboundRetryPolicy.MaxAttempts - 1 ? OutboundDeliveryStatus.Retrying : OutboundDeliveryStatus.DeadLettered);
            }
        }
        finally
        {
            Receiver.Status = HttpStatusCode.OK;
        }

        var dead = await DeliveryAsync(deliveryId);
        dead.Attempts.Should().Be(8);
        dead.LastStatusCode.Should().Be(500);
        Alerts.Raised.Should().Contain(a => a.Kind == AlertKinds.OutboundWebhooks);
        var summary = JsonDocument.Parse(await owner.Client.GetStringAsync("/api/v1/outbound-webhooks/summary")).RootElement;
        summary.GetProperty("healthy").GetBoolean().Should().BeFalse();
        summary.GetProperty("deadLettered").GetInt32().Should().BeGreaterThan(0);

        var detail = JsonDocument.Parse(await owner.Client.GetStringAsync($"/api/v1/outbound-webhooks/deliveries/{deliveryId}")).RootElement;
        detail.GetProperty("attempts").GetArrayLength().Should().Be(8);
        detail.GetProperty("delivery").GetProperty("canResend").GetBoolean().Should().BeTrue();
        var eventId = dead.EventId;
        var firstBody = Receiver.Requests.First(r => r.EventId == eventId.ToString()).Body;

        (await owner.Client.PostAsync($"/api/v1/outbound-webhooks/deliveries/{deliveryId}/resend", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DeliveryAsync(deliveryId)).Status.Should().Be(OutboundDeliveryStatus.Pending);
        await RunJobAsync(deliveryId);
        (await DeliveryAsync(deliveryId)).Status.Should().Be(OutboundDeliveryStatus.Delivered);
        LastFor(eventId).Body.Should().Equal(firstBody, "o mesmo id e os mesmos bytes em todas as tentativas (CA 9)");
        (await DbAsync(db => db.AuditEntries.AnyAsync(a => a.Entity == "OutboundDelivery" && a.EntityId == deliveryId.ToString() && a.UserId == owner.Id))).Should().BeTrue();
    }

    [Fact]
    public async Task Troca_de_segredo_sai_na_renovacao_assina_com_as_duas_e_com_immediate_so_com_a_nova()
    {
        var owner = await OwnerAsync();
        var connected = await ConnectAsync(owner, ["finance.view"]);
        var rotate = await owner.Client.PostAsJsonAsync($"/api/v1/outbound-webhooks/subscriptions/{connected.SubscriptionId}/secret/rotate", new { immediate = false });
        rotate.StatusCode.Should().Be(HttpStatusCode.OK, await rotate.Content.ReadAsStringAsync());
        string fresh;
        await using (var scope = await ScopeAsync())
        {
            var subscriptions = scope.ServiceProvider.GetRequiredService<IOutboundSubscriptions>();
            fresh = (await subscriptions.TakePendingSecretAsync(connected.ConnectionId))!;
            fresh.Should().NotBeNullOrEmpty().And.NotBe(connected.Secret);
            (await subscriptions.TakePendingSecretAsync(connected.ConnectionId)).Should().BeNull("uma vez só");
        }

        var ping = await PingAsync(owner, connected.SubscriptionId);
        await RunJobAsync(ping);
        var sent = LastFor((await DeliveryAsync(ping)).EventId);
        var ts = long.Parse(sent.Timestamp, System.Globalization.CultureInfo.InvariantCulture);
        sent.Signature.Should().Be(OutboundWebhookSigner.Sign(fresh, ts, sent.Body) + "," + OutboundWebhookSigner.Sign(connected.Secret, ts, sent.Body));

        (await owner.Client.PostAsJsonAsync($"/api/v1/outbound-webhooks/subscriptions/{connected.SubscriptionId}/secret/rotate", new { immediate = true })).StatusCode.Should().Be(HttpStatusCode.OK);
        string newest;
        await using (var scope = await ScopeAsync())
        {
            newest = (await scope.ServiceProvider.GetRequiredService<IOutboundSubscriptions>().TakePendingSecretAsync(connected.ConnectionId))!;
        }

        var second = await PingAsync(owner, connected.SubscriptionId);
        await RunJobAsync(second);
        var last = LastFor((await DeliveryAsync(second)).EventId);
        last.Signature.Should().Be(OutboundWebhookSigner.Sign(newest, long.Parse(last.Timestamp, System.Globalization.CultureInfo.InvariantCulture), last.Body));

        // Auditada sem o valor; limite de 5 por dia.
        var audit = await DbAsync(db => db.AuditEntries.Where(a => a.Entity == nameof(OutboundSubscription) && a.EntityId == connected.SubscriptionId.ToString()).Select(a => a.Changes).ToListAsync());
        string.Join("|", audit).Should().NotContain(fresh).And.NotContain(newest);
        for (var i = 0; i < 3; i++)
        {
            (await owner.Client.PostAsJsonAsync($"/api/v1/outbound-webhooks/subscriptions/{connected.SubscriptionId}/secret/rotate", new { immediate = false })).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await owner.Client.PostAsJsonAsync($"/api/v1/outbound-webhooks/subscriptions/{connected.SubscriptionId}/secret/rotate", new { immediate = false })).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Revogacao_manda_app_disconnected_e_encerra_e_suspensao_retem()
    {
        var owner = await OwnerAsync();
        var connected = await ConnectAsync(owner, ["finance.view"]);
        var pending = await PingAsync(owner, connected.SubscriptionId);
        await using (var scope = await ScopeAsync())
        {
            await scope.ServiceProvider.GetRequiredService<OutboundSubscriptionConsumers>().HandleAsync(new BemAqui.Identity.Contracts.AppConnectionRevoked(connected.ConnectionId, Guid.NewGuid()), CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<TenantDbContext>().SaveChangesAsync();
        }

        (await DbAsync(db => db.OutboundSubscriptions.AsNoTracking().SingleAsync(s => s.Id == connected.SubscriptionId))).Status.Should().Be(OutboundSubscriptionStatus.Ended);
        (await DeliveryAsync(pending)).LastError.Should().Be(OutboundSubscriptionService.EndedReason);
        var disconnected = await DbAsync(db => (from d in db.OutboundDeliveries join e in db.OutboundEvents on d.EventId equals e.Id
                                                where d.SubscriptionId == connected.SubscriptionId && e.Type == OutboundWebhookCatalog.AppDisconnected
                                                select d.Id).SingleAsync());
        await RunJobAsync(disconnected);
        (await DeliveryAsync(disconnected)).Status.Should().Be(OutboundDeliveryStatus.Delivered);
        var sent = LastFor((await DeliveryAsync(disconnected)).EventId);
        sent.Signature.Should().Be(OutboundWebhookSigner.Sign(connected.Secret, long.Parse(sent.Timestamp, System.Globalization.CultureInfo.InvariantCulture), sent.Body));

        var held = await ConnectAsync(owner, ["finance.view"]);
        var queued = await PingAsync(owner, held.SubscriptionId);
        await using (var scope = await ScopeAsync())
        {
            await scope.ServiceProvider.GetRequiredService<OutboundSubscriptionConsumers>().HandleAsync(new BemAqui.Identity.Contracts.AppConnectionSuspended(held.ConnectionId, Guid.NewGuid()), CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<TenantDbContext>().SaveChangesAsync();
        }

        (await DeliveryAsync(queued)).Status.Should().Be(OutboundDeliveryStatus.Held);
    }

    [Fact]
    public async Task Execucao_dupla_envia_uma_vez()
    {
        var owner = await OwnerAsync();
        var connected = await ConnectAsync(owner, ["finance.view"]);
        var deliveryId = await PingAsync(owner, connected.SubscriptionId);
        var eventId = (await DeliveryAsync(deliveryId)).EventId;
        Receiver.DelayMs = 300;
        try
        {
            await Task.WhenAll(RunJobAsync(deliveryId, due: false), RunJobAsync(deliveryId, due: false));
        }
        finally
        {
            Receiver.DelayMs = 0;
        }

        Receiver.Requests.Count(r => r.EventId == eventId.ToString()).Should().Be(1);
        (await DbAsync(db => db.OutboundAttempts.CountAsync(a => a.DeliveryId == deliveryId))).Should().Be(1);
    }

    [Fact]
    public async Task Expurgo_apaga_o_conteudo_depois_de_90_dias_e_Reenviar_fica_desabilitado()
    {
        var owner = await OwnerAsync();
        var connected = await ConnectAsync(owner, ["finance.view"]);
        var deliveryId = await PingAsync(owner, connected.SubscriptionId);
        await RunJobAsync(deliveryId);
        var eventId = (await DeliveryAsync(deliveryId)).EventId;
        await DbAsync(db => db.OutboundEvents.Where(e => e.Id == eventId).ExecuteUpdateAsync(s => s.SetProperty(e => e.CreatedAt, DateTimeOffset.UtcNow.AddDays(-91))));
        await ActivatorUtilities.CreateInstance<OutboundWebhookPurgeJob>(Factory.Services).ExecuteAsync(tenancy.TenantA.Id, new OutboundWebhookPurgeArgs(), CancellationToken.None);

        (await DbAsync(db => db.OutboundEvents.AsNoTracking().SingleAsync(e => e.Id == eventId))).Data.Should().BeNull();
        var detail = JsonDocument.Parse(await owner.Client.GetStringAsync($"/api/v1/outbound-webhooks/deliveries/{deliveryId}")).RootElement;
        detail.GetProperty("body").ValueKind.Should().Be(JsonValueKind.Null);
        detail.GetProperty("delivery").GetProperty("canResend").GetBoolean().Should().BeFalse();
        detail.GetProperty("delivery").GetProperty("contentPurged").GetBoolean().Should().BeTrue();
        (await owner.Client.PostAsync($"/api/v1/outbound-webhooks/deliveries/{deliveryId}/resend", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Aplicativo_ve_a_inscricao_outro_tenant_recebe_404_e_o_log_nao_tem_segredo_assinatura_nem_corpo()
    {
        var owner = await OwnerAsync();
        var connected = await ConnectAsync(owner, ["finance.view"]);
        var deliveryId = await PingAsync(owner, connected.SubscriptionId);
        await RunJobAsync(deliveryId);
        var sent = LastFor((await DeliveryAsync(deliveryId)).EventId);

        // CA 4: a rota da própria conexão com um token de acesso gravado direto.
        var token = AppClaims.AccessPrefix + OpaqueToken.New();
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            db.AppTokens.Add(new AppToken
            {
                TokenHash = OpaqueToken.Hash(token), Kind = AppTokenKind.Access, ConnectionId = connected.ConnectionId, FamilyId = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            });
            await db.SaveChangesAsync();
        }

        var app = Factory.CreateClient();
        app.DefaultRequestHeaders.Host = HostA;
        app.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var view = JsonDocument.Parse(await app.GetStringAsync("/api/v1/integration/webhooks/subscription")).RootElement;
        view.GetProperty("url").GetString().Should().Be(Url);
        view.GetProperty("status").GetString().Should().Be("Active");
        view.TryGetProperty("secret", out _).Should().BeFalse();

        // CA 18: outro tenant (ou id inexistente) responde 404; mudar avisos fora da concessão é recusado com a lista.
        var other = await OwnerAsync(HostB);
        (await other.Client.GetAsync($"/api/v1/outbound-webhooks/deliveries/{deliveryId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.Client.PostAsync($"/api/v1/outbound-webhooks/deliveries/{deliveryId}/resend", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var refused = await owner.Client.PutAsJsonAsync($"/api/v1/outbound-webhooks/subscriptions/{connected.SubscriptionId}", new { url = Url, events = new[] { "invoice.authorized" } });
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("invoice.authorized");
        (await owner.Client.PutAsJsonAsync($"/api/v1/outbound-webhooks/subscriptions/{connected.SubscriptionId}", new { url = "https://evil.test/x", events = new[] { "billing_slip.paid" } }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await owner.Client.PutAsJsonAsync($"/api/v1/outbound-webhooks/subscriptions/{connected.SubscriptionId}", new { url = Url, events = new[] { "billing_slip.paid" } }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // CA 17: canário.
        string text;
        lock (Logs.Events)
        {
            text = string.Join("\n", Logs.Events.Select(e => e.RenderMessage(System.Globalization.CultureInfo.InvariantCulture) + " " + string.Join(" ", e.Properties.Values)));
        }

        text.Should().NotContain(connected.Secret).And.NotContain(sent.Signature[3..]).And.NotContain(Encoding.UTF8.GetString(sent.Body)).And.NotContain("segredo-da-resposta");
    }
}
