using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Inventory.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Inventory.Endpoints;

/// <summary>Locais de estoque (CAD-03 CA 2 a 7). Módulo <c>inventory</c> com as três camadas de acesso (CA 19).</summary>
internal static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder api)
    {
        var locations = api.MapGroup("/inventory/locations").WithTags("Estoque").RequireModule("inventory");
        locations.MapGet(string.Empty, async (string? q, bool? includeInactive, int? page, int? pageSize, LocationService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new LocationListQuery(q, includeInactive == true), new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)))
            .RequirePermission("inventory.view").WithName("ListLocations").Produces<PagedList<LocationDetail>>();
        locations.MapGet("/{id:guid}", async (Guid id, LocationService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("GetLocation").Produces<LocationDetail>();
        locations.MapPost(string.Empty, async (LocationInput input, LocationService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/inventory/locations/" + saved.Id, saved)))
            .RequirePermission("inventory.create").WithName("CreateLocation").Produces<LocationDetail>(StatusCodes.Status201Created);
        locations.MapPut("/{id:guid}", async (Guid id, LocationInput input, LocationService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("inventory.edit").WithName("UpdateLocation").Produces<LocationDetail>();
        locations.MapPost("/{id:guid}/inactivate", async (Guid id, LocationService service, CancellationToken ct) => (await service.SetActiveAsync(id, false, ct)).ToHttpResult())
            .RequirePermission("inventory.edit").WithName("InactivateLocation").Produces<LocationDetail>();
        locations.MapPost("/{id:guid}/reactivate", async (Guid id, LocationService service, CancellationToken ct) => (await service.SetActiveAsync(id, true, ct)).ToHttpResult())
            .RequirePermission("inventory.edit").WithName("ReactivateLocation").Produces<LocationDetail>();
        locations.MapDelete("/{id:guid}", async (Guid id, LocationService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission("inventory.delete").WithName("DeleteLocation");
        return api;
    }
}
