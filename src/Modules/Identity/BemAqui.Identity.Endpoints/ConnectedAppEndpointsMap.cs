using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Identity.Application.ConnectedApps;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Identity.Endpoints;

/// <summary>
/// Aplicativos conectados (INT-01, ADR 0053): o ERP como servidor de autorização por tenant. Tudo no módulo <c>conecta</c> (403
/// <c>module_not_released</c> sem ele). Conectar, conferir, autorizar e cancelar: <c>settings.edit</c> (Proprietário). <c>/oauth/token</c> e
/// <c>/oauth/revoke</c> são anônimas de pessoa, autenticadas pelo cliente (<c>client_secret_basic</c>), com rate limit próprio e corpo fora do log
/// (ADR 0009 adendo de 26/11/2026). <c>/integration/connection</c> é do próprio aplicativo (esquema <c>App</c>).
/// </summary>
internal static class ConnectedAppEndpointsMap
{
    public static IEndpointRouteBuilder MapConnectedAppEndpoints(this IEndpointRouteBuilder api)
    {
        var oauth = api.MapGroup("/oauth").WithTags("Aplicativos conectados").RequireModule(ConnectedAppEndpoints.ModuleCode);
        oauth.MapPost("/connect", async (HttpContext http, ICurrentUser user, OAuthAuthorizeService service, CancellationToken ct) =>
                (await service.StartAsync(user.UserId!.Value, Host(http), ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("StartConectaConnection").Produces<ConnectStart>();
        oauth.MapPost("/authorize/inspect", async (AuthorizeRequest request, HttpContext http, ICurrentUser user, OAuthAuthorizeService service, CancellationToken ct) =>
                Results.Ok(await service.InspectAsync(request, user.UserId!.Value, Host(http), ct)))
            .RequirePermission("settings.edit").RequireRateLimiting(ConnectedAppEndpoints.ConsentRateLimitPolicy).WithName("InspectOAuthAuthorize").Produces<AuthorizeInspection>();
        oauth.MapPost("/authorize/consent", async (AuthorizeConsentInput input, HttpContext http, ICurrentUser user, OAuthAuthorizeService service, CancellationToken ct) =>
                (await service.ConsentAsync(input, user.UserId!.Value, Host(http), AuthEndpoints.Ip(http), ct)).ToHttpResult())
            .RequirePermission("settings.edit").RequireRateLimiting(ConnectedAppEndpoints.ConsentRateLimitPolicy).WithName("ConsentOAuthAuthorize").Produces<AuthorizeRedirect>();
        oauth.MapPost("/authorize/deny", async (AuthorizeRequest request, HttpContext http, ICurrentUser user, OAuthAuthorizeService service, CancellationToken ct) =>
                (await service.DenyAsync(request, user.UserId!.Value, Host(http), AuthEndpoints.Ip(http), ct)).ToHttpResult())
            .RequirePermission("settings.edit").RequireRateLimiting(ConnectedAppEndpoints.ConsentRateLimitPolicy).WithName("DenyOAuthAuthorize").Produces<AuthorizeRedirect>();

        oauth.MapPost("/token", TokenAsync).AllowAnonymous().RequireRateLimiting(ConnectedAppEndpoints.TokenRateLimitPolicy)
            .WithName("OAuthToken").Produces<OAuthTokenResponse>().Produces<OAuthError>(StatusCodes.Status400BadRequest);
        oauth.MapPost("/revoke", RevokeAsync).AllowAnonymous().RequireRateLimiting(ConnectedAppEndpoints.TokenRateLimitPolicy).WithName("OAuthRevoke");

        var apps = api.MapGroup("/connected-apps").WithTags("Aplicativos conectados").RequireModule(ConnectedAppEndpoints.ModuleCode);
        apps.MapGet(string.Empty, async (AppConnectService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .RequirePermission("settings.view").WithName("ListConnectedApps").Produces<IReadOnlyList<ConnectedAppListItem>>();
        apps.MapGet("/conecta", async (AppConnectService service, CancellationToken ct) => Results.Ok(await service.PageAsync(ct)))
            .RequireAnyPermissionOf("settings.view", "conecta.view").WithName("GetConectaConnection").Produces<ConnectedAppPage>();
        apps.MapPost("/{id:guid}/revoke", async (Guid id, ICurrentUser user, AppConnectService service, CancellationToken ct) =>
                (await service.RevokeAsync(id, user.UserId!.Value, ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("RevokeConnectedApp");

        var integration = api.MapGroup("/integration").WithTags("Integração (aplicativos)").RequireRateLimiting(ConnectedAppEndpoints.RateLimitPolicy);
        integration.MapGet("/connection", async (HttpContext http, AppConnectService service, CancellationToken ct) =>
                AppClaims.ConnectionOf(http.User) is { } id && await service.SelfAsync(id, ct) is { } view ? Results.Ok(view) : ConnectedAppEndpoints.InvalidToken())
            .RequireConnectedAppSelf().WithName("GetIntegrationConnection").Produces<IntegrationConnectionView>();
        return api;
    }

    private static string Host(HttpContext http) => http.Request.Host.Host.ToLowerInvariant();

    private static void NoStore(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.Pragma = "no-cache";
    }

    private static IResult OAuthFailure(HttpContext http, OAuthError error)
    {
        if (error.IsClientError)
        {
            http.Response.Headers.WWWAuthenticate = "Basic realm=\"bemaqui\"";
            return Results.Json(error, statusCode: StatusCodes.Status401Unauthorized);
        }

        return Results.Json(error, statusCode: StatusCodes.Status400BadRequest);
    }

    private static readonly OAuthError InvalidClient = new(OAuthError.InvalidClient, "Autenticação do cliente recusada.");

    /// <summary>RFC 6749 §4.1.3 e §6 (CA 11 e 13): corpo em formulário, resposta sem cache, erros com corpo uniforme.</summary>
    private static async Task<IResult> TokenAsync(HttpContext http, OAuthTokenService service, CancellationToken ct)
    {
        NoStore(http);
        if (!http.Request.HasFormContentType)
        {
            return OAuthFailure(http, new OAuthError(OAuthError.InvalidRequest, "Envie o pedido como application/x-www-form-urlencoded."));
        }

        var form = await http.Request.ReadFormAsync(ct);
        var app = await service.AuthenticateClientAsync(http.Request.Headers.Authorization.ToString(), Field(form, "client_id"), ct);
        if (app is null)
        {
            return OAuthFailure(http, InvalidClient);
        }

        var outcome = Field(form, "grant_type") switch
        {
            "authorization_code" => await service.ExchangeCodeAsync(app, Field(form, "code"), Field(form, "redirect_uri"), Field(form, "code_verifier"), AuthEndpoints.Ip(http), ct),
            "refresh_token" => await service.RefreshAsync(app, Field(form, "refresh_token"), ct),
            _ => (OAuthOutcome)new OAuthError(OAuthError.UnsupportedGrantType, "Use authorization_code ou refresh_token."),
        };
        return outcome.Response is { } response ? Results.Json(response) : OAuthFailure(http, outcome.Error!);
    }

    /// <summary>RFC 7009 (CA 22): token desconhecido também responde 200.</summary>
    private static async Task<IResult> RevokeAsync(HttpContext http, OAuthTokenService service, CancellationToken ct)
    {
        NoStore(http);
        if (!http.Request.HasFormContentType)
        {
            return OAuthFailure(http, new OAuthError(OAuthError.InvalidRequest, "Envie o pedido como application/x-www-form-urlencoded."));
        }

        var form = await http.Request.ReadFormAsync(ct);
        var app = await service.AuthenticateClientAsync(http.Request.Headers.Authorization.ToString(), Field(form, "client_id"), ct);
        if (app is null)
        {
            return OAuthFailure(http, InvalidClient);
        }

        await service.RevokeAsync(app, Field(form, "token"), ct);
        return Results.Ok();
    }

    private static string? Field(IFormCollection form, string name) => form.TryGetValue(name, out var value) && value.Count == 1 ? value.ToString() : null;
}
