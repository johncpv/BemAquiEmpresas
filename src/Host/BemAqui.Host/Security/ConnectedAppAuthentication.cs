using System.Text.Encodings.Web;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace BemAqui.Host.Security;

/// <summary>
/// Esquema <c>App</c> (INT-01 CA 15 a 18, ADR 0053 §4): <c>Authorization: Bearer bqa_…</c> achado pelo hash no banco do tenant do host. O principal
/// não tem <c>sub</c>: tem <c>app</c>, <c>conn</c>, <c>grantor</c> e <c>tid</c>. A concessão fica no <see cref="ConnectedAppContext"/> do request (a
/// permissão efetiva do autorizador passa a ser limitada a ela) e a auditoria grava a origem "Integração {aplicativo} (autorizada por {pessoa})".
/// Toda falha de token responde 401 <c>invalid_token</c>; autorizador inativo, 401 <c>app_grantor_inactive</c>. O token nunca vai para o log.
/// </summary>
public sealed class ConnectedAppAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string StatusItem = "bq.app.status";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var tenant = Context.RequestServices.GetRequiredService<TenantContextHolder>().Tenant;
        var header = Request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";
        if (tenant is null || !header.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = header[bearer.Length..].Trim();
        if (!token.StartsWith(AppClaims.AccessPrefix, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        var result = await Context.RequestServices.GetRequiredService<IConnectedAppAuthenticator>()
            .AuthenticateAsync(token, Context.Connection.RemoteIpAddress?.ToString(), Context.RequestAborted);
        Context.Items[StatusItem] = result.Status;
        if (result is not { Status: AppTokenStatus.Valid, Grant: { } grant })
        {
            return AuthenticateResult.Fail("Token de aplicativo recusado.");
        }

        Context.RequestServices.GetRequiredService<ConnectedAppContext>().Current = grant;
        Context.RequestServices.GetRequiredService<AuditOrigin>().Label = "Integração " + grant.AppName + " (autorizada por " + grant.GrantorName + ")";
        return AuthenticateResult.Success(new AuthenticationTicket(AppClaims.Create(tenant.Id, grant), AppClaims.Scheme));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.CacheControl = "no-store";
        var grantorInactive = Context.Items.TryGetValue(StatusItem, out var status) && status is AppTokenStatus.GrantorInactive;
        Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
        await (grantorInactive ? ConnectedAppEndpoints.GrantorInactive() : ConnectedAppEndpoints.InvalidToken()).ExecuteAsync(Context);
    }
}

/// <summary>
/// Policy <c>connected-app</c>: principal de aplicativo do tenant do host (sem <c>sub</c>), módulo <c>conecta</c> liberado (camada 1). As rotas de
/// negócio somam o <c>RequirePermission</c>, que o <see cref="PermissionHandler"/> avalia com a concessão ∩ as permissões atuais do autorizador.
/// </summary>
public sealed record ConnectedAppRequirement : IAuthorizationRequirement;

public sealed class ConnectedAppRequirementHandler : AuthorizationHandler<ConnectedAppRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ConnectedAppRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Resource is not HttpContext http)
        {
            return Task.CompletedTask;
        }

        var resolved = http.RequestServices.GetRequiredService<TenantContextHolder>().Resolved;
        if (resolved?.IsReleased(ConnectedAppEndpoints.ModuleCode) != true)
        {
            context.Fail(new AuthorizationFailureReason(this, PermissionHandler.NotReleasedReason + ":" + ConnectedAppEndpoints.ModuleCode));
            return Task.CompletedTask;
        }

        var user = context.User;
        if (AppClaims.IsApp(user) && Guid.TryParse(user.FindFirst(AppClaims.Tenant)?.Value, out var tenantId) && tenantId == resolved.Tenant.Id)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Em <c>/api/v1/integration/*</c>, autentica o aplicativo antes do rate limit (a política <c>app-api</c> é por conexão). JWT de pessoa nessas rotas
/// não vale: a policy pede só o esquema <c>App</c>.
/// </summary>
public sealed class ConnectedAppAuthenticationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Request.Path.StartsWithSegments(ConnectedAppEndpoints.PathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var result = await context.AuthenticateAsync(AppClaims.Scheme);
            // Só troca o principal quando o aplicativo autenticou: a sessão por PIN continua visível para o PinSessionScopeMiddleware (403 pin_scope),
            // e JWT de pessoa cai no 401 da policy, que pede só o esquema App.
            if (result.Succeeded)
            {
                context.User = result.Principal!;
            }
        }

        await next(context);
    }
}

public static class ConnectedAppAuthentication
{
    public static IServiceCollection AddConnectedAppAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, ConnectedAppAuthenticationHandler>(AppClaims.Scheme, null);
        services.AddAuthorizationBuilder().AddPolicy(AppClaims.Policy, p => p
            .AddAuthenticationSchemes(AppClaims.Scheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new ConnectedAppRequirement()));
        services.AddScoped<IAuthorizationHandler, ConnectedAppRequirementHandler>();
        return services;
    }
}
