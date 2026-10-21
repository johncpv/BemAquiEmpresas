using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Commercial.Application.Documents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Commercial.Endpoints;

internal static partial class CommercialEndpoints
{
    /// <summary>PDF, documentos e envio por e-mail (COM-03): gerar e baixar com <c>quotes.view</c>, enviar com <c>quotes.edit</c>.</summary>
    private static void MapDocuments(RouteGroupBuilder quotes)
    {
        quotes.MapGet("/{id:guid}/documents", async (Guid id, QuoteDocumentService service, CancellationToken ct) => (await service.DocumentsAsync(id, ct)).ToHttpResult())
            .RequirePermission("quotes.view").WithName("GetQuoteDocuments").Produces<QuoteDocuments>();
        quotes.MapPost("/{id:guid}/pdf", async (Guid id, QuoteDocumentService service, CancellationToken ct) => (await service.GeneratePdfAsync(id, ct)).ToHttpResult())
            .RequirePermission("quotes.view").WithName("GenerateQuotePdf").Produces<AttachmentDto>();
        quotes.MapGet("/{id:guid}/send-draft", async (Guid id, QuoteDocumentService service, CancellationToken ct) => (await service.SendDraftAsync(id, ct)).ToHttpResult())
            .RequirePermission("quotes.edit").WithName("GetQuoteSendDraft").Produces<QuoteSendDraft>();
        quotes.MapPost("/{id:guid}/email", async (Guid id, QuoteSendInput input, QuoteDocumentService service, CancellationToken ct) => (await service.SendAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("quotes.edit").RequireIdempotencyKey().WithName("EmailQuote").Produces<QuoteDocuments>();
    }
}
