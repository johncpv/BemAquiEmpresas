using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Inventory.Endpoints;

/// <summary>Itens, unidades, categorias e garantia padrão (CAD-02a). Módulo <c>inventory</c> com as três camadas de acesso (CA 22).</summary>
internal static partial class ItemEndpoints
{
    public static IEndpointRouteBuilder MapItemEndpoints(this IEndpointRouteBuilder api)
    {
        var inventory = api.MapGroup("/inventory").WithTags("Estoque").RequireModule("inventory");
        MapItems(inventory.MapGroup("/items"));
        MapUnits(inventory.MapGroup("/units"));
        MapCategories(inventory.MapGroup("/categories"));
        MapItemLinks(inventory);
        inventory.MapGet("/warranty-defaults", async (WarrantyDefaultService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .RequirePermission("inventory.view").WithName("ListWarrantyDefaults").Produces<IReadOnlyList<WarrantyDefaultDetail>>();
        inventory.MapPut("/warranty-defaults/{itemType}", async (ItemType itemType, WarrantyDefaultInput input, WarrantyDefaultService service, CancellationToken ct) =>
                (await service.UpdateAsync(itemType, input, ct)).ToHttpResult())
            .RequirePermission("inventory.edit").WithName("UpdateWarrantyDefault").Produces<WarrantyDefaultDetail>();
        return api;
    }

    private static void MapItems(RouteGroupBuilder items)
    {
        items.MapGet(string.Empty, async (string? q, ItemType? type, Guid? categoryId, bool? includeInactive, int? page, int? pageSize, string? sort, ItemService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new ItemListQuery(q, type, categoryId, includeInactive == true), new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize, sort).Normalize(), ct)))
            .RequirePermission("inventory.view").WithName("ListItems").Produces<PagedList<ItemListItem>>();
        items.MapGet("/search", async (string? q, ItemService service, CancellationToken ct) => Results.Ok(await service.SearchHitsAsync(q, ct)))
            .RequirePermission("inventory.view").WithName("SearchItems").Produces<IReadOnlyList<ItemSearchHit>>();
        items.MapGet("/{id:guid}", async (Guid id, ItemService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("GetItem").Produces<ItemDetail>();
        items.MapPost(string.Empty, async (ItemInput input, ItemService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/inventory/items/" + saved.Id, saved)))
            .RequirePermission("inventory.create").WithName("CreateItem").Produces<ItemDetail>(StatusCodes.Status201Created);
        items.MapPut("/{id:guid}", async (Guid id, ItemInput input, ItemService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("inventory.edit").WithName("UpdateItem").Produces<ItemDetail>();
        items.MapPost("/{id:guid}/inactivate", async (Guid id, ItemService service, CancellationToken ct) => (await service.SetActiveAsync(id, false, ct)).ToHttpResult())
            .RequirePermission("inventory.edit").WithName("InactivateItem").Produces<ItemDetail>();
        items.MapPost("/{id:guid}/reactivate", async (Guid id, ItemService service, CancellationToken ct) => (await service.SetActiveAsync(id, true, ct)).ToHttpResult())
            .RequirePermission("inventory.edit").WithName("ReactivateItem").Produces<ItemDetail>();
        items.MapDelete("/{id:guid}", async (Guid id, ItemService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission("inventory.delete").WithName("DeleteItem");
    }
}
