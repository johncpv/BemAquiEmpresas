using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Purchasing.Application;
using BemAqui.Purchasing.Application.Documents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Purchasing.Endpoints;

/// <summary>
/// PDF e envio do pedido ao fornecedor (CMP-03b CA 1 a 4, 10): baixar o PDF e ver os documentos com <c>purchasing.view</c>; o diálogo,
/// enviar e reenviar com <c>purchasing.edit</c> (enviar com <c>Idempotency-Key</c>). O envio é lido pelo pedido da URL (IDOR).
/// </summary>
internal static partial class PurchasingEndpoints
{
    private static void MapOrderDocuments(RouteGroupBuilder orders)
    {
        orders.MapGet("/{id:guid}/pdf", async (Guid id, HttpContext http, PurchaseOrderDispatchService service, CancellationToken ct) =>
            {
                var result = await service.PdfAsync(id, ct);
                if (result.IsFailure)
                {
                    return result.Error!.ToProblem();
                }

                AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
                return Results.File(result.Value.Content, "application/pdf", result.Value.FileName);
            })
            .RequirePermission("purchasing.view").WithName("DownloadPurchaseOrderPdf").Produces(StatusCodes.Status200OK, contentType: "application/pdf");
        orders.MapGet("/{id:guid}/documents", async (Guid id, PurchaseOrderDispatchService service, CancellationToken ct) => (await service.DocumentsAsync(id, ct)).ToHttpResult())
            .RequirePermission("purchasing.view").WithName("GetPurchaseOrderDocuments").Produces<PurchaseOrderDocuments>();
        orders.MapGet("/{id:guid}/dispatches/{dispatchId:guid}", async (Guid id, Guid dispatchId, PurchaseOrderDispatchService service, CancellationToken ct) =>
                (await service.DispatchAsync(id, dispatchId, ct)).ToHttpResult())
            .RequirePermission("purchasing.view").WithName("GetPurchaseOrderDispatch").Produces<PurchaseOrderDispatchView>();
        orders.MapGet("/{id:guid}/dispatch-draft", async (Guid id, PurchaseOrderDispatchService service, CancellationToken ct) => (await service.DraftAsync(id, ct)).ToHttpResult())
            .RequirePermission("purchasing.edit").WithName("GetPurchaseOrderDispatchDraft").Produces<PurchaseOrderDispatchDraft>();
        orders.MapPost("/{id:guid}/dispatches", async (Guid id, PurchaseOrderDispatchInput input, PurchaseOrderDispatchService service, CancellationToken ct) =>
                (await service.SendAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("purchasing.edit").RequireIdempotencyKey().WithName("DispatchPurchaseOrder").Produces<PurchaseOrderDetail>();
    }
}
