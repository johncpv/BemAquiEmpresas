using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Web;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;

namespace BemAqui.Host.Security;

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement
{
    public string Module => Permission[..Permission.IndexOf('.', StringComparison.Ordinal)];
}

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

        if (http.RequestServices.GetRequiredService<TenantContextHolder>().Resolved?.IsReleased(requirement.Module) != true)
        {
            context.Fail(new AuthorizationFailureReason(this, NotReleasedReason + ":" + requirement.Module));
            return;
        }

        if (Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId)
            && (await http.RequestServices.GetRequiredService<IPermissionService>().GetAsync(userId, http.RequestAborted)).Has(requirement.Permission))
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
        if (authorizeResult.Forbidden && policy?.Requirements.OfType<PermissionRequirement>().Any() == true)
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
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, PermissionResultHandler>();
    }
}
