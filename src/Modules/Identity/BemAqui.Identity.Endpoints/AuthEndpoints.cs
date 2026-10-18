using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Identity.Application;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Identity.Endpoints;

public sealed record LoginRequest(string Email, string Password);

public sealed record AccessTokenResponse(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>Login, renovação e saída (rotas anônimas do CLAUDE.md). O refresh só existe no cookie HttpOnly.</summary>
public static class AuthEndpoints
{
    /// <summary>Prefixo __Host-: Secure, Path=/ e sem Domain, então nenhum subdomínio irmão consegue sobrescrever.</summary>
    public const string RefreshCookie = "__Host-bq_rt";
    public const string CsrfHeader = "X-CSRF";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Autenticação").AllowAnonymous();

        auth.MapPost("/login", async (LoginRequest request, HttpContext http, AuthService service, CancellationToken ct) =>
            (await service.LoginAsync(request.Email, request.Password, http.Request.Host.Host, Ip(http), ct))
                .ToHttpResult(session => Issue(http, session)))
            .WithName("Login");

        auth.MapPost("/refresh", async (HttpContext http, AuthService service, TokenSigning signing, CancellationToken ct) =>
            {
                if (!SameOriginWithCsrfHeader(http))
                {
                    return CsrfRejected();
                }

                var result = await service.RefreshAsync(http.Request.Cookies[RefreshCookie], http.Request.Host.Host, Ip(http), ct);
                if (result.IsFailure)
                {
                    http.Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTimeOffset.UnixEpoch));
                }

                return result.ToHttpResult(session => Issue(http, session));
            })
            .WithName("RefreshSession");

        auth.MapPost("/logout", async (HttpContext http, AuthService service, CancellationToken ct) =>
            {
                if (!SameOriginWithCsrfHeader(http))
                {
                    return CsrfRejected();
                }

                await service.LogoutAsync(http.Request.Cookies[RefreshCookie], Ip(http), ct);
                http.Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTimeOffset.UnixEpoch));
                return Results.NoContent();
            })
            .WithName("Logout");

        auth.MapAccountEndpoints();

        return api;
    }

    /// <summary>Refresh e logout exigem Origin do próprio host e o cabeçalho X-CSRF (FND-04 CA 7).</summary>
    public static bool SameOriginWithCsrfHeader(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (http.Request.Headers[CsrfHeader] != "1")
        {
            return false;
        }

        var origin = http.Request.Headers.Origin.ToString();
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && string.Equals(uri.Host, http.Request.Host.Host, StringComparison.OrdinalIgnoreCase);
    }

    internal static IResult Issue(HttpContext http, LoginSession session)
    {
        http.Response.Cookies.Append(RefreshCookie, session.RefreshToken, CookieOptions(session.RefreshTokenExpiresAt));
        http.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new AccessTokenResponse(session.AccessToken, session.AccessTokenExpiresAt));
    }

    private static CookieOptions CookieOptions(DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expires,
        IsEssential = true,
    };

    private static IResult CsrfRejected() =>
        Error.Forbidden(Error.Codes.Forbidden, "Requisição recusada: origem não confirmada.").ToProblem();

    internal static string? Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();
}
