using BemAqui.BuildingBlocks.Web;
using BemAqui.Inventory.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Inventory.Endpoints;

/// <summary>
/// Capacidade de produzir (EST-06 CA 6): só leitura, nos módulos <c>inventory</c> (grupo) e <c>engineering</c> (a BOM), com
/// <c>inventory.view</c> e <c>engineering.view</c>. Pedidos em aberto (<c>purchasing.view</c>) e o valor da falta
/// (<c>projects.view_cost</c>) entram só no que o serviço mostra, sem barrar a rota.
/// </summary>
internal static class CapacityEndpoints
{
    public static IEndpointRouteBuilder MapCapacityEndpoints(this IEndpointRouteBuilder api)
    {
        var capacity = api.MapGroup("/inventory/production-capacity").WithTags("Estoque").RequireModule("inventory");
        capacity.MapGet(string.Empty, async (Guid? productionOrderId, Guid? productId, decimal? quantity, ProductionCapacityService service, CancellationToken ct) =>
                (await service.CalculateAsync(productionOrderId, productId, quantity, ct)).ToHttpResult())
            .RequireModule("engineering").RequirePermission("inventory.view").RequirePermission("engineering.view").WithName("GetProductionCapacity").Produces<ProductionCapacityView>();
        capacity.MapGet("/references", async (string? q, Guid? productId, ProductionCapacityService service, CancellationToken ct) => Results.Ok(await service.ReferencesAsync(q, productId, ct)))
            .RequireModule("engineering").RequirePermission("inventory.view").RequirePermission("engineering.view").WithName("ListProductionCapacityReferences")
            .Produces<IReadOnlyList<CapacityReference>>();
        return api;
    }
}
