using System.Text.Encodings.Web;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Identity.Application.Kiosk;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace BemAqui.Host.Security;

/// <summary>
/// Esquema <c>Kiosk</c> (ADR 0030 §2, PNL-01a CA 9): lê o cookie <c>__Host-bq_kiosk</c> e confere a sessão no banco do tenant do host
/// (o hash de outro tenant não existe aqui). O principal tem só o tenant, a tela, a sessão e o escopo; nunca <c>sub</c> nem permissão.
/// Só os endpoints com <c>RequireKiosk()</c> (policy <c>kiosk</c>) o usam; nenhum endpoint de usuário aceita este esquema.
/// </summary>
public sealed class KioskAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string StatusItem = "bq.kiosk.status";
    public const string RevokedCode = "kiosk_revoked";
    public const string SessionInvalidCode = "kiosk_session_invalid";
    public const string NetworkCode = "kiosk_network";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var tenant = Context.RequestServices.GetRequiredService<TenantContextHolder>().Tenant;
        var cookie = Request.Cookies[KioskCookie.Name];
        if (tenant is null || string.IsNullOrEmpty(cookie))
        {
            return AuthenticateResult.NoResult();
        }

        var check = await Context.RequestServices.GetRequiredService<IKioskSessionValidator>().CheckAsync(cookie, Context.Connection.RemoteIpAddress, Context.RequestAborted);
        Context.Items[StatusItem] = check.Status;
        if (check.Status != KioskSessionStatus.Valid || check.Session is null)
        {
            return AuthenticateResult.Fail("Sessão de quiosque recusada.");
        }

        return AuthenticateResult.Success(new AuthenticationTicket(KioskPrincipal.Create(tenant.Id, check.Session), KioskClaims.Scheme));
    }

    /// <summary>401 com o motivo para a TV mostrar o estado certo; rede fora da permitida responde 403 (CA 16).</summary>
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.CacheControl = "no-store";
        var status = Context.Items.TryGetValue(StatusItem, out var value) && value is KioskSessionStatus s ? s : KioskSessionStatus.Invalid;
        var company = Context.RequestServices.GetRequiredService<KioskNotices>().CompanyName;
        var (code, detail, http) = status switch
        {
            KioskSessionStatus.Revoked => (RevokedCode, $"Esta tela foi desligada por {company}.", StatusCodes.Status401Unauthorized),
            KioskSessionStatus.NetworkDenied => (NetworkCode, KioskRules.NetworkMessage, StatusCodes.Status403Forbidden),
            _ => (SessionInvalidCode, KioskRules.ReconnectMessage, StatusCodes.Status401Unauthorized),
        };
        await Results.Problem(detail: detail, statusCode: http, title: "Tela desconectada",
                extensions: new Dictionary<string, object?> { ["code"] = code, ["companyName"] = company })
            .ExecuteAsync(Context);
    }
}

/// <summary>Policy <c>kiosk</c>: principal de quiosque do tenant do host e módulo <c>kiosk</c> liberado (camada 1).</summary>
public sealed record KioskRequirement : IAuthorizationRequirement;

public sealed class KioskRequirementHandler : AuthorizationHandler<KioskRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, KioskRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Resource is not HttpContext http)
        {
            return Task.CompletedTask;
        }

        var resolved = http.RequestServices.GetRequiredService<TenantContextHolder>().Resolved;
        if (resolved?.IsReleased("kiosk") != true)
        {
            context.Fail(new AuthorizationFailureReason(this, PermissionHandler.NotReleasedReason + ":kiosk"));
            return Task.CompletedTask;
        }

        var user = context.User;
        if (user.Identity?.AuthenticationType == KioskClaims.Scheme
            && Guid.TryParse(user.FindFirst(KioskClaims.Tenant)?.Value, out var tenantId) && tenantId == resolved.Tenant.Id
            && user.FindFirst(KioskClaims.KioskId) is not null && user.FindFirst("sub") is null)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public static class KioskAuthentication
{
    /// <summary>Esquema e policy do quiosque (além do JWT do usuário, que continua o esquema padrão).</summary>
    public static IServiceCollection AddKioskAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, KioskAuthenticationHandler>(KioskClaims.Scheme, null);
        services.AddAuthorizationBuilder().AddPolicy(KioskClaims.Policy, p => p
            .AddAuthenticationSchemes(KioskClaims.Scheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new KioskRequirement()));
        services.AddScoped<IAuthorizationHandler, KioskRequirementHandler>();
        return services;
    }
}

/// <summary>
/// Escopo do principal de quiosque (ADR 0030 §2, PNL-01a CA 9), logo depois da autenticação. Em <c>/api/v1/kiosk/*</c> só o GET existe
/// (fora a troca do token, <c>POST /session</c>): outro método responde 405. Fora do painel, um request sem usuário que traz uma sessão
/// de quiosque válida recebe 403 <c>kiosk_scope</c>, antes de qualquer regra do endpoint. Rotas anônimas seguem normais; um usuário
/// autenticado (JWT) é tratado como usuário (o cookie de quiosque é ignorado).
/// </summary>
public sealed class KioskScopeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        var path = http.Request.Path;
        if (path.StartsWithSegments(KioskEndpoints.PathPrefix, StringComparison.OrdinalIgnoreCase)
            && !HttpMethods.IsGet(http.Request.Method)
            && !(path.Equals(KioskEndpoints.SessionPath, StringComparison.OrdinalIgnoreCase) && HttpMethods.IsPost(http.Request.Method)))
        {
            http.Response.Headers.Allow = "GET";
            await Results.Problem(detail: "O painel da TV só lê.", statusCode: StatusCodes.Status405MethodNotAllowed, title: "Método não permitido",
                    extensions: new Dictionary<string, object?> { ["code"] = "method_not_allowed" })
                .ExecuteAsync(http);
            return;
        }

        var endpoint = http.GetEndpoint();
        if (endpoint is null
            || endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
            || endpoint.Metadata.GetMetadata<KioskEndpointAttribute>() is not null
            || http.User.Identity?.IsAuthenticated == true
            || !http.Request.Headers.Authorization.ToString().Equals(string.Empty, StringComparison.Ordinal)
            || http.Request.Query.ContainsKey("access_token")
            || string.IsNullOrEmpty(http.Request.Cookies[KioskCookie.Name]))
        {
            await next(http);
            return;
        }

        var kiosk = await http.AuthenticateAsync(KioskClaims.Scheme);
        if (kiosk.Succeeded)
        {
            await Results.Problem(detail: KioskEndpoints.ScopeMessage, statusCode: StatusCodes.Status403Forbidden, title: "Acesso negado",
                    extensions: new Dictionary<string, object?> { ["code"] = KioskEndpoints.ScopeCode })
                .ExecuteAsync(http);
            return;
        }

        await next(http);
    }
}
