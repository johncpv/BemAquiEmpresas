using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Inventory.Endpoints;

/// <summary>
/// Séries do produto acabado (PRD-06a2 CA 11 e 12): só leitura, no módulo <c>inventory</c> (403 <c>module_not_released</c>). A aba Séries
/// da OS aceita <c>inventory.view</c> ou <c>production.view</c> (como a aba Material); a aba Séries do item exige <c>inventory.view</c>.
/// IDOR: a série é lida pela OS (série de outra OS responde 404). Nenhum DTO traz custo (CA 14).
/// </summary>
internal static class SerialEndpoints
{
    public static IEndpointRouteBuilder MapSerialEndpoints(this IEndpointRouteBuilder api)
    {
        var inventory = api.MapGroup("/inventory").WithTags("Estoque").RequireModule("inventory");
        var order = inventory.MapGroup("/production-orders/{id:guid}/serials");
        order.MapGet(string.Empty, async (Guid id, SerialQueryService service, CancellationToken ct) => (await service.ForOrderAsync(id, ct)).ToHttpResult())
            .RequireAnyPermissionOf("inventory.view", "production.view").WithName("GetOrderSerials").Produces<OrderSerialsView>();
        order.MapGet("/{serialId:guid}", async (Guid id, Guid serialId, SerialQueryService service, CancellationToken ct) => (await service.SerialAsync(id, serialId, ct)).ToHttpResult())
            .RequireAnyPermissionOf("inventory.view", "production.view").WithName("GetOrderSerial").Produces<OrderSerialDetail>();
        inventory.MapGet("/items/{itemId:guid}/serials", async (Guid itemId, SerialState? status, int? page, int? pageSize, SerialQueryService service, CancellationToken ct) =>
                (await service.ForItemAsync(itemId, status, new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("ListItemSerials").Produces<PagedList<ItemSerialRow>>();
        return api;
    }
}
