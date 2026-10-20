using BemAqui.BuildingBlocks.Web;
using BemAqui.Commercial.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Commercial.Endpoints;

internal static partial class CommercialEndpoints
{
    /// <summary>Itens e composição (CA 7 a 15); os filhos conferem o pai (IDOR, CA 20).</summary>
    private static void MapItems(RouteGroupBuilder quotes)
    {
        quotes.MapPost("/{id:guid}/items", async (Guid id, QuoteItemInput input, QuoteService service, CancellationToken ct) => (await service.AddItemAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("quotes.edit").WithName("AddQuoteItem").Produces<QuoteDetail>();
        quotes.MapPut("/{id:guid}/items/{itemId:guid}", async (Guid id, Guid itemId, QuoteItemInput input, QuoteService service, CancellationToken ct) =>
                (await service.UpdateItemAsync(id, itemId, input, ct)).ToHttpResult())
            .RequirePermission("quotes.edit").WithName("UpdateQuoteItem").Produces<QuoteDetail>();
        quotes.MapDelete("/{id:guid}/items/{itemId:guid}", async (Guid id, Guid itemId, uint version, QuoteService service, CancellationToken ct) =>
                (await service.RemoveItemAsync(id, itemId, version, ct)).ToHttpResult())
            .RequirePermission("quotes.edit").WithName("RemoveQuoteItem").Produces<QuoteDetail>();
        quotes.MapGet("/{id:guid}/costs", async (Guid id, QuoteService service, CancellationToken ct) => (await service.CostsAsync(id, ct)).ToHttpResult())
            .RequirePermission("quotes.view_cost").WithName("GetQuoteCosts").Produces<QuoteCostDetail>();
        quotes.MapPut("/{id:guid}/items/{itemId:guid}/costs", async (Guid id, Guid itemId, QuoteCostsInput input, QuoteService service, CancellationToken ct) =>
                (await service.SetCostsAsync(id, itemId, input, ct)).ToHttpResult())
            .RequirePermission("quotes.edit").WithName("SetQuoteItemCosts").Produces<QuoteCostDetail>();
        quotes.MapGet("/{id:guid}/refresh-costs", async (Guid id, QuoteService service, CancellationToken ct) => (await service.RefreshPreviewAsync(id, ct)).ToHttpResult())
            .RequirePermission("quotes.view_cost").WithName("PreviewQuoteCostRefresh").Produces<CostRefreshPreview>();
        quotes.MapPost("/{id:guid}/refresh-costs", async (Guid id, VersionInput input, QuoteService service, CancellationToken ct) => (await service.RefreshCostsAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("quotes.edit").WithName("RefreshQuoteCosts").Produces<QuoteCostDetail>();
        quotes.MapGet("/material-cost/{itemId:guid}", async (Guid itemId, QuoteService service, CancellationToken ct) => Results.Ok(new MaterialCost(await service.LastMaterialCostAsync(itemId, ct))))
            .RequirePermission("quotes.view_cost").WithName("GetLastMaterialCost").Produces<MaterialCost>();
    }
}
