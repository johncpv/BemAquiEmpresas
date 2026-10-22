using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Web;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;

namespace BemAqui.Host.Security;

/// <summary>Uma ou mais ações do mesmo módulo, separadas por <c>|</c>; basta ter uma.</summary>
public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement
{
    public string Module => Permission[..Permission.IndexOf('.', StringComparison.Ordinal)];

    public IReadOnlyList<string> Alternatives => Permission.Split('|');
}

/// <summary>
/// Acesso a um hub (PRD-02 CA 10): módulo liberado e ao menos uma das ações, que podem ser de módulos diferentes
/// (<c>production.view</c> ou <c>shopfloor.view</c>). Policy <c>hub:{modulo}:{acao}|{acao}</c>.
/// </summary>
public sealed record HubAccessRequirement(string Module, IReadOnlyList<string> Alternatives) : IAuthorizationRequirement;

/// <summary>Policy dinâmica <c>perm:{modulo}.{acao}</c> (02-arquitetura §3): usuário autenticado + requisito de permissão.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(ModuleEndpoints.PermissionPolicyPrefix, StringComparison.Ordinal))
        {
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName[ModuleEndpoints.PermissionPolicyPrefix.Length..]))
                .Build();
        }

        if (policyName.StartsWith(BemAqui.Host.Realtime.HubAccess.PolicyPrefix, StringComparison.Ordinal))
        {
            var rest = policyName[BemAqui.Host.Realtime.HubAccess.PolicyPrefix.Length..];
            var separator = rest.IndexOf(':', StringComparison.Ordinal);
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new HubAccessRequirement(rest[..separator], rest[(separator + 1)..].Split('|')))
                .Build();
        }

        return await base.GetPolicyAsync(policyName);
    }
}

/// <summary>Ordem do CLAUDE.md: módulo liberado no tenant, depois ação nos grupos do usuário (permissão efetiva).</summary>
public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    public const string NotReleasedReason = "module_not_released";

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);
        if (context.Resource is not HttpContext http)
        {
            return;
        }

        // Alternativas de módulos diferentes (RequireAnyPermissionOf): basta um dos módulos estar liberado.
        var resolved = http.RequestServices.GetRequiredService<TenantContextHolder>().Resolved;
        if (!requirement.Alternatives.Any(a => resolved?.IsReleased(a[..a.IndexOf('.', StringComparison.Ordinal)]) == true))
        {
            context.Fail(new AuthorizationFailureReason(this, NotReleasedReason + ":" + requirement.Module));
            return;
        }

        if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId))
        {
            return;
        }

        var effective = await http.RequestServices.GetRequiredService<IPermissionService>().GetAsync(userId, http.RequestAborted);
        http.Response.Headers[EffectivePermissions.Header] = effective.Version; // o front recarrega /me/permissions se mudou
        if (requirement.Alternatives.Any(effective.Has))
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>Hub: módulo liberado no tenant, depois uma das ações na permissão efetiva (mesma ordem do <see cref="PermissionHandler"/>).</summary>
public sealed class HubAccessHandler : AuthorizationHandler<HubAccessRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, HubAccessRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);
        if (context.Resource is not HttpContext http)
        {
            return;
        }

        if (http.RequestServices.GetRequiredService<TenantContextHolder>().Resolved?.IsReleased(requirement.Module) != true)
        {
            context.Fail(new AuthorizationFailureReason(this, PermissionHandler.NotReleasedReason + ":" + requirement.Module));
            return;
        }

        if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId))
        {
            return;
        }

        var effective = await http.RequestServices.GetRequiredService<IPermissionService>().GetAsync(userId, http.RequestAborted);
        if (requirement.Alternatives.Any(effective.Has))
        {
            context.Succeed(requirement);
        }
    }
}

/// <summary>403 com <c>code</c>: <c>module_not_released</c> (camada 1) ou <c>forbidden</c> (camadas 2 e 3).</summary>
public sealed class PermissionResultHandler : IAuthorizationMiddlewareResultHandler
{
    public const string ForbiddenMessage = "Você não tem acesso a esta funcionalidade. Fale com o administrador.";

    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);
        if (authorizeResult.Forbidden && policy?.Requirements.Any(r => r is PermissionRequirement or HubAccessRequirement) == true)
        {
            var notReleased = authorizeResult.AuthorizationFailure?.FailureReasons
                .Select(r => r.Message).FirstOrDefault(m => m.StartsWith(PermissionHandler.NotReleasedReason + ":", StringComparison.Ordinal));
            await (notReleased is not null
                    ? ModuleEndpoints.NotReleased(notReleased[(PermissionHandler.NotReleasedReason.Length + 1)..])
                    : Error.Forbidden(Error.Codes.Forbidden, ForbiddenMessage).ToProblem())
                .ExecuteAsync(context);
            return;
        }

        await _default.HandleAsync(next, context, policy!, authorizeResult);
    }

    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        services.AddScoped<IAuthorizationHandler, HubAccessHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, PermissionResultHandler>();
    }
}
