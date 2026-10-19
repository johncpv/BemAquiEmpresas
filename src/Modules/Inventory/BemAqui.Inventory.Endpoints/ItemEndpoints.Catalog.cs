using BemAqui.BuildingBlocks.Web;
using BemAqui.Inventory.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Inventory.Endpoints;

internal static partial class ItemEndpoints
{
    private static void MapUnits(RouteGroupBuilder units)
    {
        units.MapGet(string.Empty, async (bool? includeInactive, UnitService service, CancellationToken ct) => Results.Ok(await service.ListAsync(includeInactive == true, ct)))
            .RequirePermission("inventory.view").WithName("ListUnits").Produces<IReadOnlyList<UnitDetail>>();
        units.MapPost(string.Empty, async (UnitInput input, UnitService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/inventory/units/" + saved.Id, saved)))
            .RequirePermission("inventory.create").WithName("CreateUnit").Produces<UnitDetail>(StatusCodes.Status201Created);
        units.MapPut("/{id:guid}", async (Guid id, UnitInput input, UnitService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("inventory.edit").WithName("UpdateUnit").Produces<UnitDetail>();
        units.MapPost("/{id:guid}/inactivate", async (Guid id, UnitService service, CancellationToken ct) => (await service.SetActiveAsync(id, false, ct)).ToHttpResult())
            .RequirePermission("inventory.edit").WithName("InactivateUnit").Produces<UnitDetail>();
        units.MapPost("/{id:guid}/reactivate", async (Guid id, UnitService service, CancellationToken ct) => (await service.SetActiveAsync(id, true, ct)).ToHttpResult())
            .RequirePermission("inventory.edit").WithName("ReactivateUnit").Produces<UnitDetail>();
        units.MapDelete("/{id:guid}", async (Guid id, UnitService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission("inventory.delete").WithName("DeleteUnit");
    }

    private static void MapCategories(RouteGroupBuilder categories)
    {
        categories.MapGet(string.Empty, async (CategoryService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .RequirePermission("inventory.view").WithName("ListItemCategories").Produces<IReadOnlyList<CategoryDetail>>();
        categories.MapPost(string.Empty, async (CategoryInput input, CategoryService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/inventory/categories/" + saved.Id, saved)))
            .RequirePermission("inventory.create").WithName("CreateItemCategory").Produces<CategoryDetail>(StatusCodes.Status201Created);
        categories.MapPut("/{id:guid}", async (Guid id, CategoryInput input, CategoryService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("inventory.edit").WithName("UpdateItemCategory").Produces<CategoryDetail>();
        categories.MapPost("/{id:guid}/inactivate", async (Guid id, CategoryService service, CancellationToken ct) => (await service.SetActiveAsync(id, false, ct)).ToHttpResult())
            .RequirePermission("inventory.edit").WithName("InactivateItemCategory").Produces<CategoryDetail>();
        categories.MapPost("/{id:guid}/reactivate", async (Guid id, CategoryService service, CancellationToken ct) => (await service.SetActiveAsync(id, true, ct)).ToHttpResult())
            .RequirePermission("inventory.edit").WithName("ReactivateItemCategory").Produces<CategoryDetail>();
        categories.MapDelete("/{id:guid}", async (Guid id, CategoryService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission("inventory.delete").WithName("DeleteItemCategory");
    }
}
