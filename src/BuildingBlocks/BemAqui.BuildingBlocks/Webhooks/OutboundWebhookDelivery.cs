using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Integration;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BemAqui.BuildingBlocks.Webhooks;

public sealed record OutboundDeliveryJobArgs(Guid DeliveryId);

/// <summary>Resultado de um envio: código HTTP (ou nulo), duração, erro resumido e o <c>Retry-After</c> do 429/503.</summary>
public sealed record OutboundSendResult(bool Success, int? StatusCode, int DurationMs, string? Error, TimeSpan? RetryAfter);

/// <summary>
/// Envio de uma entrega (INT-02 CA 5, 8, 10 e 13). Reavalia a conexão, os módulos e o escopo antes de cada envio (CA 8), reserva a entrega pelo
/// <c>xmin</c> (a segunda execução simultânea não envia), manda os mesmos bytes do corpo com a assinatura do momento e agenda a próxima tentativa.
/// As novas tentativas são do próprio job (sem o retry do Hangfire). Nada do corpo, do segredo ou da assinatura vai para o log (CA 17).
/// </summary>
[AutomaticRetry(Attempts = 0)]
public sealed partial class OutboundWebhookDeliveryJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<OutboundWebhookDeliveryJob> logger)
    : TenantJob<OutboundDeliveryJobArgs>(directory, scopes, logger)
{
    public const string HttpClientName = "outbound-webhooks";
    public const string ConectaModule = "conecta";
    public const string DeadLetterNotification = "outbound_webhook_dead_lettered";
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan BusyRetry = TimeSpan.FromSeconds(5);
    public const int ResponseReadLimit = 4 * 1024;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PerSubscription = new(StringComparer.Ordinal);
    private static SemaphoreSlim? _perInstance;

    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, OutboundDeliveryJobArgs args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        var db = services.GetRequiredService<BemAquiDbContext>();
        var clock = services.GetRequiredService<TimeProvider>();
        var options = services.GetRequiredService<IOptions<OutboundWebhookOptions>>().Value;
        var now = clock.GetUtcNow();
        var delivery = await db.OutboundDeliveries.FirstOrDefaultAsync(d => d.Id == args.DeliveryId, cancellationToken);
        if (delivery is null || delivery.Status is not (OutboundDeliveryStatus.Pending or OutboundDeliveryStatus.Retrying) || delivery.NextAttemptAt > now + TimeSpan.FromSeconds(1))
        {
            return; // final, retida, ou já reservada/agendada por outra execução (CA 13)
        }

        var evt = await db.OutboundEvents.AsNoTracking().FirstAsync(e => e.Id == delivery.EventId, cancellationToken);
        var subscription = await db.OutboundSubscriptions.FirstAsync(s => s.Id == delivery.SubscriptionId, cancellationToken);

        var skip = await SkipReasonAsync(services, tenant, evt, subscription, cancellationToken);
        if (skip is not null)
        {
            if (skip == HeldMarker)
            {
                delivery.Status = OutboundDeliveryStatus.Held;
            }
            else
            {
                delivery.Skip(skip);
            }

            await SaveQuietlyAsync(db, cancellationToken);
            return;
        }

        if (evt.Data is null)
        {
            delivery.Skip("O conteúdo foi apagado depois de 90 dias");
            await SaveQuietlyAsync(db, cancellationToken);
            return;
        }

        var instance = LazyInitializer.EnsureInitialized(ref _perInstance, () => new SemaphoreSlim(Math.Max(1, options.MaxConcurrentPerInstance)));
        var gate = PerSubscription.GetOrAdd(tenant.Id + ":" + subscription.Id, _ => new SemaphoreSlim(Math.Max(1, options.MaxConcurrentPerSubscription)));
        if (!await gate.WaitAsync(0, cancellationToken))
        {
            services.GetRequiredService<OutboundDeliveryScheduler>().Enqueue(tenant.Id, delivery.Id, BusyRetry);
            return;
        }

        try
        {
            if (!await instance.WaitAsync(0, cancellationToken))
            {
                services.GetRequiredService<OutboundDeliveryScheduler>().Enqueue(tenant.Id, delivery.Id, BusyRetry);
                return;
            }

            try
            {
                await SendAsync(services, db, clock, tenant, delivery, evt, subscription, cancellationToken);
            }
            finally
            {
                instance.Release();
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private const string HeldMarker = "\u0000held";

    /// <summary>CA 3 e 8: inscrição encerrada ou suspensa, conexão inativa, módulo não liberado ou escopo perdido.</summary>
    private static async Task<string?> SkipReasonAsync(IServiceProvider services, TenantInfo tenant, OutboundEvent evt, OutboundSubscription subscription, CancellationToken cancellationToken)
    {
        if (evt.Type == OutboundWebhookCatalog.AppDisconnected)
        {
            return null; // o último aviso da conexão revogada sai mesmo com a inscrição Ended
        }

        switch (subscription.Status)
        {
            case OutboundSubscriptionStatus.Ended:
                return OutboundSubscriptionService.EndedReason;
            case OutboundSubscriptionStatus.Held:
                return HeldMarker;
        }

        var definition = OutboundWebhookCatalog.Find(evt.Type);
        var resolved = services.GetRequiredService<TenantContextHolder>().Resolved;
        if (resolved?.IsReleased(ConectaModule) != true || (definition?.Module is { } module && !resolved.IsReleased(module)))
        {
            return "Módulo não liberado";
        }

        return await services.GetRequiredService<IConnectedAppAccess>().DenyReasonAsync(subscription.ConnectionId, definition?.Scope, cancellationToken);
    }

    private async Task SendAsync(IServiceProvider services, BemAquiDbContext db, TimeProvider clock, TenantInfo tenant, OutboundDelivery delivery, OutboundEvent evt,
        OutboundSubscription subscription, CancellationToken cancellationToken)
    {
        // Reserva pelo xmin (CA 13): a outra execução simultânea recebe o conflito e não envia.
        var start = clock.GetUtcNow();
        delivery.Attempts++;
        delivery.LastAttemptAt = start;
        delivery.NextAttemptAt = start + Lease;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return;
        }

        var protector = services.GetRequiredService<ISecretProtector>();
        var scope = SecretScope.ForTenant(tenant.Id);
        var current = protector.Unprotect(scope, SecretKind.OutboundWebhookSecret, subscription.SecretCipher).Reveal();
        var previous = subscription.PreviousIfValid(start) is { } cipher ? protector.Unprotect(scope, SecretKind.OutboundWebhookSecret, cipher).Reveal() : null;
        var body = OutboundWebhookBody.Build(evt.Id, evt.Type, evt.OccurredAt, tenant.Key, subscription.ConnectionId, evt.Data!, evt.Resource);
        var result = await PostAsync(services.GetRequiredService<IHttpClientFactory>(), subscription.Url, evt, body, current, previous, start.ToUnixTimeSeconds(), cancellationToken, subscription.ConnectionId);

        var now = clock.GetUtcNow();
        db.OutboundAttempts.Add(new OutboundAttempt
        {
            DeliveryId = delivery.Id, Number = delivery.Attempts, At = start, StatusCode = result.StatusCode, DurationMs = result.DurationMs, Error = OutboundDelivery.Cut(result.Error),
        });
        delivery.LastStatusCode = result.StatusCode;
        delivery.LastError = OutboundDelivery.Cut(result.Error);
        TimeSpan? next = null;
        if (result.Success)
        {
            delivery.Status = OutboundDeliveryStatus.Delivered;
            delivery.DeliveredAt = now;
            subscription.LastSuccessAt = now;
        }
        else if (OutboundRetryPolicy.NextDelay(delivery.Attempts, result.RetryAfter, (Random.Shared.NextDouble() * 2) - 1) is { } wait)
        {
            delivery.Status = OutboundDeliveryStatus.Retrying;
            delivery.NextAttemptAt = now + wait;
            delivery.LastQueuedAt = now;
            next = wait;
        }
        else
        {
            delivery.Status = OutboundDeliveryStatus.DeadLettered;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // "Reenviar" ou o encerramento mudou a linha enquanto enviava: vale o que chegou primeiro; a tentativa fica registrada.
            db.ChangeTracker.Clear();
            db.OutboundAttempts.Add(new OutboundAttempt
            {
                DeliveryId = delivery.Id, Number = delivery.Attempts, At = start, StatusCode = result.StatusCode, DurationMs = result.DurationMs, Error = OutboundDelivery.Cut(result.Error),
            });
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        LogSent(logger, tenant.Id, delivery.Id, evt.Type, delivery.Status, result.StatusCode, result.DurationMs);
        if (next is { } delay)
        {
            services.GetRequiredService<OutboundDeliveryScheduler>().Enqueue(tenant.Id, delivery.Id, delay);
        }
        else if (delivery.Status == OutboundDeliveryStatus.DeadLettered)
        {
            await DeadLetterAsync(services, tenant, delivery, evt, cancellationToken);
        }
    }

    /// <summary>
    /// POST com os cabeçalhos do contrato (CA 5); 2xx em até 10 s é entregue; 3xx (não seguido), outro código, tempo esgotado e erro de rede são falha
    /// (CA 10). A resposta é lida até 4 KB e descartada (CA 15).
    /// </summary>
    public static async Task<OutboundSendResult> PostAsync(IHttpClientFactory factory, string url, OutboundEvent evt, byte[] body, string current, string? previous, long timestamp,
        CancellationToken cancellationToken, Guid? connectionId = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(evt);
        var watch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(body) };
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            request.Headers.TryAddWithoutValidation("User-Agent", OutboundWebhookSigner.UserAgent);
            request.Headers.Add(OutboundWebhookSigner.EventIdHeader, evt.Id.ToString());
            request.Headers.Add(OutboundWebhookSigner.EventTypeHeader, evt.Type);
            request.Headers.Add(OutboundWebhookSigner.TimestampHeader, timestamp.ToString(CultureInfo.InvariantCulture));
            request.Headers.Add(OutboundWebhookSigner.SignatureHeader, OutboundWebhookSigner.Header(current, previous, timestamp, body));
            if (connectionId is { } connection)
            {
                request.Headers.Add(OutboundWebhookSigner.ConnectionHeader, connection.ToString());
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await factory.CreateClient(HttpClientName).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            await DrainAsync(response, timeout.Token);
            var code = (int)response.StatusCode;
            var ok = code is >= 200 and < 300;
            TimeSpan? retryAfter = null;
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable && response.Headers.RetryAfter is { } header)
            {
                retryAfter = header.Delta ?? (header.Date is { } date ? date - DateTimeOffset.UtcNow : null);
            }

            return new OutboundSendResult(ok, code, (int)watch.ElapsedMilliseconds, ok ? null : "Resposta HTTP " + code.ToString(CultureInfo.InvariantCulture), retryAfter);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new OutboundSendResult(false, null, (int)watch.ElapsedMilliseconds, "Tempo esgotado (10 s).", null);
        }
        catch (HttpRequestException ex)
        {
            var refused = FindRefusal(ex);
            return new OutboundSendResult(false, null, (int)watch.ElapsedMilliseconds, refused ?? "Erro de rede (" + (ex.HttpRequestError.ToString()) + ").", null);
        }
    }

    private static string? FindRefusal(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is OutboundAddressRefusedException refused)
            {
                return refused.Message;
            }
        }

        return null;
    }

    private static async Task DrainAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[1024];
        var total = 0;
        int read;
        while (total < ResponseReadLimit && (read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
        }
    }

    /// <summary>CA 19: alerta <c>outbound_webhooks</c> (FND-12) e aviso aos Proprietários com o link do filtro "Parados".</summary>
    private static async Task DeadLetterAsync(IServiceProvider services, TenantInfo tenant, OutboundDelivery delivery, OutboundEvent evt, CancellationToken cancellationToken)
    {
        await services.GetRequiredService<IAlertSink>().RaiseAsync(
            new Alert(AlertKinds.OutboundWebhooks, tenant.Key, "Aviso " + evt.Type + " parou depois de " + OutboundRetryPolicy.MaxAttempts + " tentativas", delivery.LastError, OutboundWebhookLinks.Stalled),
            cancellationToken);
        var notifications = services.GetRequiredService<INotificationService>();
        var day = Time.BusinessTime.Today(services.GetRequiredService<TimeProvider>()).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        foreach (var owner in await services.GetRequiredService<IUserDirectory>().OwnerIdsAsync(cancellationToken))
        {
            await notifications.NotifyOnceAsync(owner, DeadLetterNotification, "O Conecta não está recebendo os avisos. Veja os parados e reenvie.", OutboundWebhookLinks.Stalled,
                "outbound-dead:" + day, cancellationToken);
        }
    }

    private static async Task SaveQuietlyAsync(BemAquiDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // mudou no meio: a próxima execução relê
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Aviso de saída do tenant {TenantId}: entrega {DeliveryId} ({Type}) {Status}, HTTP {StatusCode}, {Elapsed} ms")]
    private static partial void LogSent(ILogger logger, Guid tenantId, Guid deliveryId, string type, OutboundDeliveryStatus status, int? statusCode, int elapsed);
}
