using BemAqui.BuildingBlocks.Web;
using BemAqui.Commercial.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Commercial.Endpoints;

internal static partial class CommercialEndpoints
{
    /// <summary>Revisões, prorrogação e comparação (COM-02).</summary>
    private static void MapRevisions(RouteGroupBuilder quotes)
    {
        quotes.MapPost("/{id:guid}/revisions", async (Guid id, VersionInput input, QuoteService service, CancellationToken ct) =>
                (await service.NewRevisionAsync(id, input, ct)).ToHttpResult(saved => Results.Created("/api/v1/quotes/" + saved.Id, saved)))
            .RequirePermission("quotes.edit").WithName("NewQuoteRevision").Produces<QuoteDetail>(StatusCodes.Status201Created);
        quotes.MapGet("/{id:guid}/revisions", async (Guid id, QuoteService service, CancellationToken ct) => (await service.RevisionsAsync(id, ct)).ToHttpResult())
            .RequirePermission("quotes.view").WithName("ListQuoteRevisions").Produces<IReadOnlyList<QuoteRevisionSummary>>();
        quotes.MapGet("/{id:guid}/revisions/{revisionId:guid}", async (Guid id, Guid revisionId, QuoteService service, CancellationToken ct) => (await service.RevisionAsync(id, revisionId, ct)).ToHttpResult())
            .RequirePermission("quotes.view").WithName("GetQuoteRevision").Produces<QuoteDetail>();
        quotes.MapPost("/{id:guid}/extend-validity", async (Guid id, ExtendValidityInput input, QuoteService service, CancellationToken ct) => (await service.ExtendValidityAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("quotes.edit").WithName("ExtendQuoteValidity").Produces<QuoteDetail>();
    }
}
