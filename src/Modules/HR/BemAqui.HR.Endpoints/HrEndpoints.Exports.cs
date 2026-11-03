using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Web;
using BemAqui.HR.Application;
using BemAqui.HR.Application.Exports;
using BemAqui.HR.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.HR.Endpoints;

/// <summary>
/// Exportação do RH (EXP-05 CA 11 a 15, ADR 0045 §3): <c>GET …/export?format=xlsx</c> com os mesmos filtros e a mesma permissão da tela; CPF,
/// salário e valores por pessoa mascarados sem <c>hr.view_sensitive</c> (sem 2FA, decisão do PO). Os holerites da folha e a prévia do 13º só
/// por rotas <c>/sensitive</c>. Teto, auditoria, nome do arquivo sem pessoa e <c>no-store</c> no <see cref="ListExportService"/>.
/// </summary>
internal static partial class HrEndpoints
{
    private static void MapHrExports(RouteGroupBuilder hr, RouteGroupBuilder employees, RouteGroupBuilder runs)
    {
        employees.MapGet("/export", (string? format, string? q, EmployeeStatus? status, Guid? positionId, Guid? workCenterId, string? sort, HttpContext http, HrExportService service,
                ListExportService export, CancellationToken ct) =>
                export.ExportAsync(http, format, (max, c) => service.EmployeesAsync(new EmployeeListQuery(q, status, positionId, workCenterId), sort, max, c), ct))
            .RequirePermission("hr.view").WithName("ExportEmployees");
        runs.MapGet("/export", (string? format, HttpContext http, HrExportService service, ListExportService export, CancellationToken ct) =>
                export.ExportAsync(http, format, (_, c) => service.PayrollRunsAsync(c), ct))
            .RequirePermission("hr.view").WithName("ExportPayrollRuns");
        runs.MapGet("/{id:guid}/payslips/sensitive/export", (Guid id, string? format, HttpContext http, HrExportService service, ListExportService export, CancellationToken ct) =>
                export.ExportAsync(http, format, (_, c) => service.PayslipsAsync(id, c), ct))
            .RequirePermission("hr.view_sensitive").WithName("ExportPayslipsSensitive");
        hr.MapGet("/timesheets/export", (string? format, string? period, HttpContext http, HrExportService service, ListExportService export, CancellationToken ct) =>
                export.ExportAsync(http, format, (_, c) => service.TimesheetsAsync(period, c), ct))
            .RequirePermission("hr.view").WithName("ExportTimesheetOverview");
        hr.MapGet("/positions/export", (string? format, string? q, bool? includeInactive, string? sort, HttpContext http, HrExportService service, ListExportService export,
                CancellationToken ct) => export.ExportAsync(http, format, (max, c) => service.PositionsAsync(new PositionListQuery(q, includeInactive == true), sort, max, c), ct))
            .RequirePermission("hr.view").WithName("ExportPositions");

        // CA 11: prévia do 13º em PDF, sempre "Simulação", com a mesma regra da prévia da tela (hr.view_sensitive).
        runs.MapGet("/thirteenth-preview/sensitive/pdf", async (int? year, PayrollRunType? type, HttpContext http, HrExportService service, CancellationToken ct) =>
            {
                var result = await service.ThirteenthPreviewPdfAsync(year, type, ct);
                if (result.IsFailure)
                {
                    return result.Error!.ToProblem();
                }

                AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
                return Results.File(result.Value.Content, "application/pdf", result.Value.FileName);
            })
            .RequirePermission("hr.view_sensitive").WithName("DownloadThirteenthPreviewPdf").Produces(StatusCodes.Status200OK, contentType: "application/pdf");
    }
}
