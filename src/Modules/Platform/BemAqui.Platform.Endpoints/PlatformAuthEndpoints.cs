using System.Security.Claims;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Platform.Application.Auth;
using BemAqui.Platform.Domain;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Platform.Endpoints;

public sealed record PlatformLoginRequest(string Email, string Password);

public sealed record PlatformChallengeResponse(string Challenge, string Purpose, DateTimeOffset ExpiresAt);

public sealed record PlatformSetPasswordRequest(string Token, string Password);

public sealed record PlatformChallengeRequest(string Challenge);

public sealed record PlatformConfirmRequest(string Challenge, string Code);

public sealed record PlatformVerifyRequest(string Challenge, string? Code, string? RecoveryCode);

public sealed record PlatformRecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);

public sealed record PlatformCurrentAdmin(string AdminId);

/// <summary>
/// Login do super admin em <c>/api/v1/platform</c> (FND-13). Essas rotas só existem no host da plataforma (o
/// roteamento por host devolve 404 nos hosts de tenant). Todo POST exige Origin do próprio host e <c>X-CSRF: 1</c>,
/// porque a sessão vai num cookie.
/// </summary>
internal static class PlatformAuthEndpoints
{
    public const string CsrfHeader = "X-CSRF";

    public static IEndpointRouteBuilder MapPlatformAuthEndpoints(this IEndpointRouteBuilder api)
    {
        var platform = api.MapGroup("/platform").WithTags("Plataforma").AddEndpointFilter(RequireSameOriginOnPost);
        var auth = platform.MapGroup("/auth").AllowAnonymous();

        auth.MapPost("/login", async (PlatformLoginRequest request, HttpContext http, PlatformAuthService service, CancellationToken ct) =>
                (await service.LoginAsync(request.Email, request.Password, Ip(http), ct))
                    .ToHttpResult(c => Results.Ok(new PlatformChallengeResponse(c.Challenge, c.Purpose.ToString().ToLowerInvariant(), c.ExpiresAt))))
            .WithName("PlatformLogin").Produces<PlatformChallengeResponse>();

        auth.MapPost("/set-password", async (PlatformSetPasswordRequest request, HttpContext http, PlatformAuthService service, CancellationToken ct) =>
                (await service.SetPasswordAsync(request.Token, request.Password, Ip(http), ct)).ToHttpResult())
            .WithName("PlatformSetPassword");

        auth.MapPost("/2fa/setup", async (PlatformChallengeRequest request, PlatformAuthService service, CancellationToken ct) =>
                (await service.BeginSetupAsync(request.Challenge, ct)).ToHttpResult())
            .WithName("PlatformBeginTwoFactorSetup").Produces<PlatformTotpSetup>();

        auth.MapPost("/2fa/confirm", async (PlatformConfirmRequest request, HttpContext http, PlatformAuthService service, CancellationToken ct) =>
                (await service.ConfirmSetupAsync(request.Challenge, request.Code, Host(http), Ip(http), ct))
                    .ToHttpResult(activated =>
                    {
                        SetSession(http, activated.Session);
                        return Results.Ok(new PlatformRecoveryCodesResponse(activated.RecoveryCodes));
                    }))
            .WithName("PlatformConfirmTwoFactorSetup").Produces<PlatformRecoveryCodesResponse>();

        auth.MapPost("/2fa/verify", async (PlatformVerifyRequest request, HttpContext http, PlatformAuthService service, CancellationToken ct) =>
                (await service.VerifyAsync(request.Challenge, request.Code, request.RecoveryCode, Host(http), Ip(http), ct))
                    .ToHttpResult(session =>
                    {
                        SetSession(http, session);
                        return Results.NoContent();
                    }))
            .WithName("PlatformVerifyTwoFactor");

        auth.MapPost("/logout", async (HttpContext http, PlatformAuthService service, CancellationToken ct) =>
            {
                await service.LogoutAsync(http.Request.Cookies[PlatformSessionAuthenticationHandler.Cookie], Ip(http), ct);
                http.Response.Cookies.Delete(PlatformSessionAuthenticationHandler.Cookie, CookieOptions());
                return Results.NoContent();
            })
            .WithName("PlatformLogout");

        platform.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new PlatformCurrentAdmin(user.FindFirstValue("sub")!)))
            .RequireAuthorization(PlatformSessionAuthenticationHandler.Policy)
            .WithName("GetPlatformCurrentAdmin").Produces<PlatformCurrentAdmin>();

        return api;
    }

    internal static async ValueTask<object?> RequireSameOriginOnPost(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method))
        {
            return await next(context);
        }

        var origin = http.Request.Headers.Origin.ToString();
        var sameOrigin = Uri.TryCreate(origin, UriKind.Absolute, out var uri) && string.Equals(uri.Host, http.Request.Host.Host, StringComparison.OrdinalIgnoreCase);
        return sameOrigin && http.Request.Headers[CsrfHeader] == "1"
            ? await next(context)
            : Error.Forbidden(Error.Codes.Forbidden, "Requisição recusada: origem não confirmada.").ToProblem();
    }

    private static void SetSession(HttpContext http, PlatformSessionIssued session)
    {
        http.Response.Cookies.Append(PlatformSessionAuthenticationHandler.Cookie, session.Token, CookieOptions());
        http.Response.Headers.CacheControl = "no-store";
    }

    /// <summary>Cookie de sessão (sem Expires): a inatividade e o teto são conferidos no servidor.</summary>
    private static CookieOptions CookieOptions() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        IsEssential = true,
    };

    private static string Host(HttpContext http) => TenantDomain.Normalize(http.Request.Host.Host);

    private static string? Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();
}
