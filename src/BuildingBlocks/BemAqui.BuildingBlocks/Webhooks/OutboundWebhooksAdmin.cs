using System.Text.Json.Serialization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Web;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.BuildingBlocks.Webhooks;

public sealed record OutboundEventOption(string Type, string Label, bool Subscribed);

/// <summary>Resumo de saúde da página do Conecta (CA 19) e o cabeçalho do "Avançado".</summary>
public sealed record OutboundWebhookSummary(
    bool Connected,
    Guid? SubscriptionId,
    string? Url,
    string? Status,
    bool Healthy,
    DateTimeOffset? FailingSince,
    int DeadLettered,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? SecretRotatedAt,
    DateTimeOffset? PreviousValidUntil,
    IReadOnlyList<OutboundEventOption> Events,
    bool CanEdit);

public sealed record OutboundDeliveryRow(
    Guid Id,
    Guid EventId,
    string Type,
    string TypeLabel,
    DateTimeOffset CreatedAt,
    string Status,
    int Attempts,
    int? LastStatusCode,
    string? LastError,
    DateTimeOffset? NextAttemptAt,
    bool CanResend,
    bool ContentPurged);

public sealed record OutboundDeliveryList(PagedList<OutboundDeliveryRow> Page, IReadOnlyList<OutboundEventOption> Types, int DeadLettered);

public sealed record OutboundAttemptView(int Number, DateTimeOffset At, int? StatusCode, int DurationMs, string? Error);

/// <summary>Painel do aviso (CA 15): o corpo e cada tentativa (sem o corpo da resposta).</summary>
public sealed record OutboundDeliveryDetail(OutboundDeliveryRow Delivery, string? Body, string Resource, IReadOnlyList<OutboundAttemptView> Attempts);

public sealed record OutboundDeliveryFilter(string? Type, string? Status, DateOnly? From, DateOnly? To);

public sealed record RotateOutboundSecretRequest(bool Immediate);

/// <summary>Leitura da inscrição pelo próprio aplicativo (CA 4): URL, tipos e situação. Nunca o segredo.</summary>
public sealed record IntegrationWebhookSubscriptionView(
    [property: JsonPropertyName("subscription_id")] Guid SubscriptionId,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("events")] IReadOnlyList<string> Events,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("contract")] int Contract);

/// <summary>
/// Configurações → Integrações → Eventos → "Enviados" e o resumo do Conecta (CA 11, 15, 18 e 19): <c>conecta.view</c> vê; "Reenviar" exige
/// <c>conecta.edit</c> e só vale para entrega <c>Delivered</c> ou <c>DeadLettered</c> com o conteúdo ainda guardado. Id de outro tenant não está
/// neste banco: 404.
/// </summary>
public sealed class OutboundWebhooksAdmin(
    BemAquiDbContext db,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IPermissionService permissions,
    OutboundDeliveryScheduler scheduler,
    TimeProvider clock)
{
    public const string EditPermission = "conecta.edit";

    private async Task<bool> CanEditAsync(CancellationToken cancellationToken) =>
        currentUser.UserId is { } id && (await permissions.GetAsync(id, cancellationToken)).Has(EditPermission);

    private Task<OutboundSubscription?> CurrentAsync(CancellationToken cancellationToken) =>
        db.OutboundSubscriptions.AsNoTracking().OrderBy(s => s.Status == OutboundSubscriptionStatus.Ended).ThenByDescending(s => s.CreatedAt).FirstOrDefaultAsync(cancellationToken);

    public async Task<OutboundWebhookSummary> SummaryAsync(CancellationToken cancellationToken = default)
    {
        var canEdit = await CanEditAsync(cancellationToken);
        var subscription = await CurrentAsync(cancellationToken);
        if (subscription is null || subscription.Status == OutboundSubscriptionStatus.Ended)
        {
            return new OutboundWebhookSummary(false, null, null, subscription?.Status.ToString(), true, null, 0, subscription?.LastSuccessAt, null, null, Options(null), canEdit);
        }

        var failing = await db.OutboundDeliveries.AsNoTracking()
            .Where(d => d.SubscriptionId == subscription.Id && (d.Status == OutboundDeliveryStatus.Retrying || d.Status == OutboundDeliveryStatus.DeadLettered))
            .GroupBy(_ => 1)
            .Select(g => new { Since = g.Min(d => d.CreatedAt), Dead = g.Count(d => d.Status == OutboundDeliveryStatus.DeadLettered) })
            .FirstOrDefaultAsync(cancellationToken);
        return new OutboundWebhookSummary(true, subscription.Id, subscription.Url, subscription.Status.ToString(), failing is null, failing?.Since, failing?.Dead ?? 0,
            subscription.LastSuccessAt, subscription.SecretRotatedAt, subscription.PreviousIfValid(clock.GetUtcNow()) is null ? null : subscription.PreviousValidUntil,
            Options(subscription), canEdit);
    }

    private static List<OutboundEventOption> Options(OutboundSubscription? subscription) =>
        OutboundWebhookCatalog.All.Where(t => t.Type != OutboundWebhookCatalog.Ping)
            .Select(t => new OutboundEventOption(t.Type, t.Label, subscription?.Subscribes(t.Type) == true)).ToList();

    public async Task<OutboundDeliveryList> ListAsync(OutboundDeliveryFilter filter, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var canEdit = await CanEditAsync(cancellationToken);
        var query = from d in db.OutboundDeliveries.AsNoTracking()
                    join e in db.OutboundEvents.AsNoTracking() on d.EventId equals e.Id
                    select new { d, e.Type, Purged = e.Data == null };
        var deadLettered = await query.CountAsync(x => x.d.Status == OutboundDeliveryStatus.DeadLettered, cancellationToken);
        if (!string.IsNullOrEmpty(filter.Type))
        {
            query = query.Where(x => x.Type == filter.Type);
        }

        if (Enum.TryParse<OutboundDeliveryStatus>(filter.Status, ignoreCase: false, out var status))
        {
            query = query.Where(x => x.d.Status == status);
        }

        if (filter.From is { } from)
        {
            var start = Time.BusinessTime.StartOfDayUtc(from);
            query = query.Where(x => x.d.CreatedAt >= start);
        }

        if (filter.To is { } to)
        {
            var end = Time.BusinessTime.StartOfDayUtc(to.AddDays(1));
            query = query.Where(x => x.d.CreatedAt < end);
        }

        var raw = await query.OrderByDescending(x => x.d.CreatedAt).ThenByDescending(x => x.d.Id).ToPagedListAsync(page, cancellationToken);
        var rows = raw.Items.Select(x => Row(x.d, x.Type, x.Purged, canEdit)).ToList();
        var types = OutboundWebhookCatalog.All.Select(t => new OutboundEventOption(t.Type, t.Label, false)).ToList();
        return new OutboundDeliveryList(new PagedList<OutboundDeliveryRow>(rows, raw.Page, raw.PageSize, raw.Total), types, deadLettered);
    }

    private static OutboundDeliveryRow Row(OutboundDelivery d, string type, bool purged, bool canEdit) =>
        new(d.Id, d.EventId, type, OutboundWebhookCatalog.Find(type)?.Label ?? type, d.CreatedAt, d.Status.ToString(), d.Attempts, d.LastStatusCode, d.LastError,
            d.Status is OutboundDeliveryStatus.Pending or OutboundDeliveryStatus.Retrying ? d.NextAttemptAt : null,
            canEdit && !purged && d.Status is OutboundDeliveryStatus.Delivered or OutboundDeliveryStatus.DeadLettered, purged);

    public async Task<Result<OutboundDeliveryDetail>> DetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var delivery = await db.OutboundDeliveries.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (delivery is null)
        {
            return NotFound();
        }

        var evt = await db.OutboundEvents.AsNoTracking().FirstAsync(e => e.Id == delivery.EventId, cancellationToken);
        var connectionId = await db.OutboundSubscriptions.AsNoTracking().Where(s => s.Id == delivery.SubscriptionId).Select(s => s.ConnectionId).FirstAsync(cancellationToken);
        var attempts = await db.OutboundAttempts.AsNoTracking().Where(a => a.DeliveryId == id).OrderBy(a => a.At).ThenBy(a => a.Number)
            .Select(a => new OutboundAttemptView(a.Number, a.At, a.StatusCode, a.DurationMs, a.Error)).ToListAsync(cancellationToken);
        var body = evt.Data is null
            ? null
            : System.Text.Encoding.UTF8.GetString(OutboundWebhookBody.Build(evt.Id, evt.Type, evt.OccurredAt, tenant.Tenant?.Key ?? string.Empty, connectionId, evt.Data, evt.Resource));
        return new OutboundDeliveryDetail(Row(delivery, evt.Type, evt.Data is null, await CanEditAsync(cancellationToken)), body, evt.Resource, attempts);
    }

    /// <summary>"Reenviar" (CA 11): nova série com o mesmo id e corpo, auditada.</summary>
    public async Task<Result> ResendAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var delivery = await db.OutboundDeliveries.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (delivery is null)
        {
            return NotFound();
        }

        if (delivery.Status is not (OutboundDeliveryStatus.Delivered or OutboundDeliveryStatus.DeadLettered))
        {
            return Error.Conflict("outbound_delivery_not_final", "Só o aviso entregue ou parado pode ser reenviado.");
        }

        if (await db.OutboundEvents.AsNoTracking().AnyAsync(e => e.Id == delivery.EventId && e.Data == null, cancellationToken))
        {
            return Error.Conflict("outbound_delivery_purged", "O conteúdo foi apagado depois de 90 dias");
        }

        if (await db.OutboundSubscriptions.AsNoTracking().AnyAsync(s => s.Id == delivery.SubscriptionId && s.Status != OutboundSubscriptionStatus.Active, cancellationToken))
        {
            return Error.Conflict("outbound_subscription_inactive", "A conexão com o Conecta não está ativa.");
        }

        var now = clock.GetUtcNow();
        db.AuditEntries.Add(new AuditEntry
        {
            Entity = "OutboundDelivery", EntityId = delivery.Id.ToString(), Action = AuditActions.Updated, UserId = currentUser.UserId, At = now,
            Changes = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["status"] = new { old = delivery.Status.ToString(), @new = nameof(OutboundDeliveryStatus.Pending) },
                ["attempts"] = new { old = delivery.Attempts, @new = 0 },
            }),
        });
        delivery.Status = OutboundDeliveryStatus.Pending;
        delivery.Attempts = 0;
        delivery.LastError = null;
        delivery.LastStatusCode = null;
        delivery.DeliveredAt = null;
        delivery.NextAttemptAt = now;
        delivery.LastQueuedAt = now;
        await scheduler.EnqueueAfterCommitAsync([delivery.Id], cancellationToken);
        await db.SaveChangesAsync(cancellationToken); // xmin: dois "Reenviar" ao mesmo tempo, o segundo dá 409
        return Result.Success();
    }

    /// <summary>A inscrição da própria conexão do aplicativo (CA 4).</summary>
    public async Task<IntegrationWebhookSubscriptionView?> ForConnectionAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        var s = await db.OutboundSubscriptions.AsNoTracking().FirstOrDefaultAsync(x => x.ConnectionId == connectionId, cancellationToken);
        return s is null ? null : new IntegrationWebhookSubscriptionView(s.Id, s.Url, s.Events, s.Status.ToString(), OutboundWebhookCatalog.Contract);
    }

    private static Error NotFound() => Error.NotFound(Error.Codes.NotFound, "Aviso não encontrado.");
}

/// <summary>Rotas dos avisos de saída (INT-02): as de pessoa no módulo <c>conecta</c> e a da própria conexão em <c>/api/v1/integration</c>.</summary>
public static class OutboundWebhookEndpoints
{
    public static IEndpointRouteBuilder MapOutboundWebhookEndpoints(this IEndpointRouteBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var group = api.MapGroup("/outbound-webhooks").RequireModule(ConnectedAppEndpoints.ModuleCode).WithTags("Avisos de saída");
        group.MapGet("/summary", async (OutboundWebhooksAdmin admin, CancellationToken ct) => Results.Ok(await admin.SummaryAsync(ct)))
            .RequirePermission("conecta.view").WithName("GetOutboundWebhookSummary").Produces<OutboundWebhookSummary>();
        group.MapGet("/deliveries", async (string? type, string? status, DateOnly? from, DateOnly? to, int? page, int? pageSize, OutboundWebhooksAdmin admin, CancellationToken ct) =>
                Results.Ok(await admin.ListAsync(new OutboundDeliveryFilter(type, status, from, to), new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize), ct)))
            .RequirePermission("conecta.view").WithName("ListOutboundDeliveries").Produces<OutboundDeliveryList>();
        group.MapGet("/deliveries/{id:guid}", async (Guid id, OutboundWebhooksAdmin admin, CancellationToken ct) => (await admin.DetailAsync(id, ct)).ToHttpResult())
            .RequirePermission("conecta.view").WithName("GetOutboundDelivery").Produces<OutboundDeliveryDetail>();
        group.MapPost("/deliveries/{id:guid}/resend", async (Guid id, OutboundWebhooksAdmin admin, CancellationToken ct) => (await admin.ResendAsync(id, ct)).ToHttpResult())
            .RequirePermission("conecta.edit").WithName("ResendOutboundDelivery");
        group.MapPut("/subscriptions/{id:guid}", async (Guid id, OutboundSubscriptionInput input, OutboundSubscriptionService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("conecta.edit").WithName("UpdateOutboundSubscription");
        group.MapPost("/subscriptions/{id:guid}/test", async (Guid id, OutboundSubscriptionService service, CancellationToken ct) =>
                (await service.TestAsync(id, ct)).ToHttpResult(deliveryId => Results.Ok(new OutboundTestResult(deliveryId))))
            .RequirePermission("conecta.edit").WithName("TestOutboundSubscription").Produces<OutboundTestResult>();
        group.MapPost("/subscriptions/{id:guid}/secret/rotate", async (Guid id, RotateOutboundSecretRequest request, OutboundSubscriptionService service, CancellationToken ct) =>
                (await service.RotateAsync(id, request?.Immediate == true, ct)).ToHttpResult())
            .RequirePermission("conecta.edit").WithName("RotateOutboundSecret").Produces<OutboundSecretRotation>();

        var integration = api.MapGroup("/integration/webhooks").WithTags("Integração (aplicativos)").RequireRateLimiting(ConnectedAppEndpoints.RateLimitPolicy);
        integration.MapGet("/subscription", async (HttpContext http, OutboundWebhooksAdmin admin, CancellationToken ct) =>
                AppClaims.ConnectionOf(http.User) is { } connection && await admin.ForConnectionAsync(connection, ct) is { } view
                    ? Results.Ok(view)
                    : Results.Problem(detail: "A conexão não tem inscrição de avisos.", statusCode: StatusCodes.Status404NotFound, title: "Não encontrado",
                        extensions: new Dictionary<string, object?> { ["code"] = Error.Codes.NotFound }))
            .RequireConnectedAppSelf().WithName("GetIntegrationWebhookSubscription").Produces<IntegrationWebhookSubscriptionView>();
        return api;
    }
}

public sealed record OutboundTestResult(Guid DeliveryId);
