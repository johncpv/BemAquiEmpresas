using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.BuildingBlocks.Web;

/// <summary>Metadado do endpoint: a qual módulo do catálogo ele pertence.</summary>
public sealed record RequiresModuleMetadata(string ModuleCode);

/// <summary>Módulo do catálogo como o front vê: para o menu (FND-06d) e a tela "não liberada".</summary>
public sealed record ModuleSummary(string Code, string Name, string Area, int Order, string Description, bool Released);

public static class ModuleEndpoints
{
    public const string NotReleasedMessage = "Funcionalidade não liberada nessa versão do sistema.";
    public const string PermissionPolicyPrefix = "perm:";

    /// <summary>
    /// Exige <c>modulo.acao</c> (policy <c>perm:</c>): módulo não liberado → 403 <c>module_not_released</c>; sem a ação
    /// nos grupos → 403 <c>forbidden</c> (FND-06b CA 18). Código fora do catálogo falha aqui e no teste de arquitetura.
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!ModuleCatalog.IsKnownPermission(permission))
        {
            throw new ArgumentException("Permissão fora do ModuleCatalog: " + permission, nameof(permission));
        }

        return builder.RequireAuthorization(PermissionPolicyPrefix + permission);
    }

    /// <summary>
    /// Exige uma das ações do mesmo módulo (policy <c>perm:modulo.a|modulo.b</c>), ex.: consultar CNPJ com <c>parties.create</c>
    /// ou <c>parties.edit</c> (CAD-01b CA 11). Mesmas respostas 403 do <see cref="RequirePermission{TBuilder}"/>.
    /// </summary>
    public static TBuilder RequireAnyPermission<TBuilder>(this TBuilder builder, params string[] permissions)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(permissions);
        if (permissions.Length == 0 || permissions.Any(p => !ModuleCatalog.IsKnownPermission(p)) || permissions.Select(p => p.Split('.')[0]).Distinct().Count() != 1)
        {
            throw new ArgumentException("Permissões fora do ModuleCatalog ou de módulos diferentes: " + string.Join(", ", permissions), nameof(permissions));
        }

        return builder.RequireAuthorization(PermissionPolicyPrefix + string.Join('|', permissions));
    }

    /// <summary>
    /// Camada 1 do controle de acesso (CLAUDE.md): módulo não liberado para o tenant responde 403
    /// <c>module_not_released</c>, antes da checagem de grupo e ação (FND-06 CA 4). Use no grupo de rotas do módulo.
    /// </summary>
    public static TBuilder RequireModule<TBuilder>(this TBuilder builder, string moduleCode)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (ModuleCatalog.Find(moduleCode) is null)
        {
            throw new ArgumentException("Módulo fora do ModuleCatalog: " + moduleCode, nameof(moduleCode));
        }

        builder.WithMetadata(new RequiresModuleMetadata(moduleCode));
        builder.AddEndpointFilter(async (context, next) =>
            IsReleased(context.HttpContext, moduleCode) ? await next(context) : NotReleased(moduleCode));
        return builder;
    }

    public static bool IsReleased(HttpContext http, string moduleCode)
    {
        ArgumentNullException.ThrowIfNull(http);
        return http.RequestServices.GetService<TenantContextHolder>()?.Resolved?.IsReleased(moduleCode) == true;
    }

    public static IResult NotReleased(string moduleCode) =>
        Results.Problem(
            detail: NotReleasedMessage,
            statusCode: StatusCodes.Status403Forbidden,
            title: "Acesso negado",
            extensions: new Dictionary<string, object?> { ["code"] = Error.Codes.ModuleNotReleased, ["module"] = moduleCode });

    /// <summary>Catálogo com o que está liberado para o tenant do host (autenticado; base do menu da FND-06d).</summary>
    public static IEndpointRouteBuilder MapModuleEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/modules", (TenantContextHolder tenant) => Results.Ok(ModuleCatalog.All
                .OrderBy(m => Array.IndexOf(ModuleAreas.All, m.Area)).ThenBy(m => m.Order)
                .Select(m => new ModuleSummary(m.Code, m.Name, m.Area, m.Order, m.Description, tenant.Resolved?.IsReleased(m.Code) == true))))
            .WithName("ListModules").Produces<IEnumerable<ModuleSummary>>()
            .WithTags("Módulos");
        return api;
    }
}
