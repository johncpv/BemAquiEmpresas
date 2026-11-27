using System.Text.Json.Serialization;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Integration;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Catalog.Contracts;
using BemAqui.Inventory.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Catalog.Endpoints;

/// <summary>
/// Produto para o aplicativo conectado (INT-03 CA 4 e 6): id, código interno, GTIN, descrição, unidade, preço da tabela padrão e a versão da tabela.
/// <c>Available</c> (físico − reservado no local padrão) só vem com <c>inventory.view</c>. Nunca custo nem margem.
/// </summary>
public sealed record IntegrationProduct(
    Guid Id,
    string? Code,
    string? Gtin,
    string Description,
    string Unit,
    decimal? Price,
    Guid PriceListId,
    long PriceListVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Available);

/// <summary>Página da vitrine (CA 5): <c>items</c>, <c>page</c>, <c>pageSize</c> e <c>total</c>.</summary>
public sealed record IntegrationProductPage(IReadOnlyList<IntegrationProduct> Items, int Page, int PageSize, int Total);

/// <summary>
/// <c>GET /api/v1/integration/products</c> e <c>/products/{id}</c> com <c>catalog.view</c> (INT-03 CA 3 a 6), sobre o <see cref="ICatalogLookup"/> e o
/// <see cref="IStockBalances"/>: a rota não toca em tabela nenhuma.
/// </summary>
internal static class IntegrationCatalogEndpoints
{
    public static void MapIntegrationCatalog(this IEndpointRouteBuilder api)
    {
        var products = api.MapIntegrationGroup().MapGroup("/products");
        products.MapGet(string.Empty, async (string? q, int? page, int? pageSize, ICatalogLookup lookup, IStockBalances balances, ILocations locations, IPermissionChecker permissions,
                CancellationToken ct) =>
            {
                var request = new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize();
                var found = await lookup.ListAsync(q, request.Page, request.PageSize, ct);
                return Results.Ok(new IntegrationProductPage(await ViewsAsync(found.Items, balances, locations, permissions, ct), request.Page, request.PageSize, found.Total));
            })
            .RequirePermission("catalog.view").WithName("IntegrationListProducts").Produces<IntegrationProductPage>();
        products.MapGet("/{id:guid}", async (Guid id, ICatalogLookup lookup, IStockBalances balances, ILocations locations, IPermissionChecker permissions, CancellationToken ct) =>
                await lookup.ListingAsync(id, ct) is { } listing
                    ? Results.Ok((await ViewsAsync([listing], balances, locations, permissions, ct))[0])
                    : IntegrationApi.NotFound())
            .RequirePermission("catalog.view").WithName("IntegrationGetProduct").Produces<IntegrationProduct>();
    }

    private static async Task<IReadOnlyList<IntegrationProduct>> ViewsAsync(IReadOnlyList<CatalogListing> listings, IStockBalances balances, ILocations locations,
        IPermissionChecker permissions, CancellationToken ct)
    {
        Dictionary<Guid, decimal>? available = null;
        if (listings.Count > 0 && await permissions.HasPermissionAsync("inventory", "view", ct))
        {
            var location = (await locations.DefaultAsync(ct))?.Id;
            available = location is null
                ? []
                : (await balances.GetAsync(listings.Select(l => l.Product.ItemId).Distinct().ToList(), location, ct)).GroupBy(a => a.ItemId).ToDictionary(g => g.Key, g => g.Sum(a => a.Available));
        }

        return listings.Select(l => new IntegrationProduct(l.Product.ProductId, l.Code, l.Gtin, l.Product.Description, l.Product.UnitCode, l.Product.Price, l.Product.PriceListId,
            l.Product.PriceListVersion, available is null ? null : available.GetValueOrDefault(l.Product.ItemId))).ToList();
    }
}
