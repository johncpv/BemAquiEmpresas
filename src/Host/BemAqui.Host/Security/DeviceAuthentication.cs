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
        if (tenant is null || string.IsNullOrEmpty(cookie) || string.IsNullOrEmpty(signature) || !FreshTimestamp(timestamp))
        {
            return AuthenticateResult.NoResult();
        }

        if (Request.ContentLength > MaxSignedBody)
        {
            return AuthenticateResult.Fail("Corpo grande demais para o aparelho.");
        }

        var payload = DeviceSignatureHeaders.Payload(Request.Method, Request.Path.Value + Request.QueryString.Value, timestamp, await BodyHashAsync());
        var result = await Context.RequestServices.GetRequiredService<IDeviceAuthenticator>().AuthenticateAsync(cookie, payload, signature, Context.RequestAborted);
        Context.Items[StatusItem] = result.Status;
        if (result.Status == DeviceAuthStatus.Invalid || result.Device is null)
        {
            return AuthenticateResult.Fail("Aparelho recusado.");
        }

        return AuthenticateResult.Success(new AuthenticationTicket(DevicePrincipal.Create(tenant.Id, result.Device), DeviceClaims.Scheme));
    }

    /// <summary>401 com o motivo: a tela do <c>/pdv</c> pede um novo registro.</summary>
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.CacheControl = "no-store";
        await Results.Problem(detail: DeviceEndpoints.InvalidMessage, statusCode: StatusCodes.Status401Unauthorized, title: "Aparelho não reconhecido",
                extensions: new Dictionary<string, object?> { ["code"] = DeviceEndpoints.InvalidCode })
            .ExecuteAsync(Context);
    }

    private bool FreshTimestamp(string value)
    {
        if (!long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var ms))
        {
            return false;
        }

        var now = (Context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
        var at = DateTimeOffset.FromUnixTimeMilliseconds(Math.Clamp(ms, 0, DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()));
        return (now - at).Duration() <= DeviceSignatureHeaders.ClockSkew;
    }

    private async Task<string> BodyHashAsync()
    {
        Request.EnableBuffering(MaxSignedBody);
        Request.Body.Position = 0;
        var hash = await SHA256.HashDataAsync(Request.Body, Context.RequestAborted);
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
