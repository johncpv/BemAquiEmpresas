using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Integration;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Fiscal.Application.Invoices;
using BemAqui.Fiscal.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Fiscal.Endpoints;

/// <summary>
/// <c>GET /api/v1/integration/invoices/{id}/danfe</c> com <c>fiscal.view</c> (INT-03 CA 18): só nota de produção, não descartada e de um terceiro
/// (destinatário); o resto responde 404 igual (CA 20). A leitura fica na auditoria (CA 23) com a origem do aplicativo.
/// </summary>
internal static class IntegrationInvoiceEndpoints
{
    public static void MapIntegrationInvoices(this IEndpointRouteBuilder api)
    {
        var invoices = api.MapIntegrationGroup().MapGroup("/invoices");
        invoices.MapGet("/{id:guid}/danfe", async (Guid id, IOutgoingInvoices lookup, OutgoingInvoiceService service, IAuditWriter audit, HttpContext http, CancellationToken ct) =>
            {
                if (await lookup.FindProductionAsync(id, ct) is not { PartyId: not null })
                {
                    return IntegrationApi.NotFound();
                }

                var file = await service.FileAsync(id, false, ct);
                if (file.IsFailure)
                {
                    return file.Error!.Type == SharedKernel.ErrorType.NotFound ? IntegrationApi.NotFound() : file.Error.ToProblem();
                }

                await audit.RecordReadAsync("OutgoingInvoice", id.ToString(), "danfe", ct);
                AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
                return Results.File(file.Value.Content, file.Value.ContentType, file.Value.FileName);
            })
            .RequirePermission("fiscal.view").WithName("IntegrationDownloadInvoiceDanfe").Produces(StatusCodes.Status200OK, contentType: "application/pdf");
    }
}
