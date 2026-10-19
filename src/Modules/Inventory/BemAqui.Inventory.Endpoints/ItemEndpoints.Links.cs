using BemAqui.BuildingBlocks.Web;
using BemAqui.Inventory.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Inventory.Endpoints;

internal static partial class ItemEndpoints
{
    /// <summary>Conversões e códigos do fornecedor (CAD-02b): o filho sempre é conferido contra o item da URL (CA 13).</summary>
    private static void MapItemLinks(RouteGroupBuilder inventory)
    {
        inventory.MapGet("/supplier-options", async (string? q, ItemSupplierCodeService service, CancellationToken ct) => Results.Ok(await service.SupplierOptionsAsync(q, ct)))
            .RequirePermission("inventory.view").WithName("ListSupplierOptions").Produces<IReadOnlyList<SupplierOption>>();

        var conversions = inventory.MapGroup("/items/{itemId:guid}/conversions");
        conversions.MapGet(string.Empty, async (Guid itemId, ItemConversionService service, CancellationToken ct) => (await service.ListAsync(itemId, ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("ListItemConversions").Produces<IReadOnlyList<ConversionDetail>>();
        conversions.MapPost(string.Empty, async (Guid itemId, ConversionInput input, ItemConversionService service, CancellationToken ct) =>
                (await service.CreateAsync(itemId, input, ct)).ToHttpResult(saved => Results.Created($"/api/v1/inventory/items/{itemId}/conversions/{saved.Id}", saved)))
            .RequirePermission("inventory.edit").WithName("CreateItemConversion").Produces<ConversionDetail>(StatusCodes.Status201Created);
        conversions.MapPut("/{conversionId:guid}", async (Guid itemId, Guid conversionId, ConversionInput input, ItemConversionService service, CancellationToken ct) =>
                (await service.UpdateAsync(itemId, conversionId, input, ct)).ToHttpResult())
            .RequirePermission("inventory.edit").WithName("UpdateItemConversion").Produces<ConversionDetail>();
        conversions.MapDelete("/{conversionId:guid}", async (Guid itemId, Guid conversionId, ItemConversionService service, CancellationToken ct) =>
                (await service.DeleteAsync(itemId, conversionId, ct)).ToHttpResult())
            .RequirePermission("inventory.edit").WithName("DeleteItemConversion");

        var codes = inventory.MapGroup("/items/{itemId:guid}/supplier-codes");
        codes.MapGet(string.Empty, async (Guid itemId, ItemSupplierCodeService service, CancellationToken ct) => (await service.ListAsync(itemId, ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("ListItemSupplierCodes").Produces<IReadOnlyList<SupplierCodeDetail>>();
        codes.MapPost(string.Empty, async (Guid itemId, SupplierCodeInput input, ItemSupplierCodeService service, CancellationToken ct) =>
                (await service.CreateAsync(itemId, input, ct)).ToHttpResult(saved => Results.Created($"/api/v1/inventory/items/{itemId}/supplier-codes/{saved.Id}", saved)))
            .RequirePermission("inventory.edit").WithName("CreateItemSupplierCode").Produces<SupplierCodeDetail>(StatusCodes.Status201Created);
        codes.MapPut("/{codeId:guid}", async (Guid itemId, Guid codeId, SupplierCodeInput input, ItemSupplierCodeService service, CancellationToken ct) =>
                (await service.UpdateAsync(itemId, codeId, input, ct)).ToHttpResult())
            .RequirePermission("inventory.edit").WithName("UpdateItemSupplierCode").Produces<SupplierCodeDetail>();
        codes.MapDelete("/{codeId:guid}", async (Guid itemId, Guid codeId, ItemSupplierCodeService service, CancellationToken ct) =>
                (await service.DeleteAsync(itemId, codeId, ct)).ToHttpResult())
            .RequirePermission("inventory.edit").WithName("DeleteItemSupplierCode");
    }
}
