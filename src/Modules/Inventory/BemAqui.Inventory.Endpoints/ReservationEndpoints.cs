using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Inventory.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Inventory.Endpoints;

/// <summary>
/// Reservas e faltas (EST-02 CA 14, 15, 17 e 18): só leitura, no módulo <c>inventory</c> (403 <c>module_not_released</c>). A aba
/// Material e o histórico da OS aceitam <c>inventory.view</c> ou <c>production.view</c>; a lista Faltas e o histórico do item exigem
/// <c>inventory.view</c>. A reserva é lida pela OS (IDOR: reserva de outra OS responde 404).
/// </summary>
internal static class ReservationEndpoints
{
    public static IEndpointRouteBuilder MapReservationEndpoints(this IEndpointRouteBuilder api)
    {
        var inventory = api.MapGroup("/inventory").WithTags("Estoque").RequireModule("inventory");
        var order = inventory.MapGroup("/production-orders/{id:guid}/reservations");
        order.MapGet(string.Empty, async (Guid id, StockReservationService service, CancellationToken ct) => (await service.MaterialAsync(id, ct)).ToHttpResult())
            .RequireAnyPermissionOf("inventory.view", "production.view").WithName("GetOrderMaterial").Produces<OrderMaterialView>();
        order.MapGet("/history", async (Guid id, int? page, int? pageSize, StockReservationService service, CancellationToken ct) =>
                (await service.OrderHistoryAsync(id, Page(page, pageSize), ct)).ToHttpResult())
            .RequireAnyPermissionOf("inventory.view", "production.view").WithName("GetOrderReservationHistory").Produces<PagedList<ReservationChangeRow>>();
        order.MapGet("/{reservationId:guid}", async (Guid id, Guid reservationId, StockReservationService service, CancellationToken ct) =>
                (await service.ReservationAsync(id, reservationId, ct)).ToHttpResult())
            .RequireAnyPermissionOf("inventory.view", "production.view").WithName("GetOrderReservation").Produces<ReservationDetail>();
        inventory.MapGet("/shortages", async (string? q, Guid? projectId, Guid? itemId, int? page, int? pageSize, StockReservationService service, CancellationToken ct) =>
                Results.Ok(await service.ShortagesAsync(new ShortageListQuery(q, projectId, itemId), Page(page, pageSize), ct)))
            .RequirePermission("inventory.view").WithName("ListStockShortages").Produces<PagedList<ShortageRow>>();
        inventory.MapGet("/items/{itemId:guid}/reservations/history", async (Guid itemId, int? page, int? pageSize, StockReservationService service, CancellationToken ct) =>
                (await service.ItemHistoryAsync(itemId, Page(page, pageSize), ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("GetItemReservationHistory").Produces<PagedList<ReservationChangeRow>>();
        return api;
    }

    private static PageRequest Page(int? page, int? pageSize) => new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize();
}
