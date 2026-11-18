using System.Security.Cryptography;
using System.Text.Encodings.Web;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace BemAqui.Host.Security;

/// <summary>
/// Esquema <c>Device</c> (PDV-01 CA 9 e 10, ADR 0048 §2): o aparelho de caixa se apresenta com o cookie <c>__Host-bq_pos</c> e assina a requisição
/// com a chave ECDSA não exportável dele (<see cref="DeviceSignatureHeaders"/>). O principal tem só o tenant, o aparelho, a loja, o caixa e a época,
/// tirados do banco do tenant do host; nunca <c>sub</c> nem permissão. Só os endpoints com <c>RequireDevice()</c> (policy <c>device</c>), todos em
/// <c>/api/v1/pos/sync/*</c>, o usam. Nada do segredo, da assinatura ou do corpo vai para o log.
/// </summary>
public sealed class DeviceAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>Corpo assinado de até 4 MB (o lote de eventos da OFF-02 cabe com folga).</summary>
    public const long MaxSignedBody = 4 * 1024 * 1024;

    public const string StatusItem = "bq.device.status";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var tenant = Context.RequestServices.GetRequiredService<TenantContextHolder>().Tenant;
        var cookie = Request.Cookies[PosDeviceCookie.Name];
        var timestamp = Request.Headers[DeviceSignatureHeaders.Timestamp].ToString();
        var signature = Request.Headers[DeviceSignatureHeaders.Signature].ToString();
        if (tenant is null || string.IsNullOrEmpty(cookie) || string.IsNullOrEmpty(signature)
            || !long.TryParse(timestamp, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var ms))
        {
            return AuthenticateResult.NoResult();
        }

        // B5: relógio do aparelho fora da tolerância tem código próprio (a tela pede para acertar o relógio, não para registrar de novo).
        if (!FreshTimestamp(ms))
        {
            Context.Items[StatusItem] = ClockSkewStatus;
            return AuthenticateResult.Fail("Relógio do aparelho fora da tolerância.");
        }

        if (Request.ContentLength > MaxSignedBody)
        {
            Context.Items[StatusItem] = TooLargeStatus;
            return AuthenticateResult.Fail("Corpo grande demais para o aparelho.");
        }

        // B2: o aparelho é achado pelo segredo antes; o corpo só é lido (e o hash calculado) para um aparelho que existe.
        DeviceAuthResult result;
        try
        {
            result = await Context.RequestServices.GetRequiredService<IDeviceAuthenticator>().AuthenticateAsync(cookie,
                async ct => DeviceSignatureHeaders.Payload(Request.Method, Request.Path.Value + Request.QueryString.Value, timestamp, await BodyHashAsync(ct)),
                signature, Context.RequestAborted);
        }
        catch (IOException) when (!Context.RequestAborted.IsCancellationRequested)
        {
            // Corpo sem Content-Length (chunked) passando do limite do buffer: 413, não 500.
            Context.Items[StatusItem] = TooLargeStatus;
            return AuthenticateResult.Fail("Corpo grande demais para o aparelho.");
        }

        Context.Items[StatusItem] = result.Status;
        if (result.Status == DeviceAuthStatus.Invalid || result.Device is null)
        {
            return AuthenticateResult.Fail("Aparelho recusado.");
        }

        return AuthenticateResult.Success(new AuthenticationTicket(DevicePrincipal.Create(tenant.Id, result.Device), DeviceClaims.Scheme));
    }

    private const string ClockSkewStatus = "clock_skew";
    private const string TooLargeStatus = "too_large";

    /// <summary>401 com o motivo (a tela do <c>/pdv</c> pede novo registro, ou para acertar o relógio); 413 para corpo grande demais.</summary>
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.CacheControl = "no-store";
        var status = Context.Items.TryGetValue(StatusItem, out var value) ? value as string : null;
        var result = status switch
        {
            ClockSkewStatus => Results.Problem(detail: DeviceEndpoints.ClockSkewMessage, statusCode: StatusCodes.Status401Unauthorized, title: "Relógio do aparelho errado",
                extensions: new Dictionary<string, object?> { ["code"] = DeviceEndpoints.ClockSkewCode, ["serverTime"] = DeviceClock().GetUtcNow() }),
            TooLargeStatus => Results.Problem(detail: "O envio passou do limite de 4 MB. Envie em lotes menores.", statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "Envio grande demais", extensions: new Dictionary<string, object?> { ["code"] = "payload_too_large" }),
            _ => Results.Problem(detail: DeviceEndpoints.InvalidMessage, statusCode: StatusCodes.Status401Unauthorized, title: "Aparelho não reconhecido",
                extensions: new Dictionary<string, object?> { ["code"] = DeviceEndpoints.InvalidCode }),
        };
        await result.ExecuteAsync(Context);
    }

    private TimeProvider DeviceClock() => Context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System;

    private bool FreshTimestamp(long ms)
    {
        var at = DateTimeOffset.FromUnixTimeMilliseconds(Math.Clamp(ms, 0, DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()));
        return (DeviceClock().GetUtcNow() - at).Duration() <= DeviceSignatureHeaders.ClockSkew;
    }

    private async Task<string> BodyHashAsync(CancellationToken cancellationToken)
    {
        Request.EnableBuffering(MaxSignedBody);
        Request.Body.Position = 0;
        var hash = await SHA256.HashDataAsync(Request.Body, cancellationToken);
        Request.Body.Position = 0;
        return Convert.ToBase64String(hash);
    }
}

/// <summary>Policy <c>device</c>: principal de aparelho do tenant do host e módulo <c>pos</c> liberado (camada 1).</summary>
public sealed record DeviceRequirement : IAuthorizationRequirement;

public sealed class DeviceRequirementHandler : AuthorizationHandler<DeviceRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, DeviceRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Resource is not HttpContext http)
        {
            return Task.CompletedTask;
        }

        var resolved = http.RequestServices.GetRequiredService<TenantContextHolder>().Resolved;
        if (resolved?.IsReleased("pos") != true)
        {
            context.Fail(new AuthorizationFailureReason(this, PermissionHandler.NotReleasedReason + ":pos"));
            return Task.CompletedTask;
        }

        var user = context.User;
        if (user.Identity?.AuthenticationType == DeviceClaims.Scheme
            && Guid.TryParse(user.FindFirst(DeviceClaims.Tenant)?.Value, out var tenantId) && tenantId == resolved.Tenant.Id
            && user.FindFirst(DeviceClaims.DeviceId) is not null && user.FindFirst("sub") is null)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public static class DeviceAuthentication
{
    /// <summary>Esquema e policy do aparelho de PDV (além do JWT do usuário, que continua o esquema padrão).</summary>
    public static IServiceCollection AddDeviceAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, DeviceAuthenticationHandler>(DeviceClaims.Scheme, null);
        services.AddAuthorizationBuilder().AddPolicy(DeviceClaims.Policy, p => p
            .AddAuthenticationSchemes(DeviceClaims.Scheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new DeviceRequirement()));
        services.AddScoped<IAuthorizationHandler, DeviceRequirementHandler>();
        return services;
    }
}
