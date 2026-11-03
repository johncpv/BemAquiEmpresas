using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Commercial.Application.Documents;
using BemAqui.Commercial.Application.Exports;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Commercial.Endpoints;

/// <summary>
/// PDF do contrato e do aditivo e Excel das listas do comercial (EXP-04, ADR 0045): a mesma permissão da tela; as listas com os mesmos filtros do
/// <c>GET</c> e as colunas de custo, margem e recebido omitidas sem a ação. Teto, auditoria e <c>no-store</c> no <see cref="ListExportService"/>.
/// </summary>
internal static partial class CommercialEndpoints
{
    private static void MapQuoteExports(RouteGroupBuilder quotes) =>
        quotes.MapGet("/export", (string? format, string? q, string? situation, Guid? partyId, DateOnly? from, DateOnly? to, Guid? authorId, HttpContext http,
                CommercialExportService service, ListExportService export, CancellationToken ct) =>
                export.ExportAsync(http, format, (max, c) => service.QuotesAsync(q, situation, partyId, from, to, authorId, max, c), ct))
            .RequirePermission("quotes.view").WithName("ExportQuotes");

    private static void MapContractDocuments(RouteGroupBuilder contracts, RouteGroupBuilder projects)
    {
        contracts.MapGet("/export", (string? format, string? q, string? situation, HttpContext http, CommercialExportService service, ListExportService export, CancellationToken ct) =>
                export.ExportAsync(http, format, (max, c) => service.ContractsAsync(q, situation, max, c), ct))
            .RequirePermission("projects.view").WithName("ExportContracts");
        // EXP-04 CA 4 e 6: resumo do contrato, sem custo nem margem para ninguém.
        contracts.MapGet("/{id:guid}/pdf", async (Guid id, HttpContext http, ContractDocumentService service, CancellationToken ct) =>
                File(http, await service.ContractPdfAsync(id, ct)))
            .RequirePermission("projects.view").WithName("DownloadContractPdf").Produces(StatusCodes.Status200OK, contentType: "application/pdf");
        // EXP-04 CA 5: aditivo de outro contrato é 404 (IDOR).
        contracts.MapGet("/{contractId:guid}/amendments/{id:guid}/pdf", async (Guid contractId, Guid id, HttpContext http, ContractDocumentService service, CancellationToken ct) =>
                File(http, await service.AmendmentPdfAsync(contractId, id, ct)))
            .RequirePermission("projects.view").WithName("DownloadAmendmentPdf").Produces(StatusCodes.Status200OK, contentType: "application/pdf");
        projects.MapGet("/export", (string? format, string? q, string? situation, Guid? partyId, Guid? managerId, HttpContext http, CommercialExportService service,
                ListExportService export, CancellationToken ct) => export.ExportAsync(http, format, (max, c) => service.ProjectsAsync(q, situation, partyId, managerId, max, c), ct))
            .RequirePermission("projects.view").WithName("ExportProjects");
    }

    private static IResult File(HttpContext http, Result<(byte[] Content, string FileName)> result)
    {
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
        return Results.File(result.Value.Content, "application/pdf", result.Value.FileName);
    }
}
