using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Identity.Application;
using BemAqui.Identity.Application.Shopfloor;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Identity.Endpoints;

/// <param name="Ephemeral">Login completo feito no tablet da fábrica (registrar aparelho, criar PIN): sem refresh (ADR 0026 §2).</param>
public sealed record LoginRequest(string Email, string Password, bool Ephemeral = false);

public sealed record AccessTokenResponse(string AccessToken, DateTimeOffset ExpiresAt);

public sealed record PinLoginRequest(Guid UserId, string Pin);

/// <summary>Entrada por PIN: access token de 15 min e o fim absoluto da sessão (12 h, sem renovação além disso).</summary>
public sealed record PinSessionResponse(string AccessToken, DateTimeOffset ExpiresAt, DateTimeOffset SessionExpiresAt);

/// <summary>Login, renovação e saída (rotas anônimas do CLAUDE.md). O refresh só existe no cookie HttpOnly.</summary>
public static class AuthEndpoints
{
    /// <summary>Prefixo __Host-: Secure, Path=/ e sem Domain, então nenhum subdomínio irmão consegue sobrescrever.</summary>
    public const string RefreshCookie = SessionCookie.Refresh;
    public const string CsrfHeader = "X-CSRF";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Autenticação").AllowAnonymous();

        auth.MapPost("/login", async (LoginRequest request, HttpContext http, AuthService service, CancellationToken ct) =>
            {
                // No tablet registrado (cookie do aparelho) o login completo nunca deixa sessão persistente (ADR 0026 §2).
                var persistent = !Ephemeral(http, request.Ephemeral);
                var result = await service.LoginAsync(request.Email, request.Password, http.Request.Host.Host, Ip(http), persistent, ct);
                if (!persistent && result.IsSuccess && result.Value.Session is not null)
                {
                    await service.LogoutAsync(http.Request.Cookies[RefreshCookie], Ip(http), ct); // a sessão anterior do tablet acaba aqui
                }

                return result.ToHttpResult(outcome => Results.Ok(TwoFactorEndpoints.Login(http, outcome)));
            })
            .WithName("Login").Produces<LoginResponse>();

        auth.MapPost("/refresh", async (HttpContext http, AuthService service, CancellationToken ct) =>
            {
                if (!SameOriginWithCsrfHeader(http))
                {
                    return CsrfRejected();
                }

                var result = await service.RefreshAsync(http.Request.Cookies[RefreshCookie], http.Request.Host.Host, Ip(http), http.Request.Cookies[ShopfloorDeviceCookie.Name], ct);
                if (result.IsFailure)
                {
                    http.Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTimeOffset.UnixEpoch));
                }

                return result.ToHttpResult(session => Issue(http, session));
            })
            .WithName("RefreshSession").Produces<AccessTokenResponse>();

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
            .AllowPinSession().WithName("Logout");

        MapPinLogin(auth);
        auth.MapAccountEndpoints();
        auth.MapTwoFactorChallengeEndpoints();

        return api;
    }

    /// <summary>
    /// Entrada por PIN (ADR 0026 §4, ADR 0009 adendo): anônimas só com o cookie do aparelho registrado válido; sem ele, 404 como rota
    /// inexistente. Depois do cookie: <c>Origin</c> do host e <c>X-CSRF: 1</c>, módulo <c>shopfloor</c> liberado. O rate limit é o do
    /// login (<c>/api/v1/auth/*</c>).
    /// </summary>
    private static void MapPinLogin(RouteGroupBuilder auth)
    {
        auth.MapGet("/pin/users", async (HttpContext http, PinService pins, CancellationToken ct) =>
            {
                if (await pins.UsersAsync(http.Request.Cookies[ShopfloorDeviceCookie.Name], ct) is not { } users)
                {
                    return Results.NotFound();
                }

                if (!SameSiteWithCsrfHeader(http))
                {
                    return CsrfRejected();
                }

                http.Response.Headers.CacheControl = "no-store";
                return ModuleEndpoints.IsReleased(http, "shopfloor") ? Results.Ok(users) : ModuleEndpoints.NotReleased("shopfloor");
            })
            .WithName("ListPinUsers").Produces<PinUsers>();

        auth.MapPost("/pin", async (PinLoginRequest request, HttpContext http, ShopfloorDeviceGate gate, PinService pins, CancellationToken ct) =>
            {
                if (await gate.ResolveAsync(http.Request.Cookies[ShopfloorDeviceCookie.Name], ct) is not { } device)
                {
                    return Results.NotFound();
                }

                if (!SameOriginWithCsrfHeader(http))
                {
                    return CsrfRejected();
                }

                if (!ModuleEndpoints.IsReleased(http, "shopfloor"))
                {
                    return ModuleEndpoints.NotReleased("shopfloor");
                }

                var result = await pins.LoginAsync(device, request.UserId, request.Pin, http.Request.Host.Host, Ip(http), ct);
                return result.ToHttpResult(login =>
                {
                    SetSession(http, login.Session);
                    SetDeviceCookie(http, login.DeviceSecret, login.DeviceExpiresAt);
                    return Results.Ok(new PinSessionResponse(login.Session.AccessToken, login.Session.AccessTokenExpiresAt, login.Session.SessionExpiresAt ?? login.Session.AccessTokenExpiresAt));
                });
            })
            .WithName("LoginWithPin").Produces<PinSessionResponse>();
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

    /// <summary>
    /// GET da lista do PIN: o navegador não manda <c>Origin</c> num GET da mesma origem, então vale o <c>X-CSRF: 1</c> (cabeçalho que
    /// outra origem só manda com preflight, que não liberamos), com <c>Origin</c> igual ao host e <c>Sec-Fetch-Site: same-origin</c>
    /// quando vierem.
    /// </summary>
    public static bool SameSiteWithCsrfHeader(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (http.Request.Headers[CsrfHeader] != "1")
        {
            return false;
        }

        var site = http.Request.Headers["Sec-Fetch-Site"].ToString();
        if (site.Length > 0 && site != "same-origin")
        {
            return false;
        }

        var origin = http.Request.Headers.Origin.ToString();
        return origin.Length == 0 || SameOriginWithCsrfHeader(http);
    }

    /// <summary>Login no tablet: pedido pela tela ou o navegador já tem o cookie do aparelho registrado.</summary>
    internal static bool Ephemeral(HttpContext http, bool requested) => requested || http.Request.Cookies.ContainsKey(ShopfloorDeviceCookie.Name);

    internal static IResult Issue(HttpContext http, LoginSession session)
    {
        SetSession(http, session);
        return Results.Ok(new AccessTokenResponse(session.AccessToken, session.AccessTokenExpiresAt));
    }

    /// <summary>
    /// Grava o refresh no cookie HttpOnly e impede cache da resposta que leva o access token. Sessão sem persistência (tablet): nenhum
    /// refresh, e o cookie que houver é apagado.
    /// </summary>
    internal static void SetSession(HttpContext http, LoginSession session)
    {
        if (session.RefreshToken is { } refresh && session.RefreshTokenExpiresAt is { } expires)
        {
            http.Response.Cookies.Append(RefreshCookie, refresh, CookieOptions(expires));
        }
        else
        {
            http.Response.Cookies.Delete(RefreshCookie, CookieOptions(DateTimeOffset.UnixEpoch));
        }

        http.Response.Headers.CacheControl = "no-store";
    }

    /// <summary>Cookie do aparelho (ADR 0026 §1): o mesmo padrão do refresh, com a validade do aparelho (180 dias do registro).</summary>
    internal static void SetDeviceCookie(HttpContext http, string secret, DateTimeOffset expires)
    {
        http.Response.Cookies.Append(ShopfloorDeviceCookie.Name, secret, CookieOptions(expires));
        http.Response.Headers.CacheControl = "no-store";
    }

    internal static void DeleteDeviceCookie(HttpContext http) =>
        http.Response.Cookies.Delete(ShopfloorDeviceCookie.Name, CookieOptions(DateTimeOffset.UnixEpoch));

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