using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Inventory.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Inventory.Endpoints;

/// <summary>
/// Estoque → Divergências (PDV-05 CA 7): as faltas de saldo das vendas do caixa, por local e item, com <c>inventory.view</c>; "Aplicar baixa"
/// com a permissão de ajuste que já existe (<c>inventory.adjust</c>) e <c>Idempotency-Key</c>, porque cria movimento.
/// </summary>
internal static class ShortfallEndpoints
{
    public static IEndpointRouteBuilder MapShortfallEndpoints(this IEndpointRouteBuilder api)
    {
        var shortfalls = api.MapGroup("/inventory/shortfalls").WithTags("Estoque").RequireModule("inventory");
        shortfalls.MapGet(string.Empty, async (string? q, Guid? locationId, int? page, int? pageSize, StockShortfallService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new StockShortfallListQuery(q, locationId), new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)))
            .RequirePermission("inventory.view").WithName("ListStockShortfalls").Produces<PagedList<StockShortfallRow>>();
        shortfalls.MapPost("/apply", async (ApplyStockShortfallInput input, StockShortfallService service, CancellationToken ct) => (await service.ApplyAsync(input, ct)).ToHttpResult())
            .RequirePermission("inventory.adjust").RequireIdempotencyKey().WithName("ApplyStockShortfall").Produces<ApplyStockShortfallResult>();
        return api;
    }
}
