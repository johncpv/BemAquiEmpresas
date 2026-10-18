using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Identity.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Identity.Endpoints;

public sealed record SetGroupMembersRequest(IReadOnlyList<Guid> UserIds);

/// <summary>
/// Grupos e matriz (FND-06b), no módulo <c>identity</c>. Ver: <c>identity.view</c>; criar e duplicar:
/// <c>identity.create</c>; editar matriz, membros e inativar: <c>identity.edit</c> (inativar é edição, sem exclusão).
/// </summary>
internal static class GroupEndpoints
{
    public static IEndpointRouteBuilder MapGroupEndpoints(this IEndpointRouteBuilder api)
    {
        var groups = api.MapGroup("/identity/groups").WithTags("Usuários e acessos").RequireModule("identity");

        groups.MapGet(string.Empty, async (GroupService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .RequirePermission("identity.view").WithName("ListGroups").Produces<IReadOnlyList<GroupSummary>>();

        groups.MapGet("/{id:guid}", async (Guid id, GroupService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("identity.view").WithName("GetGroup").Produces<GroupDetail>();

        groups.MapPost(string.Empty, async (SaveGroupRequest request, ICurrentUser user, GroupService service, CancellationToken ct) =>
                (await service.CreateAsync(request, user.UserId!.Value, ct)).ToHttpResult(saved => Results.Created("/api/v1/identity/groups/" + saved.Id, saved)))
            .RequirePermission("identity.create").WithName("CreateGroup").Produces<GroupSaved>(201);

        groups.MapPost("/{id:guid}/duplicate", async (Guid id, ICurrentUser user, GroupService service, CancellationToken ct) =>
                (await service.DuplicateAsync(id, user.UserId!.Value, ct)).ToHttpResult(saved => Results.Created("/api/v1/identity/groups/" + saved.Id, saved)))
            .RequirePermission("identity.create").WithName("DuplicateGroup").Produces<GroupSaved>(201);

        groups.MapPut("/{id:guid}", async (Guid id, SaveGroupRequest request, ICurrentUser user, GroupService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, user.UserId!.Value, ct)).ToHttpResult())
            .RequirePermission("identity.edit").WithName("UpdateGroup").Produces<GroupSaved>();

        groups.MapPost("/{id:guid}/deactivate", async (Guid id, ICurrentUser user, GroupService service, CancellationToken ct) =>
                (await service.DeactivateAsync(id, user.UserId!.Value, ct)).ToHttpResult())
            .RequirePermission("identity.edit").WithName("DeactivateGroup").Produces<GroupSaved>();

        groups.MapPut("/{id:guid}/members", async (Guid id, SetGroupMembersRequest request, ICurrentUser user, GroupService service, CancellationToken ct) =>
                (await service.SetMembersAsync(id, request.UserIds, user.UserId!.Value, ct)).ToHttpResult())
            .RequirePermission("identity.edit").WithName("SetGroupMembers").Produces<GroupSaved>();

        return api;
    }
}
