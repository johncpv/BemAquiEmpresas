using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Time;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Application.Documents;
using BemAqui.Finance.Application.Exports;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Finance.Endpoints;

/// <summary>
/// EXP-05: ficha do título a receber e a pagar em PDF (<c>finance.view</c>, mesma leitura da tela, 404 para id de outro título) e a planilha
/// do plano de contas, dos centros de custo e dos descontos e cancelamentos, com os mesmos filtros da tela.
/// </summary>
internal static partial class FinanceEndpoints
{
    private static void MapTitleDocuments(RouteGroupBuilder finance)
    {
        finance.MapGet("/receivables/{id:guid}/pdf", async (Guid id, HttpContext http, TitleDocumentService service, CancellationToken ct) =>
                PdfFile(http, await service.ReceivablePdfAsync(id, ct)))
            .RequirePermission("finance.view").WithName("DownloadReceivablePdf").Produces(StatusCodes.Status200OK, contentType: "application/pdf").ProducesProblem(StatusCodes.Status404NotFound);
        finance.MapGet("/payables/{id:guid}/pdf", async (Guid id, HttpContext http, TitleDocumentService service, CancellationToken ct) =>
                PdfFile(http, await service.PayablePdfAsync(id, ct)))
            .RequirePermission("finance.view").WithName("DownloadPayablePdf").Produces(StatusCodes.Status200OK, contentType: "application/pdf").ProducesProblem(StatusCodes.Status404NotFound);

        finance.MapGet("/chart-of-accounts/export", (string? format, bool? inactive, HttpContext http, RegistryExportService service, ListExportService export, CancellationToken ct) =>
                export.ExportAsync(http, format, async (_, c) => Result<ExportData<AccountExportRow>>.Success(await service.AccountsAsync(inactive == true, c)), ct))
            .RequirePermission("finance.view").WithName("ExportAccounts");
        finance.MapGet("/cost-centers/export", (string? format, bool? inactive, HttpContext http, RegistryExportService service, ListExportService export, CancellationToken ct) =>
                export.ExportAsync(http, format, async (_, c) => Result<ExportData<CostCenterExportRow>>.Success(await service.CostCentersAsync(inactive == true, c)), ct))
            .RequirePermission("finance.view").WithName("ExportCostCenters");
        finance.MapGet("/receivables/discounts-and-cancellations/export", (string? format, DateOnly? from, DateOnly? to, HttpContext http, TimeProvider clock, RegistryExportService service,
                ListExportService export, CancellationToken ct) =>
            {
                var today = BusinessTime.Today(clock);
                var start = from ?? new DateOnly(today.Year, today.Month, 1);
                var end = to ?? start.AddMonths(1).AddDays(-1);
                return export.ExportAsync(http, format, async (_, c) => Result<ExportData<DiscountExportRow>>.Success(await service.DiscountsAsync(start, end, c)), ct);
            })
            .RequirePermission("finance.view").WithName("ExportDiscountsAndCancellations");
    }

    private static IResult PdfFile(HttpContext http, Result<(byte[] Content, string FileName)> result)
    {
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
        return Results.File(result.Value.Content, "application/pdf", result.Value.FileName);
    }
}
