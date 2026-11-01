using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Fiscal.Application.Invoices;
using BemAqui.Fiscal.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Fiscal.Endpoints;

/// <summary>
/// Fiscal → Notas de saída (FIS-03a CA 18): o módulo <c>fiscal</c> liberado (403 <c>module_not_released</c>); ver, a lista, o item (pelo id da
/// nota, IDOR), o histórico e os arquivos com <c>fiscal.view</c>; criar (com <c>Idempotency-Key</c>), conferir, pré-visualizar, emitir,
/// reenviar, a fila "A emitir" e o "Faturar" com <c>fiscal.create</c>; salvar, corrigir e descartar o rascunho com <c>fiscal.edit</c>.
/// Nenhuma rota DELETE (CA 20). XML e DANFE só daqui, conferindo que o anexo é da nota (CA 18). Respostas sem cache.
/// </summary>
internal static class FiscalOutgoingInvoiceEndpoints
{
    private static readonly string[] HistoryChildren = [nameof(OutgoingInvoiceItem)];

    public static IEndpointRouteBuilder MapFiscalOutgoingInvoiceEndpoints(this IEndpointRouteBuilder api)
    {
        var invoices = api.MapGroup("/fiscal/outgoing-invoices").WithTags("Fiscal").RequireModule("fiscal")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });

        invoices.MapGet(string.Empty, async (DateOnly? from, DateOnly? to, OutgoingInvoiceStatus? status, Guid? partyId, Guid? projectId, int? page, int? pageSize, OutgoingInvoiceService service,
                CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new OutgoingInvoiceQuery(from, to, status, partyId, projectId), new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)))
            .RequirePermission("fiscal.view").WithName("ListOutgoingInvoices").Produces<OutgoingInvoicePage>();
        invoices.MapGet("/to-issue", async (OutgoingInvoiceService service, CancellationToken ct) => Results.Ok(await service.ToIssueAsync(ct)))
            .RequirePermission("fiscal.create").WithName("ListInvoicesToIssue").Produces<IReadOnlyList<InvoiceQueueRow>>();
        invoices.MapGet("/billing", async (Guid projectId, OutgoingInvoiceService service, CancellationToken ct) => (await service.BillingAsync(projectId, ct)).ToHttpResult())
            .RequirePermission("fiscal.create").WithName("GetProjectBilling").Produces<ProjectBillingView>();
        invoices.MapPost(string.Empty, async (CreateOutgoingInvoiceRequest request, OutgoingInvoiceService service, CancellationToken ct) =>
                (await service.CreateAsync(request, ct)).ToHttpResult(view => Results.Created("/api/v1/fiscal/outgoing-invoices/" + view.Id, view)))
            .RequirePermission("fiscal.create").RequireIdempotencyKey().WithName("CreateOutgoingInvoice").Produces<OutgoingInvoiceView>(StatusCodes.Status201Created);
        invoices.MapGet("/{id:guid}", async (Guid id, OutgoingInvoiceService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("fiscal.view").WithName("GetOutgoingInvoice").Produces<OutgoingInvoiceView>();
        invoices.MapPut("/{id:guid}", async (Guid id, UpdateOutgoingInvoiceRequest request, OutgoingInvoiceService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, ct)).ToHttpResult())
            .RequirePermission("fiscal.edit").WithName("UpdateOutgoingInvoice").Produces<OutgoingInvoiceView>();
        invoices.MapGet("/{id:guid}/items/{itemId:guid}", async (Guid id, Guid itemId, OutgoingInvoiceService service, CancellationToken ct) =>
                (await service.ItemAsync(id, itemId, ct)).ToHttpResult())
            .RequirePermission("fiscal.view").WithName("GetOutgoingInvoiceItem").Produces<OutgoingInvoiceItemView>();
        invoices.MapGet("/{id:guid}/issue-check", async (Guid id, OutgoingInvoiceService service, CancellationToken ct) => (await service.IssueCheckAsync(id, ct)).ToHttpResult())
            .RequirePermission("fiscal.create").WithName("CheckOutgoingInvoiceIssue").Produces<IssueCheckView>();
        invoices.MapGet("/{id:guid}/preview", async (Guid id, OutgoingInvoiceService service, HttpContext http, CancellationToken ct) =>
            {
                var result = await service.PreviewAsync(id, ct);
                if (result.IsFailure)
                {
                    return result.Error!.ToProblem();
                }

                AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
                return Results.File(result.Value, "application/pdf", "previa-nfe.pdf");
            })
            .RequirePermission("fiscal.create").WithName("PreviewOutgoingInvoice");
        invoices.MapPost("/{id:guid}/issue", async (Guid id, OutgoingInvoiceVersionRequest request, OutgoingInvoiceService service, CancellationToken ct) =>
                (await service.IssueAsync(id, request, ct)).ToHttpResult(result => result.Status == OutgoingInvoiceStatus.Authorized ? Results.Ok(result) : Results.Accepted(null, result)))
            .RequirePermission("fiscal.create").WithName("IssueOutgoingInvoice").Produces<IssueResultView>().Produces<IssueResultView>(StatusCodes.Status202Accepted);
        invoices.MapPost("/{id:guid}/reopen", async (Guid id, OutgoingInvoiceVersionRequest request, OutgoingInvoiceService service, CancellationToken ct) =>
                (await service.ReopenAsync(id, request, ct)).ToHttpResult())
            .RequirePermission("fiscal.edit").WithName("ReopenOutgoingInvoice").Produces<OutgoingInvoiceView>();
        invoices.MapPost("/{id:guid}/discard", async (Guid id, OutgoingInvoiceVersionRequest request, OutgoingInvoiceService service, CancellationToken ct) =>
                (await service.DiscardAsync(id, request, ct)).ToHttpResult())
            .RequirePermission("fiscal.edit").WithName("DiscardOutgoingInvoice").Produces<OutgoingInvoiceView>();
        invoices.MapPost("/{id:guid}/resend", async (Guid id, OutgoingInvoiceService service, CancellationToken ct) => (await service.ResendAsync(id, ct)).ToHttpResult())
            .RequirePermission("fiscal.create").WithName("ResendOutgoingInvoice").Produces<OutgoingInvoiceView>();
        invoices.MapGet("/{id:guid}/xml", (Guid id, OutgoingInvoiceService service, HttpContext http, CancellationToken ct) => FileAsync(id, true, service, http, ct))
            .RequirePermission("fiscal.view").WithName("DownloadOutgoingInvoiceXml");
        invoices.MapGet("/{id:guid}/danfe", (Guid id, OutgoingInvoiceService service, HttpContext http, CancellationToken ct) => FileAsync(id, false, service, http, ct))
            .RequirePermission("fiscal.view").WithName("DownloadOutgoingInvoiceDanfe");
        invoices.MapGet("/{id:guid}/history", async (Guid id, int? page, int? pageSize, AuditHistory history, CancellationToken ct) =>
                (await history.GetWithChildrenAsync(nameof(OutgoingInvoice), id.ToString(), HistoryChildren, "invoiceId",
                    new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)).ToHttpResult())
            .RequirePermission("fiscal.view").WithName("GetOutgoingInvoiceHistory").Produces<PagedList<RelatedAuditEntryDto>>();
        return api;
    }

    private static async Task<IResult> FileAsync(Guid id, bool xml, OutgoingInvoiceService service, HttpContext http, CancellationToken ct)
    {
        var result = await service.FileAsync(id, xml, ct);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
        return Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }
}
