using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Identity.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Identity.Endpoints;

public sealed record SetUserGroupsRequest(IReadOnlyList<Guid> GroupIds);

public sealed record MyModule(string Code, string Name, string Area, bool Released, bool Enabled, IReadOnlyList<string> Actions);

public sealed record MyPermissionsResponse(IReadOnlyList<string> Permissions, bool IsOwner, string Version, IReadOnlyList<MyModule> Modules);

/// <summary>
/// Usuários (FND-06c, módulo <c>identity</c>): ver com <c>identity.view</c>, convidar com <c>identity.create</c> e o
/// resto com <c>identity.edit</c>. Configurações → Segurança no módulo <c>settings</c>. E <c>/me/permissions</c>.
/// </summary>
internal static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder api)
    {
        var users = api.MapGroup("/identity/users").WithTags("Usuários e acessos").RequireModule("identity");
        users.MapGet(string.Empty, async (UserService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .RequirePermission("identity.view").WithName("ListUsers");
        users.MapPost("/invite", async (InviteUserRequest request, ICurrentUser user, UserService service, CancellationToken ct) =>
                (await service.InviteAsync(request, user.UserId!.Value, ct)).ToHttpResult(invited => Results.Created("/api/v1/identity/users/" + invited.Id, invited)))
            .RequirePermission("identity.create").WithName("InviteUser");
        users.MapPost("/{id:guid}/resend-invite", async (Guid id, ICurrentUser user, UserService service, CancellationToken ct) =>
                (await service.ResendInviteAsync(id, user.UserId!.Value, ct)).ToHttpResult())
            .RequirePermission("identity.edit").WithName("ResendInvite");
        users.MapPost("/{id:guid}/cancel-invite", async (Guid id, ICurrentUser user, UserService service, CancellationToken ct) =>
                (await service.CancelInviteAsync(id, user.UserId!.Value, ct)).ToHttpResult())
            .RequirePermission("identity.edit").WithName("CancelInvite");
        users.MapPost("/{id:guid}/deactivate", async (Guid id, ICurrentUser user, UserService service, CancellationToken ct) =>
                (await service.DeactivateAsync(id, user.UserId!.Value, ct)).ToHttpResult())
            .RequirePermission("identity.edit").WithName("DeactivateUser");
        users.MapPost("/{id:guid}/reactivate", async (Guid id, ICurrentUser user, UserService service, CancellationToken ct) =>
                (await service.ReactivateAsync(id, user.UserId!.Value, ct)).ToHttpResult())
            .RequirePermission("identity.edit").WithName("ReactivateUser");
        users.MapPut("/{id:guid}/groups", async (Guid id, SetUserGroupsRequest request, ICurrentUser user, UserService service, CancellationToken ct) =>
                (await service.SetGroupsAsync(id, request.GroupIds, user.UserId!.Value, ct)).ToHttpResult())
            .RequirePermission("identity.edit").WithName("SetUserGroups");

        var security = api.MapGroup("/settings/security").WithTags("Configurações").RequireModule("settings");
        security.MapGet(string.Empty, async (SecuritySettings settings, CancellationToken ct) => Results.Ok(await settings.GetAsync(ct)))
            .RequirePermission("settings.view").WithName("GetSecuritySettings");
        security.MapPut(string.Empty, async (SecuritySettingsDto request, ICurrentUser user, SecuritySettings settings, CancellationToken ct) =>
                Results.Ok(await settings.SaveAsync(request, user.UserId!.Value, ct)))
            .RequirePermission("settings.edit").WithName("SaveSecuritySettings");

        // Permissões de quem está logado (CA 1): o front esconde ações com <Can>; a autorização real é sempre no backend.
        api.MapGet("/me/permissions", async (ICurrentUser user, IPermissionService permissions, TenantContextHolder tenant, HttpContext http, CancellationToken ct) =>
            {
                var effective = await permissions.GetAsync(user.UserId!.Value, ct);
                http.Response.Headers[EffectivePermissions.Header] = effective.Version;
                var modules = ModuleCatalog.All
                    .OrderBy(m => Array.IndexOf(ModuleAreas.All, m.Area)).ThenBy(m => m.Order)
                    .Select(m =>
                    {
                        var actions = m.Actions.Where(a => effective.Has(m.Code + "." + a)).ToList();
                        return new MyModule(m.Code, m.Name, m.Area, tenant.Resolved?.IsReleased(m.Code) == true, actions.Count > 0, actions);
                    })
                    .ToList();
                return Results.Ok(new MyPermissionsResponse(effective.Permissions.Order(StringComparer.Ordinal).ToList(), effective.IsOwner, effective.Version, modules));
            })
            .WithTags("Identidade").WithName("GetMyPermissions");

        return api;
    }
}
