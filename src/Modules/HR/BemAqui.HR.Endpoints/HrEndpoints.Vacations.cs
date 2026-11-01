using BemAqui.BuildingBlocks.Web;
using BemAqui.HR.Application;
using BemAqui.HR.Application.Documents;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.HR.Endpoints;

internal static partial class HrEndpoints
{
    /// <summary>
    /// Férias (FLH-03 CA 1 a 12): ver os períodos, as programações, o calendário e o aviso com <c>hr.view</c> (sem valor); programar e cancelar
    /// com <c>hr.edit</c>; calcular com <c>hr.edit</c> e <c>hr.view_sensitive</c> no serviço; fechar a folha de férias pela rota da folha
    /// (<c>hr.approve</c>). A programação da rota do funcionário confere o funcionário (IDOR).
    /// </summary>
    private static void MapVacations(RouteGroupBuilder hr, RouteGroupBuilder employees)
    {
        var vacations = hr.MapGroup("/vacations");
        vacations.MapGet(string.Empty, async (int? days, VacationService service, CancellationToken ct) => Results.Ok(await service.OverviewAsync(days, ct)))
            .RequirePermission("hr.view").WithName("GetVacationOverview").Produces<VacationOverview>();
        vacations.MapPost("/{id:guid}/cancel", async (Guid id, VacationCancelInput input, VacationService service, CancellationToken ct) => (await service.CancelAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("CancelVacationSchedule").Produces<VacationScheduleItem>();
        vacations.MapPost("/{id:guid}/calculate", async (Guid id, VacationService service, CancellationToken ct) => (await service.CalculateAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.edit").RequireIdempotencyKey().WithName("CalculateVacation").Produces<VacationCalculationResult>();
        vacations.MapGet("/{id:guid}/notice", async (Guid id, VacationService service, HttpContext http, CancellationToken ct) => File(http, await service.NoticeAsync(id, ct)))
            .RequirePermission("hr.view").WithName("DownloadVacationNotice").Produces(StatusCodes.Status200OK, contentType: PayslipDocumentService.PdfContentType);

        employees.MapGet("/{id:guid}/vacations", async (Guid id, VacationService service, CancellationToken ct) => (await service.EmployeeAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view").WithName("GetEmployeeVacations").Produces<EmployeeVacations>();
        employees.MapPost("/{id:guid}/vacations", async (Guid id, VacationScheduleInput input, VacationService service, CancellationToken ct) =>
                (await service.ScheduleAsync(id, input, ct)).ToHttpResult(saved => Results.Created("/api/v1/hr/vacations/" + saved.Schedule.Id, saved)))
            .RequirePermission("hr.edit").RequireIdempotencyKey().WithName("ScheduleVacation").Produces<VacationScheduleResult>(StatusCodes.Status201Created);
    }

    /// <summary>"Minhas férias" no "Meu ponto" (CA 5 e 12, ADR 0035): pelo vínculo (o afastado também vê), só datas e saldo, sem valor.</summary>
    private static void MapMyVacations(RouteGroupBuilder me) =>
        me.MapGet("/vacations", async (HttpContext http, VacationService service, CancellationToken ct) => Results.Ok(await service.MineAsync(ActiveEmployeeLink.Employee(http).Id, ct)))
            .RequireActiveEmployeeLink(allowOnLeave: true).WithName("ListMyVacations").Produces<MyVacations>();
}
