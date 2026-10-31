using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Commercial.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Commercial.Endpoints;

/// <summary>Orçamentos (COM-01a): módulo <c>quotes</c>; custo e margem só nas rotas de custo, com <c>quotes.view_cost</c> (D7).</summary>
internal static partial class CommercialEndpoints
{
    public static IEndpointRouteBuilder MapCommercialEndpoints(this IEndpointRouteBuilder api)
    {
        var quotes = api.MapGroup("/quotes").WithTags("Orçamentos").RequireModule("quotes");
        quotes.MapGet(string.Empty, async (string? q, string? situation, Guid? partyId, DateOnly? from, DateOnly? to, Guid? authorId, int? page, int? pageSize, QuoteService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(q, situation, new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), partyId, from, to, authorId, ct)))
            .RequirePermission("quotes.view").WithName("ListQuotes").Produces<QuotePage>();
        quotes.MapGet("/funnel", async (DateOnly? from, DateOnly? to, Guid? authorId, string? sort, QuoteService service, CancellationToken ct) =>
                (await service.FunnelAsync(from, to, authorId, sort, ct)).ToHttpResult())
            .RequirePermission("quotes.view").WithName("GetQuoteFunnel").Produces<QuoteFunnel>();
        quotes.MapGet("/costs", async (string ids, QuoteService service, CancellationToken ct) =>
                (await service.CostSummariesAsync(ParseIds(ids), ct)).ToHttpResult())
            .RequirePermission("quotes.view_cost").WithName("ListQuoteCosts").Produces<IReadOnlyList<QuoteCostSummary>>();
        quotes.MapGet("/{id:guid}", async (Guid id, QuoteService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("quotes.view").WithName("GetQuote").Produces<QuoteDetail>();
        quotes.MapPost(string.Empty, async (QuoteCreateInput input, QuoteService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/quotes/" + saved.Id, saved)))
            .RequirePermission("quotes.create").RequireIdempotencyKey().WithName("CreateQuote").Produces<QuoteDetail>(StatusCodes.Status201Created);
        quotes.MapPut("/{id:guid}", async (Guid id, QuoteHeaderInput input, QuoteService service, CancellationToken ct) => (await service.UpdateHeaderAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("quotes.edit").WithName("UpdateQuote").Produces<QuoteDetail>();
        quotes.MapPost("/{id:guid}/send", async (Guid id, VersionInput input, QuoteService service, CancellationToken ct) => (await service.SendAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("quotes.edit").WithName("MarkQuoteSent").Produces<QuoteDetail>();
        quotes.MapDelete("/{id:guid}", async (Guid id, uint version, QuoteService service, CancellationToken ct) => (await service.DeleteAsync(id, version, ct)).ToHttpResult())
            .RequirePermission("quotes.delete").WithName("DeleteQuote");
        MapItems(quotes);
        MapConditions(quotes);
        MapRevisions(quotes);
        MapScheduleSimulation(quotes);
        MapApproval(quotes);
        MapDocuments(quotes);
        MapContracts(api);
        MapLookups(quotes.MapGroup("/lookups"));
        var commercial = api.MapGroup("/commercial").WithTags("Orçamentos").RequireModule("settings");
        commercial.MapGet("/parameters", async (CommercialParametersService service, CancellationToken ct) => Results.Ok(await service.GetAsync(ct)))
            .RequirePermission("settings.view").WithName("GetCommercialParameters").Produces<CommercialParametersView>();
        commercial.MapPut("/parameters", async (CommercialParametersInput input, CommercialParametersService service, CancellationToken ct) => (await service.UpdateAsync(input, ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("UpdateCommercialParameters").Produces<CommercialParametersView>();
        return api;
    }

    private static List<Guid> ParseIds(string ids) =>
        ids.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).Take(200).ToList();
}
