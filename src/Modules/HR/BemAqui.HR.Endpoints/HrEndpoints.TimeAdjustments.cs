using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Web;
using BemAqui.HR.Application.TimeClock;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.HR.Endpoints;

internal static partial class HrEndpoints
{
    /// <summary>
    /// Ajustes do ponto pelo RH (RH-01b CA 2 a 4, 7 a 16 e 18): ver com <c>hr.view</c>, lançar e abonar com <c>hr.create</c> (com
    /// <c>Idempotency-Key</c>), aprovar e reprovar com <c>hr.approve</c>, documento do abono com <c>hr.view_health</c>. As rotas por
    /// funcionário conferem que o pedido é dele (IDOR). Nenhuma rota edita nem apaga marcação ou pedido.
    /// </summary>
    private static void MapTimeAdjustments(RouteGroupBuilder hr, RouteGroupBuilder employees)
    {
        hr.MapGet("/absence-reasons", () => Results.Ok(TimeAdjustmentService.Reasons()))
            .RequirePermission("hr.view").WithName("ListAbsenceReasons").Produces<IReadOnlyList<AbsenceReasonOption>>();

        var adjustments = hr.MapGroup("/time-adjustments");
        adjustments.MapGet(string.Empty, async (TimeAdjustmentStatus? status, Guid? employeeId, TimeAdjustmentService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(status, employeeId, ct)))
            .RequirePermission("hr.view").WithName("ListTimeAdjustments").Produces<TimeAdjustmentList>();
        adjustments.MapGet("/{id:guid}", async (Guid id, TimeAdjustmentService service, CancellationToken ct) => (await service.DetailAsync(id, null, ct)).ToHttpResult())
            .RequirePermission("hr.view").WithName("GetTimeAdjustment").Produces<TimeAdjustmentDetail>();
        adjustments.MapPost("/{id:guid}/approve", async (Guid id, TimeAdjustmentDecisionInput input, TimeAdjustmentService service, CancellationToken ct) =>
                (await service.ApproveAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.approve").WithName("ApproveTimeAdjustment").Produces<TimeAdjustmentView>();
        adjustments.MapPost("/{id:guid}/reject", async (Guid id, TimeAdjustmentDecisionInput input, TimeAdjustmentService service, CancellationToken ct) =>
                (await service.RejectAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.approve").WithName("RejectTimeAdjustment").Produces<TimeAdjustmentView>();
        adjustments.MapPost("/{id:guid}/cancel", async (Guid id, TimeAdjustmentDecisionInput input, TimeAdjustmentService service, CancellationToken ct) =>
                (await service.CancelAsync(id, input, null, ct)).ToHttpResult())
            .RequirePermission("hr.create").WithName("CancelTimeAdjustment").Produces<TimeAdjustmentView>();
        adjustments.MapPost("/approve-batch", async (TimeAdjustmentBatchInput input, TimeAdjustmentService service, CancellationToken ct) =>
                (await service.ApproveManyAsync(input, ct)).ToHttpResult())
            .RequirePermission("hr.approve").WithName("ApproveTimeAdjustments").Produces<TimeAdjustmentBatchResult>();

        employees.MapGet("/{id:guid}/time-adjustments", async (Guid id, TimeAdjustmentService service, CancellationToken ct) => (await service.EmployeeAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view").WithName("ListEmployeeTimeAdjustments").Produces<TimeAdjustmentList>();
        employees.MapGet("/{id:guid}/time-adjustments/schedule-day", async (Guid id, DateOnly day, TimeAdjustmentService service, CancellationToken ct) =>
                (await service.ScheduleDayAsync(id, day, ct)).ToHttpResult())
            .RequirePermission("hr.view").WithName("GetEmployeeScheduleDay").Produces<ScheduleDayProposal>();
        employees.MapGet("/{id:guid}/time-adjustments/{adjustmentId:guid}", async (Guid id, Guid adjustmentId, TimeAdjustmentService service, CancellationToken ct) =>
                (await service.DetailAsync(adjustmentId, id, ct)).ToHttpResult())
            .RequirePermission("hr.view").WithName("GetEmployeeTimeAdjustment").Produces<TimeAdjustmentDetail>();
        employees.MapPost("/{id:guid}/time-adjustments", async (Guid id, TimeAdjustmentInput input, TimeAdjustmentService service, CancellationToken ct) =>
                (await service.CreateForEmployeeAsync(id, input, ct)).ToHttpResult(saved => Results.Created($"/api/v1/hr/employees/{id}/time-adjustments/{saved.Id}", saved)))
            .RequirePermission("hr.create").RequireIdempotencyKey().WithName("CreateEmployeeTimeAdjustment").Produces<TimeAdjustmentView>(StatusCodes.Status201Created);
        employees.MapPost("/{id:guid}/time-adjustments/day", async (Guid id, TimeAdjustmentDayInput input, TimeAdjustmentService service, CancellationToken ct) =>
                (await service.CreateDayAsync(id, input, ct)).ToHttpResult(saved => Results.Created($"/api/v1/hr/employees/{id}/time-adjustments", saved)))
            .RequirePermission("hr.create").RequireIdempotencyKey().WithName("CreateEmployeeTimeAdjustmentDay").Produces<IReadOnlyList<TimeAdjustmentView>>(StatusCodes.Status201Created);
        employees.MapPost("/{id:guid}/time-adjustments/{adjustmentId:guid}/documents", async (Guid id, Guid adjustmentId, IFormFile file, TimeAdjustmentService service, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                return (await service.AttachAsync(id, adjustmentId, file.FileName, content, ct)).ToHttpResult(dto => Results.Created($"/api/v1/hr/employees/{id}/time-adjustments/{adjustmentId}/documents/{dto.Id}", dto));
            })
            .RequirePermission("hr.view_health").DisableAntiforgery().WithName("AttachTimeAdjustmentDocument").Produces<AttachmentDto>(StatusCodes.Status201Created);
        employees.MapGet("/{id:guid}/time-adjustments/{adjustmentId:guid}/documents/{attachmentId:guid}", async (Guid id, Guid adjustmentId, Guid attachmentId, TimeAdjustmentService service, HttpContext http, CancellationToken ct) =>
                File(http, await service.DocumentAsync(id, adjustmentId, attachmentId, ct)))
            .RequirePermission("hr.view_health").WithName("DownloadTimeAdjustmentDocument");
    }

    /// <summary>
    /// "Pedir ajuste" e "Meus pedidos" (RH-01b CA 5 a 8, ADR 0035): <c>/api/v1/me/hr/time-adjustments</c> pela camada 1 e pelo vínculo, sem
    /// ação no grupo e fora da sessão por PIN; o funcionário vem sempre da sessão. Pedir exige funcionário ativo e <c>Idempotency-Key</c>.
    /// </summary>
    private static void MapMyTimeAdjustments(RouteGroupBuilder me)
    {
        me.MapGet("/absence-reasons", () => Results.Ok(TimeAdjustmentService.Reasons()))
            .RequireActiveEmployeeLink(allowOnLeave: true).WithName("ListMyAbsenceReasons").Produces<IReadOnlyList<AbsenceReasonOption>>();
        var mine = me.MapGroup("/time-adjustments");
        mine.MapGet(string.Empty, async (HttpContext http, TimeAdjustmentService service, CancellationToken ct) =>
                Results.Ok(await service.MineAsync(ActiveEmployeeLink.Employee(http), ct)))
            .RequireActiveEmployeeLink(allowOnLeave: true).WithName("ListMyTimeAdjustments").Produces<IReadOnlyList<TimeAdjustmentView>>();
        mine.MapPost(string.Empty, async (TimeAdjustmentInput input, HttpContext http, TimeAdjustmentService service, CancellationToken ct) =>
                (await service.CreateMineAsync(ActiveEmployeeLink.Employee(http), input, ct)).ToHttpResult(saved => Results.Created("/api/v1/me/hr/time-adjustments/" + saved.Id, saved)))
            .RequireActiveEmployeeLink().RequireIdempotencyKey().WithName("CreateMyTimeAdjustment").Produces<TimeAdjustmentView>(StatusCodes.Status201Created);
        mine.MapPost("/{id:guid}/cancel", async (Guid id, TimeAdjustmentDecisionInput input, HttpContext http, TimeAdjustmentService service, CancellationToken ct) =>
                (await service.CancelAsync(id, input, ActiveEmployeeLink.Employee(http).Id, ct)).ToHttpResult())
            .RequireActiveEmployeeLink(allowOnLeave: true).WithName("CancelMyTimeAdjustment").Produces<TimeAdjustmentView>();
        mine.MapPost("/{id:guid}/documents", async (Guid id, IFormFile file, HttpContext http, TimeAdjustmentService service, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                return (await service.AttachMineAsync(ActiveEmployeeLink.Employee(http), id, file.FileName, content, ct)).ToHttpResult(dto => Results.Created($"/api/v1/me/hr/time-adjustments/{id}/documents/{dto.Id}", dto));
            })
            .RequireActiveEmployeeLink(allowOnLeave: true).DisableAntiforgery().WithName("AttachMyTimeAdjustmentDocument").Produces<AttachmentDto>(StatusCodes.Status201Created);
        mine.MapGet("/{id:guid}/documents/{attachmentId:guid}", async (Guid id, Guid attachmentId, HttpContext http, TimeAdjustmentService service, CancellationToken ct) =>
                File(http, await service.MyDocumentAsync(ActiveEmployeeLink.Employee(http), id, attachmentId, ct)))
            .RequireActiveEmployeeLink(allowOnLeave: true).WithName("DownloadMyTimeAdjustmentDocument");
    }

    private static IResult File(HttpContext http, Result<AttachmentContent> result)
    {
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
        return Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }
}
