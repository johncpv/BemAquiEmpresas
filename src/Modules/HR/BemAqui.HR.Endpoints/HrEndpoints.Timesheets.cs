using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Web;
using BemAqui.HR.Application.TimeClock;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.HR.Endpoints;

internal static partial class HrEndpoints
{
    /// <summary>
    /// Espelho de ponto pelo RH (RH-01c CA 3, 11 a 14, 16, 18 e 20): ver e baixar com <c>hr.view</c>; fechar e marcar revisado com
    /// <c>hr.edit</c>; reabrir com <c>hr.approve</c>; o acordo de banco com <c>hr.edit</c> + <c>hr.view_sensitive</c> (o sensível no serviço).
    /// As rotas por funcionário conferem o funcionário da rota (IDOR). O período vai na rota como "2026-10".
    /// </summary>
    private static void MapTimesheets(RouteGroupBuilder hr, RouteGroupBuilder employees, RouteGroupBuilder runs)
    {
        var sheets = hr.MapGroup("/timesheets");
        sheets.MapGet(string.Empty, async (string? period, TimesheetService service, CancellationToken ct) => (await service.OverviewAsync(period, ct)).ToHttpResult())
            .RequirePermission("hr.view").WithName("GetTimesheetOverview").Produces<TimesheetOverview>();
        sheets.MapGet("/{period}/close-check", async (string period, TimesheetService service, CancellationToken ct) => (await service.CloseCheckAsync(period, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("GetTimesheetCloseCheck").Produces<TimesheetCloseCheck>();
        sheets.MapPost("/{period}/close", async (string period, TimesheetService service, CancellationToken ct) => (await service.CloseAsync(period, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("CloseTimesheetPeriod").Produces<TimesheetCloseResult>();
        sheets.MapPost("/{period}/reopen", async (string period, TimesheetReopenInput input, TimesheetService service, CancellationToken ct) => (await service.ReopenAsync(period, input, ct)).ToHttpResult())
            .RequirePermission("hr.approve").WithName("ReopenTimesheetPeriod").Produces<TimesheetReopenResult>();
        sheets.MapPost("/{period}/pdfs", async (string period, TimesheetPdfService service, CancellationToken ct) => (await service.StartBatchAsync(period, ct)).ToHttpResult())
            .RequirePermission("hr.view").WithName("GenerateTimesheetBatch").Produces<TimesheetBatchResult>();
        sheets.MapGet("/{period}/pdfs/{attachmentId:guid}", async (string period, Guid attachmentId, TimesheetPdfService service, HttpContext http, CancellationToken ct) =>
                File(http, await service.BatchFileAsync(period, attachmentId, ct)))
            .RequirePermission("hr.view").WithName("DownloadTimesheetBatch").Produces(StatusCodes.Status200OK, contentType: TimesheetPdfService.ZipContentType);

        employees.MapGet("/{id:guid}/timesheets/{period}", async (Guid id, string period, TimesheetService service, CancellationToken ct) => (await service.EmployeeAsync(id, period, ct)).ToHttpResult())
            .RequirePermission("hr.view").WithName("GetEmployeeTimesheet").Produces<TimesheetDetail>();
        employees.MapPost("/{id:guid}/timesheets/reviews", async (Guid id, TimesheetDayReviewInput input, TimesheetService service, CancellationToken ct) =>
                (await service.ReviewDayAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("ReviewTimesheetDay").Produces<TimesheetDayView>();
        employees.MapGet("/{id:guid}/timesheets/{period}/pdf", async (Guid id, string period, TimesheetPdfService service, HttpContext http, CancellationToken ct) =>
                File(http, await service.PdfAsync(id, period, ct)))
            .RequirePermission("hr.view").WithName("DownloadEmployeeTimesheetPdf").Produces(StatusCodes.Status200OK, contentType: TimesheetPdfService.PdfContentType);
        employees.MapGet("/{id:guid}/hour-bank-agreement", async (Guid id, TimesheetService service, CancellationToken ct) => (await service.AgreementAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("GetHourBankAgreement").Produces<HourBankAgreementView>();
        employees.MapPut("/{id:guid}/hour-bank-agreement", async (Guid id, HourBankAgreementInput input, TimesheetService service, CancellationToken ct) =>
                (await service.SetAgreementAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("SetHourBankAgreement").Produces<HourBankAgreementView>();

        runs.MapGet("/{id:guid}/payslips/{payslipId:guid}/timesheet", async (Guid id, Guid payslipId, TimesheetService service, CancellationToken ct) =>
                (await service.PayslipHoursAsync(id, payslipId, ct)).ToHttpResult())
            .RequirePermission("hr.view").WithName("GetPayslipTimesheetHours").Produces<PayslipTimesheetHours>();
    }

    /// <summary>
    /// "Meu espelho" (RH-01c CA 8, 15 e 18, ADR 0035): <c>/api/v1/me/hr/timesheets</c> pela camada 1 e pelo vínculo (o afastado também vê),
    /// sem ação no grupo e fora da sessão por PIN; o funcionário vem sempre da sessão (nunca o de outro).
    /// </summary>
    private static void MapMyTimesheets(RouteGroupBuilder me)
    {
        var mine = me.MapGroup("/timesheets");
        mine.MapGet(string.Empty, async (HttpContext http, TimesheetService service, CancellationToken ct) => Results.Ok(await service.MyPeriodsAsync(ActiveEmployeeLink.Employee(http), ct)))
            .RequireActiveEmployeeLink(allowOnLeave: true).WithName("ListMyTimesheets").Produces<IReadOnlyList<MyTimesheetPeriod>>();
        mine.MapGet("/{period}", async (string period, HttpContext http, TimesheetService service, CancellationToken ct) =>
                (await service.MineAsync(ActiveEmployeeLink.Employee(http), period, ct)).ToHttpResult())
            .RequireActiveEmployeeLink(allowOnLeave: true).WithName("GetMyTimesheet").Produces<TimesheetDetail>();
        mine.MapPost("/{period}/ack", async (string period, HttpContext http, TimesheetService service, CancellationToken ct) =>
                (await service.AcknowledgeAsync(ActiveEmployeeLink.Employee(http), period, ct)).ToHttpResult())
            .RequireActiveEmployeeLink(allowOnLeave: true).WithName("AcknowledgeMyTimesheet").Produces<TimesheetAckResult>();
        mine.MapGet("/{period}/pdf", async (string period, HttpContext http, TimesheetPdfService service, CancellationToken ct) =>
                File(http, await service.MinePdfAsync(ActiveEmployeeLink.Employee(http), period, ct)))
            .RequireActiveEmployeeLink(allowOnLeave: true).WithName("DownloadMyTimesheetPdf").Produces(StatusCodes.Status200OK, contentType: TimesheetPdfService.PdfContentType);
    }

    private static IResult File(HttpContext http, Result<TimesheetFile> result)
    {
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
        return Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }
}
