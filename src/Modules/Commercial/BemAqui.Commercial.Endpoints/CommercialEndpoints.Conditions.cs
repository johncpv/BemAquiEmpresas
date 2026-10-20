using BemAqui.BuildingBlocks.Web;
using BemAqui.Commercial.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Commercial.Endpoints;

internal static partial class CommercialEndpoints
{
    /// <summary>Condições, validade, perda e cópia (COM-01b).</summary>
    private static void MapConditions(RouteGroupBuilder quotes)
    {
        quotes.MapGet("/{id:guid}/conditions", async (Guid id, QuoteService service, CancellationToken ct) => (await service.ConditionsAsync(id, ct)).ToHttpResult())
            .RequirePermission("quotes.view").WithName("GetQuoteConditions").Produces<QuoteConditions>();
        quotes.MapPut("/{id:guid}/conditions", async (Guid id, QuoteConditionsInput input, QuoteService service, CancellationToken ct) => (await service.SaveConditionsAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("quotes.edit").WithName("SaveQuoteConditions").Produces<QuoteConditions>();
        quotes.MapGet("/{id:guid}/conditions/from-term/{termId:guid}", async (Guid id, Guid termId, QuoteService service, CancellationToken ct) => (await service.FromPaymentTermAsync(id, termId, ct)).ToHttpResult())
            .RequirePermission("quotes.edit").WithName("QuoteMilestonesFromTerm").Produces<IReadOnlyList<QuoteMilestoneInput>>();
        quotes.MapPost("/{id:guid}/lose", async (Guid id, QuoteLoseInput input, QuoteService service, CancellationToken ct) => (await service.LoseAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("quotes.edit").WithName("LoseQuote").Produces<QuoteDetail>();
        quotes.MapGet("/copy-options", async (string? q, QuoteService service, CancellationToken ct) => Results.Ok(await service.CopyOptionsAsync(q, ct)))
            .RequirePermission("quotes.create").WithName("ListQuoteCopyOptions").Produces<IReadOnlyList<QuoteCopyOption>>();
        quotes.MapPost("/{id:guid}/copy", async (Guid id, QuoteCopyInput input, QuoteService service, CancellationToken ct) =>
                (await service.CopyAsync(id, input, ct)).ToHttpResult(saved => Results.Created("/api/v1/quotes/" + saved.Id, saved)))
            .RequirePermission("quotes.create").RequireIdempotencyKey().WithName("CopyQuote").Produces<QuoteDetail>(StatusCodes.Status201Created);
        quotes.MapPost("/{id:guid}/copy-items", async (Guid id, QuoteCopyItemsInput input, QuoteService service, CancellationToken ct) => (await service.CopyItemsAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("quotes.edit").WithName("CopyQuoteItems").Produces<QuoteDetail>();
        quotes.MapGet("/payment-terms", async (QuoteLookups service, CancellationToken ct) => Results.Ok(await service.PaymentTermsAsync(ct)))
            .RequirePermission("quotes.view").WithName("ListQuotePaymentTerms").Produces<IReadOnlyList<QuoteTermOption>>();
    }
}
