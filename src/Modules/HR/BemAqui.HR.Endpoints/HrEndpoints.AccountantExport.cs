using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Web;
using BemAqui.HR.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.HR.Endpoints;

internal static partial class HrEndpoints
{
    /// <summary>
    /// Exportação para a contadora (RH-03): a tela e o pacote, os dois com <c>hr.view_sensitive</c> (CA 8). O pacote sai como download
    /// (<c>attachment</c>, <c>nosniff</c>, <c>private, no-store</c>) e nunca é gravado; a geração vai para a auditoria (CA 5).
    /// </summary>
    private static void MapAccountantExports(RouteGroupBuilder exports)
    {
        exports.MapGet(string.Empty, async (string? period, AccountantExportService service, CancellationToken ct) => (await service.OverviewAsync(period, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("GetAccountantExportOverview").Produces<AccountantExportOverview>();
        exports.MapGet("/package", async (string? period, AccountantExportService service, HttpContext http, CancellationToken ct) =>
            {
                var result = await service.PackageAsync(period, ct);
                if (result.IsFailure)
                {
                    return result.Error!.ToProblem();
                }

                AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
                return Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
            })
            .RequirePermission("hr.view_sensitive").WithName("DownloadAccountantExport").Produces(StatusCodes.Status200OK, contentType: AccountantExportService.ContentType);
    }
}
