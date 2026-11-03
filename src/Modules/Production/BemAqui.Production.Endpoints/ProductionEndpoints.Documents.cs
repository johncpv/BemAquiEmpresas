using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Production.Application;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Production.Endpoints;

internal static partial class ProductionEndpoints
{
    /// <summary>
    /// PDFs e exportação da produção (EXP-04 CA 7, 8, 14 e 15), com a permissão da tela (<c>production.view</c>): OS de instalação, termo de
    /// aceite técnico (409 na OS cancelada) e NC (custo só com <c>projects.view_cost</c>, no serviço); xlsx das listas de OS de fabricação, OS
    /// de instalação e NC com os mesmos filtros do <c>GET</c> da lista. Teto, auditoria e <c>no-store</c> no <see cref="ListExportService"/>.
    /// </summary>
    private static void MapDocuments(RouteGroupBuilder production)
    {
        production.MapGet("/installations/{id:guid}/pdf", async (Guid id, HttpContext http, InstallationDocumentService service, CancellationToken ct) =>
                Pdf(http, await service.PdfAsync(id, ct)))
            .RequirePermission("production.view").WithName("DownloadInstallationOrderPdf").Produces(StatusCodes.Status200OK, contentType: "application/pdf");
        production.MapGet("/installations/{id:guid}/acceptance-term/pdf", async (Guid id, HttpContext http, InstallationDocumentService service, CancellationToken ct) =>
                Pdf(http, await service.AcceptanceTermAsync(id, ct)))
            .RequirePermission("production.view").WithName("DownloadAcceptanceTermPdf").Produces(StatusCodes.Status200OK, contentType: "application/pdf");
        production.MapGet("/non-conformities/{id:guid}/pdf", async (Guid id, HttpContext http, NonConformityDocumentService service, CancellationToken ct) =>
                Pdf(http, await service.PdfAsync(id, ct)))
            .RequirePermission("production.view").WithName("DownloadNonConformityPdf").Produces(StatusCodes.Status200OK, contentType: "application/pdf");

        production.MapGet("/production-orders/export", (string? format, Guid? projectId, Guid? partyId, string? line, string? situation, bool? late, HttpContext http,
                ProductionExportService service, ListExportService export, CancellationToken ct) =>
                export.ExportAsync(http, format, (max, c) => service.FabricationOrdersAsync(new FabricationListQuery(projectId, partyId, line, situation, late == true), max, c), ct))
            .RequirePermission("production.view").WithName("ExportFabricationOrders");
        production.MapGet("/installations/export", (string? format, string? situation, Guid? projectId, HttpContext http, ProductionExportService service, ListExportService export,
                CancellationToken ct) => export.ExportAsync(http, format, (max, c) => service.InstallationsAsync(situation, projectId, max, c), ct))
            .RequirePermission("production.view").WithName("ExportInstallationOrders");
        production.MapGet("/non-conformities/export", (string? format, string? filter, string? situation, Guid? orderId, Guid? projectId, HttpContext http, ProductionExportService service,
                ListExportService export, CancellationToken ct) =>
                export.ExportAsync(http, format, (max, c) => service.NonConformitiesAsync(new NonConformityListQuery(filter, situation, orderId, projectId), max, c), ct))
            .RequirePermission("production.view").WithName("ExportNonConformities");
    }

    private static IResult Pdf(HttpContext http, Result<(byte[] Content, string FileName)> result)
    {
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
        return Results.File(result.Value.Content, "application/pdf", result.Value.FileName);
    }
}
