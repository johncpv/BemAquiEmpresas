using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.BuildingBlocks.Notifications;

public static class NotificationEndpoints
{
    /// <summary>GET /api/v1/notifications e POST /api/v1/notifications/{id}/read (mapeados no Host na FND-02).</summary>
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/notifications").WithTags("Notificações");

        group.MapGet("/", async (int? page, int? pageSize, NotificationService service, CancellationToken ct) =>
                (await service.ListMineAsync(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize), ct)).ToHttpResult())
            .WithName("ListMyNotifications");

        group.MapPost("/{id:guid}/read", async (Guid id, NotificationService service, CancellationToken ct) =>
                (await service.MarkReadAsync(id, ct)).ToHttpResult())
            .WithName("MarkNotificationRead");

        return api;
    }
}
