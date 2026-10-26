using System.Diagnostics;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace BemAqui.BuildingBlocks.Webhooks;

public sealed record RotateWebhookRequest(string? Code);

/// <summary>
/// Rota pública dos webhooks (FND-16, ADR 0031 §1) e telas de Configurações → Integrações. A rota pública fica fora de
/// <c>/api/v1</c> e fora do OpenAPI, sem usuário, sem cookie, sem CSRF; o tenant vem só do <c>tenantKey</c> opaco.
/// </summary>
public static partial class WebhookEndpoints
{
    public const string RoutePattern = "/webhooks/{provider}/{tenantKey}/{pathToken?}";
    public const string PathPrefix = "/webhooks";
    public const string RateLimitPolicy = "webhooks";

    /// <summary>
    /// <c>POST</c> (recepção) e <c>GET</c> (verificação, só para quem declara); outro método responde 405 pelo roteamento. A ordem
    /// das recusas segue o CA 8: 429 (rate limit, antes do endpoint), 415, 413 (antes de ler o corpo inteiro), e só então o banco
    /// do tenant. Toda recusa de origem é a mesma: 401 sem corpo.
    /// </summary>
    public static IEndpointRouteBuilder MapWebhookReceiver(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapPost(RoutePattern, ReceiveAsync)
            .AllowAnonymous().DisableAntiforgery().ExcludeFromDescription()
            .RequireRateLimiting(RateLimitPolicy)
            .WithMetadata(new RequestSizeLimitAttribute(WebhookOptions.MaxBodyBytes))
            .WithName("ReceiveWebhook");
        app.MapGet(RoutePattern, VerifyAsync)
            .AllowAnonymous().ExcludeFromDescription()
            .RequireRateLimiting(RateLimitPolicy)
            .WithName("VerifyWebhook");
        return app;
    }

    /// <summary>
    /// Configurações → Integrações (CA 7, 15 e 16): o módulo <c>settings</c> liberado e <c>settings.view</c>; o serviço limita aos
    /// provedores do módulo dono que a pessoa vê. Rotação com <c>settings.edit</c> e 2FA de novo.
    /// </summary>
    public static IEndpointRouteBuilder MapWebhookSettingsEndpoints(this IEndpointRouteBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var group = api.MapGroup("/settings/integrations").RequireModule("settings").WithTags("Configurações");

        group.MapGet("/events", async (string? provider, string? status, DateOnly? from, DateOnly? to, int? page, int? pageSize, WebhookEventsAdmin admin, CancellationToken ct) =>
                (await admin.ListAsync(new WebhookEventFilter(provider, status, from, to), new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize), ct)).ToHttpResult())
            .RequirePermission("settings.view").WithName("ListWebhookEvents").Produces<WebhookEventList>();

        group.MapGet("/events/{id:guid}/content", async (Guid id, WebhookEventsAdmin admin, HttpContext http, CancellationToken ct) =>
                (await admin.ContentAsync(id, ct)).ToHttpResult(content =>
                {
                    http.Response.Headers.CacheControl = "no-store";
                    return Results.Ok(content);
                }))
            .RequirePermission("settings.view").WithName("GetWebhookEventContent").Produces<WebhookEventContent>();

        group.MapPost("/events/{id:guid}/reprocess", async (Guid id, WebhookEventsAdmin admin, CancellationToken ct) =>
                (await admin.ReprocessAsync(id, ct)).ToHttpResult())
            .RequirePermission("settings.view").WithName("ReprocessWebhookEvent");

        group.MapGet("/webhooks", async (WebhookCredentialService service, CancellationToken ct) => Results.Ok(await service.StatusAsync(ct)))
            .RequirePermission("settings.view").WithName("ListWebhookCredentials").Produces<IReadOnlyList<WebhookCredentialStatus>>();

        group.MapPost("/webhooks/{provider}/rotate", async (string provider, RotateWebhookRequest request, WebhookCredentialService service, HttpContext http, CancellationToken ct) =>
                (await service.RotateAsync(provider, request?.Code, ct)).ToHttpResult(rotation =>
                {
                    http.Response.Headers.CacheControl = "no-store";
                    return Results.Ok(rotation);
                }))
            .RequirePermission("settings.edit").WithName("RotateWebhookSecret").Produces<WebhookRotation>();

        return api;
    }

    private static async Task ReceiveAsync(HttpContext http, string provider, string tenantKey, string? pathToken)
    {
        var services = http.RequestServices;
        var clock = services.GetRequiredService<TimeProvider>();
        var started = clock.GetTimestamp();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(WebhookEndpoints).FullName!);
        PrepareResponse(http);

        var registry = services.GetRequiredService<WebhookProviderRegistry>();
        if (!registry.TryGet(provider, out var definition))
        {
            await RejectAsync(http, logger, null, null, started, clock);
            return;
        }

        if (!MediaTypeMatches(http.Request.ContentType, definition.ContentType))
        {
            http.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }

        if (http.Request.ContentLength > WebhookOptions.MaxBodyBytes)
        {
            http.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        var resolved = await ResolveTenantAsync(http, definition);
        if (resolved is null)
        {
            await RejectAsync(http, logger, definition, null, started, clock);
            return;
        }

        if (resolved.SchemaOutdated)
        {
            http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable; // o provedor reentrega depois do migrate
            return;
        }

        var body = await ReadLimitedAsync(http, WebhookOptions.MaxBodyBytes);
        if (body is null)
        {
            http.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        var request = new WebhookRequest(body, Headers(http.Request), QueryToken(http.Request, out var badQuery), pathToken);
        if (badQuery)
        {
            await RejectAsync(http, logger, definition, resolved.Tenant, started, clock);
            return;
        }

        WebhookReceiveOutcome outcome;
        try
        {
            await using var scope = await services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(resolved.Tenant.Tenant, http.RequestAborted);
            outcome = await scope.ServiceProvider.GetRequiredService<WebhookReceiver>().ReceiveAsync(definition, request, http.RequestAborted);
        }
        catch (Exception ex) when (ex is DbUpdateException or Npgsql.NpgsqlException or InvalidOperationException or TimeoutException)
        {
            // Nada fica gravado pela metade (uma transação só); o provedor reentrega (CA 9).
            var ms1 = clock.GetElapsedTime(started).TotalMilliseconds;
            LogUnavailable(logger, definition.Code, resolved.Tenant.Tenant.Id, ex.GetType().Name, ms1);
            http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        switch (outcome.Result)
        {
            case WebhookReceiveResult.Rejected:
                await RejectAsync(http, logger, definition, resolved.Tenant, started, clock);
                return;
            case WebhookReceiveResult.Duplicate:
                http.Response.StatusCode = StatusCodes.Status200OK;
                break;
            default:
                http.Response.StatusCode = StatusCodes.Status202Accepted;
                break;
        }

        var ms2 = clock.GetElapsedTime(started).TotalMilliseconds;
        LogReceived(logger, definition.Code, resolved.Tenant.Tenant.Id, outcome.EventId, outcome.EventType ?? "-", outcome.Result, ms2);
    }

    /// <summary>
    /// GET de verificação (CA 1, padrão da Meta): só para o provedor que declara; <c>hub.verify_token</c> comparado em tempo constante
    /// com o hash guardado; devolve o <c>hub.challenge</c> em texto puro e não grava evento. Qualquer falha: 403 sem corpo.
    /// </summary>
    private static async Task VerifyAsync(HttpContext http, string provider, string tenantKey, string? pathToken)
    {
        var services = http.RequestServices;
        var clock = services.GetRequiredService<TimeProvider>();
        var started = clock.GetTimestamp();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(WebhookEndpoints).FullName!);
        PrepareResponse(http);
        var registry = services.GetRequiredService<WebhookProviderRegistry>();
        var known = registry.TryGet(provider, out var definition) ? definition : null;
        var resolved = known is { SupportsVerificationGet: true } && pathToken is null ? await ResolveTenantAsync(http, known) : null;
        var challenge = http.Request.Query["hub.challenge"].ToString();
        if (known is null || resolved is null || resolved.SchemaOutdated
            || !string.Equals(http.Request.Query["hub.mode"].ToString(), "subscribe", StringComparison.Ordinal)
            || challenge.Length is 0 or > 256 || challenge.Any(char.IsControl))
        {
            await services.GetRequiredService<WebhookRejectionMonitor>().RecordAsync(known?.Code, resolved?.Tenant.Tenant.Id, resolved?.Tenant.Tenant.Key);
            var ms3 = clock.GetElapsedTime(started).TotalMilliseconds;
            LogRejected(logger, known?.Code ?? "-", resolved?.Tenant.Tenant.Id, ms3);
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        bool valid;
        await using (var scope = await services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(resolved.Tenant.Tenant, http.RequestAborted))
        {
            var db = scope.ServiceProvider.GetRequiredService<BemAquiDbContext>();
            var credentials = await db.WebhookCredentials.AsNoTracking().Where(c => c.Provider == known.Code && c.Kind == WebhookCredentialKind.UrlToken).ToListAsync(http.RequestAborted);
            var now = clock.GetUtcNow();
            var token = http.Request.Query["hub.verify_token"].ToString();
            valid = false;
            foreach (var credential in credentials)
            {
                var keys = new WebhookKeys(Hex(credential.TokenHash), Hex(credential.PreviousTokenHash), credential.PreviousValidUntil);
                valid |= UrlTokenValidator.Compare(token, keys, now) != WebhookOriginMatch.Invalid;
            }
        }

        if (!valid)
        {
            await services.GetRequiredService<WebhookRejectionMonitor>().RecordAsync(known.Code, resolved.Tenant.Tenant.Id, resolved.Tenant.Tenant.Key);
            var ms4 = clock.GetElapsedTime(started).TotalMilliseconds;
            LogRejected(logger, known.Code, resolved.Tenant.Tenant.Id, ms4);
            http.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        http.Response.StatusCode = StatusCodes.Status200OK;
        http.Response.ContentType = "text/plain; charset=utf-8";
        await http.Response.WriteAsync(challenge, http.RequestAborted);
    }

    private sealed record ResolvedWebhookTenant(ResolvedTenant Tenant, bool SchemaOutdated);

    /// <summary>Tenant só pelo <c>tenantKey</c> (nunca pelo host) e só se <c>Active</c>; o host interno só para quem o declara.</summary>
    private static async Task<ResolvedWebhookTenant?> ResolveTenantAsync(HttpContext http, WebhookProvider provider)
    {
        var services = http.RequestServices;
        var options = services.GetRequiredService<IOptions<WebhookOptions>>().Value;
        if (IsInternalHost(http, options) && !provider.AllowInternalHost)
        {
            return null;
        }

        var tenantKey = http.Request.RouteValues["tenantKey"] as string ?? string.Empty;
        var tenant = await services.GetRequiredService<IWebhookTenantKeys>().ResolveAsync(tenantKey, http.RequestAborted);
        if (tenant is null || tenant.Tenant.Status != TenantStatus.Active)
        {
            return null;
        }

        var expected = services.GetService<ITenantSchemaVersion>()?.Expected;
        var outdated = expected is not null && string.CompareOrdinal(tenant.SchemaVersion ?? string.Empty, expected) < 0;
        return new ResolvedWebhookTenant(tenant, outdated);
    }

    public static bool IsInternalHost(HttpContext http, WebhookOptions options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        var host = http.Request.Host.Host;
        return options.InternalHosts.Any(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task RejectAsync(HttpContext http, ILogger logger, WebhookProvider? provider, ResolvedTenant? tenant, long started, TimeProvider clock)
    {
        await http.RequestServices.GetRequiredService<WebhookRejectionMonitor>().RecordAsync(provider?.Code, tenant?.Tenant.Id, tenant?.Tenant.Key);
        var ms5 = clock.GetElapsedTime(started).TotalMilliseconds;
        LogRejected(logger, provider?.Code ?? "-", tenant?.Tenant.Id, ms5);
        http.Response.StatusCode = StatusCodes.Status401Unauthorized; // mesma resposta para chave, provedor, tenant ou assinatura (CA 3)
    }

    /// <summary>Resposta sem corpo e sem cache; a página de status padrão não acrescenta nada (CA 3).</summary>
    private static void PrepareResponse(HttpContext http)
    {
        if (http.Features.Get<IStatusCodePagesFeature>() is { } pages)
        {
            pages.Enabled = false;
        }

        http.Response.Headers.CacheControl = "no-store";
    }

    internal static bool MediaTypeMatches(string? contentType, string expected) =>
        MediaTypeHeaderValue.TryParse(contentType, out var parsed)
        && string.Equals(parsed.MediaType.Value, expected, StringComparison.OrdinalIgnoreCase);

    /// <summary>Lê o corpo com teto: passou de <paramref name="max"/>, para na hora e devolve nulo (413).</summary>
    private static async Task<byte[]?> ReadLimitedAsync(HttpContext http, int max)
    {
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
        {
            limit.MaxRequestBodySize = max;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        try
        {
            int read;
            while ((read = await http.Request.Body.ReadAsync(chunk, http.RequestAborted)) > 0)
            {
                if (buffer.Length + read > max)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return null;
        }

        return buffer.ToArray();
    }

    private static Dictionary<string, string> Headers(HttpRequest request) =>
        request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);

    /// <summary>O token da query (<c>?t=</c>); repetido é recusado.</summary>
    private static string? QueryToken(HttpRequest request, out bool invalid)
    {
        var values = request.Query[UrlTokenValidator.QueryParameter];
        invalid = values.Count > 1;
        return values.Count == 1 ? values[0] : null;
    }

    private static byte[]? Hex(string? value) => string.IsNullOrEmpty(value) ? null : Convert.FromHexString(value);

    [LoggerMessage(Level = LogLevel.Information, Message = "Webhook {Provider} do tenant {TenantId}: evento {EventId} ({EventType}) {Result} em {Elapsed:0.0} ms")]
    private static partial void LogReceived(ILogger logger, string provider, Guid tenantId, Guid? eventId, string eventType, WebhookReceiveResult result, double elapsed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Webhook {Provider} do tenant {TenantId}: recusado em {Elapsed:0.0} ms")]
    private static partial void LogRejected(ILogger logger, string provider, Guid? tenantId, double elapsed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Webhook {Provider} do tenant {TenantId}: banco indisponível ({Error}), 503 em {Elapsed:0.0} ms")]
    private static partial void LogUnavailable(ILogger logger, string provider, Guid tenantId, string error, double elapsed);
}

/// <summary>Versão de schema que o build espera (o Host informa), para o webhook responder 503 antes do migrate (FND-03a CA 12).</summary>
public interface ITenantSchemaVersion
{
    string Expected { get; }
}
