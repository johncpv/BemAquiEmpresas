using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Catalog.Application;
using BemAqui.Catalog.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Catalog.Endpoints;

/// <summary>
/// Catálogo de produtos (CAT-01): módulo <c>catalog</c> (403 <c>module_not_released</c>; oculto fora dos perfis de comércio) e as ações
/// <c>view</c>, <c>create</c>, <c>edit</c>, <c>delete</c>, <c>edit_price</c> (rota do preço) e <c>view_cost</c> (conferida no serviço: sem ela,
/// custo e margem ficam ausentes da resposta). Respostas sem cache.
/// </summary>
internal static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder api)
    {
        var root = api.MapGroup("/catalog").WithTags("Catálogo").RequireModule(ProductService.Module)
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });
        MapProducts(root.MapGroup("/products"));
        MapCategories(root.MapGroup("/categories"));
        root.MapGet("/price-lists", async (PriceService service, CancellationToken ct) => Results.Ok(await service.ListsAsync(ct)))
            .RequirePermission("catalog.view").WithName("ListPriceLists").Produces<IReadOnlyList<PriceListView>>();
        root.MapGet("/lookup", async (string? code, string? q, ICatalogLookup lookup, CancellationToken ct) => Results.Ok(await LookupAsync(code, q, lookup, ct)))
            .RequirePermission("catalog.view").WithName("LookupCatalog").Produces<IReadOnlyList<LookupView>>();
        root.MapGet("/units", async (ProductService service, CancellationToken ct) => Results.Ok(await service.UnitChoicesAsync(ct)))
            .RequirePermission("catalog.view").WithName("ListCatalogUnits").Produces<IReadOnlyList<UnitChoiceView>>();
        root.MapGet("/items", async (string? q, ProductService service, CancellationToken ct) => Results.Ok(await service.ItemChoicesAsync(q, ct)))
            .RequirePermission("catalog.view").WithName("SearchCatalogItems").Produces<IReadOnlyList<ItemChoice>>();
        return api;
    }

    private static void MapProducts(RouteGroupBuilder products)
    {
        products.MapGet(string.Empty, async (string? q, Guid? categoryId, bool? includeInactive, bool? withoutPrice, int? page, int? pageSize, string? sort, ProductService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new ProductListQuery(q, categoryId, includeInactive == true, withoutPrice == true),
                    new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize, sort).Normalize(), ct)))
            .RequirePermission("catalog.view").WithName("ListProducts").Produces<PagedList<ProductListItem>>();
        products.MapGet("/export", (string? format, string? q, Guid? categoryId, bool? includeInactive, bool? withoutPrice, string? sort, HttpContext http,
                ProductExportService service, ListExportService export, CancellationToken ct) =>
                export.ExportAsync(http, format, (max, c) => service.ExportAsync(new ProductListQuery(q, categoryId, includeInactive == true, withoutPrice == true), sort, max, c), ct))
            .RequirePermission("catalog.view").WithName("ExportProducts");
        products.MapGet("/{id:guid}", async (Guid id, ProductService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("catalog.view").WithName("GetProduct").Produces<ProductDetail>();
        products.MapPost(string.Empty, async (ProductCreateInput input, ProductService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/catalog/products/" + saved.Id, saved)))
            .RequirePermission("catalog.create").WithName("CreateProduct").Produces<ProductDetail>(StatusCodes.Status201Created);
        products.MapPut("/{id:guid}", async (Guid id, ProductUpdateInput input, ProductService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("catalog.edit").WithName("UpdateProduct").Produces<ProductDetail>();
        products.MapPost("/{id:guid}/inactivate", async (Guid id, ProductService service, CancellationToken ct) => (await service.SetActiveAsync(id, false, ct)).ToHttpResult())
            .RequirePermission("catalog.edit").WithName("InactivateProduct").Produces<ProductDetail>();
        products.MapPost("/{id:guid}/reactivate", async (Guid id, ProductService service, CancellationToken ct) => (await service.SetActiveAsync(id, true, ct)).ToHttpResult())
            .RequirePermission("catalog.edit").WithName("ReactivateProduct").Produces<ProductDetail>();
        products.MapDelete("/{id:guid}", async (Guid id, ProductService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission("catalog.delete").WithName("DeleteProduct");

        // Preço (CA 8 e 9): só com catalog.edit_price; o histórico é leitura de quem vê o produto.
        products.MapPut("/{id:guid}/price", async (Guid id, PriceInput input, PriceService prices, ProductService service, CancellationToken ct) =>
            {
                var set = await prices.SetAsync(id, input, ct);
                return set.IsFailure ? set.Error!.ToProblem() : (await service.GetAsync(id, ct)).ToHttpResult();
            })
            .RequirePermission("catalog.edit_price").WithName("SetProductPrice").Produces<ProductDetail>();
        products.MapGet("/{id:guid}/price-history", async (Guid id, PriceService prices, CancellationToken ct) => (await prices.HistoryAsync(id, ct)).ToHttpResult())
            .RequirePermission("catalog.view").WithName("ListProductPriceHistory").Produces<IReadOnlyList<PriceChangeView>>();

        // Códigos de barras (CA 5 a 7).
        products.MapGet("/{id:guid}/barcodes", async (Guid id, ProductService service, CancellationToken ct) => (await service.BarcodesAsync(id, ct)).ToHttpResult())
            .RequirePermission("catalog.view").WithName("ListProductBarcodes").Produces<IReadOnlyList<BarcodeView>>();
        products.MapPost("/{id:guid}/barcodes", async (Guid id, BarcodeInput input, ProductService service, CancellationToken ct) =>
                (await service.AddBarcodeAsync(id, input, ct)).ToHttpResult(saved => Results.Created($"/api/v1/catalog/products/{id}/barcodes/{saved.Id}", saved)))
            .RequirePermission("catalog.edit").WithName("AddProductBarcode").Produces<BarcodeView>(StatusCodes.Status201Created);
        products.MapDelete("/{id:guid}/barcodes/{barcodeId:guid}", async (Guid id, Guid barcodeId, ProductService service, CancellationToken ct) =>
                (await service.RemoveBarcodeAsync(id, barcodeId, ct)).ToHttpResult())
            .RequirePermission("catalog.edit").WithName("RemoveProductBarcode");

        // Grade (CA 15): criar variações é cadastrar produto.
        products.MapPost("/{id:guid}/variants", async (Guid id, VariantsInput input, ProductService service, CancellationToken ct) => (await service.CreateVariantsAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("catalog.create").WithName("CreateProductVariants").Produces<ProductDetail>();

        // Foto (CA 16): no produto raiz; a variação mostra a do pai.
        products.MapPost("/{id:guid}/photo", async (Guid id, IFormFile file, ProductPhotoService photos, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                return (await photos.UploadAsync(id, file.FileName, content, ct)).ToHttpResult(dto => Results.Created($"/api/v1/catalog/products/{id}/photo/{dto.Id}", dto));
            })
            .RequirePermission("catalog.edit").DisableAntiforgery().WithName("UploadProductPhoto").Produces<AttachmentDto>(StatusCodes.Status201Created);
        products.MapGet("/{id:guid}/photo/{attachmentId:guid}", async (Guid id, Guid attachmentId, ProductPhotoService photos, HttpContext http, CancellationToken ct) =>
            {
                var result = await photos.DownloadAsync(id, attachmentId, ct);
                if (result.IsFailure)
                {
                    return result.Error!.ToProblem();
                }

                AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
                return Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
            })
            .RequirePermission("catalog.view").WithName("DownloadProductPhoto");
    }

    private static void MapCategories(RouteGroupBuilder categories)
    {
        categories.MapGet(string.Empty, async (bool? includeInactive, CategoryService service, CancellationToken ct) => Results.Ok(await service.ListAsync(includeInactive == true, ct)))
            .RequirePermission("catalog.view").WithName("ListProductCategories").Produces<IReadOnlyList<ProductCategoryDetail>>();
        categories.MapPost(string.Empty, async (ProductCategoryInput input, CategoryService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/catalog/categories/" + saved.Id, saved)))
            .RequirePermission("catalog.create").WithName("CreateProductCategory").Produces<ProductCategoryDetail>(StatusCodes.Status201Created);
        categories.MapPut("/{id:guid}", async (Guid id, ProductCategoryInput input, CategoryService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("catalog.edit").WithName("UpdateProductCategory").Produces<ProductCategoryDetail>();
        categories.MapPost("/{id:guid}/inactivate", async (Guid id, CategoryService service, CancellationToken ct) => (await service.SetActiveAsync(id, false, ct)).ToHttpResult())
            .RequirePermission("catalog.edit").WithName("InactivateProductCategory").Produces<ProductCategoryDetail>();
        categories.MapPost("/{id:guid}/reactivate", async (Guid id, CategoryService service, CancellationToken ct) => (await service.SetActiveAsync(id, true, ct)).ToHttpResult())
            .RequirePermission("catalog.edit").WithName("ReactivateProductCategory").Produces<ProductCategoryDetail>();
        categories.MapDelete("/{id:guid}", async (Guid id, CategoryService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission("catalog.delete").WithName("DeleteProductCategory");
    }

    /// <summary>Consulta rápida (CA 12): <c>code</c> procura GTIN e depois PLU; <c>q</c> procura por trecho do nome.</summary>
    private static async Task<IReadOnlyList<LookupView>> LookupAsync(string? code, string? q, ICatalogLookup lookup, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            var hit = await lookup.ByGtinAsync(code, null, ct) ?? await lookup.ByPluAsync(code, null, ct);
            return hit is null ? [] : [View(hit)];
        }

        return string.IsNullOrWhiteSpace(q) ? [] : (await lookup.SearchAsync(q, 20, null, ct)).Select(View).ToList();
    }

    private static LookupView View(CatalogLookupHit hit) =>
        new(hit.Product.ProductId, hit.Product.Description, hit.Gtin, hit.PackQuantity, hit.Match.ToString(), hit.Product.Price, hit.Product.UnitCode);
}
