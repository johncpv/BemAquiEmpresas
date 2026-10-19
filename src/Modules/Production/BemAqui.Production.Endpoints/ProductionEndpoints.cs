using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Production.Endpoints;

/// <summary>Centros de trabalho e calendário (CAD-03 CA 8 a 14). Módulo <c>production</c> com as três camadas de acesso (CA 19).</summary>
internal static class ProductionEndpoints
{
    public static IEndpointRouteBuilder MapProductionEndpoints(this IEndpointRouteBuilder api)
    {
        var production = api.MapGroup("/production").WithTags("Produção").RequireModule("production");
        MapWorkCenters(production.MapGroup("/work-centers"));
        MapCalendars(production.MapGroup("/calendars"));
        return api;
    }

    private static void MapWorkCenters(RouteGroupBuilder centers)
    {
        centers.MapGet(string.Empty, async (string? q, bool? includeInactive, int? page, int? pageSize, WorkCenterService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new WorkCenterListQuery(q, includeInactive == true), new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)))
            .RequirePermission("production.view").WithName("ListWorkCenters").Produces<PagedList<WorkCenterDetail>>();
        centers.MapGet("/suggested-shifts", async (WorkCenterService service, CancellationToken ct) => Results.Ok(await service.SuggestedShiftsAsync(ct)))
            .RequirePermission("production.view").WithName("GetSuggestedShifts").Produces<IReadOnlyList<Shift>>();
        centers.MapGet("/{id:guid}", async (Guid id, WorkCenterService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("production.view").WithName("GetWorkCenter").Produces<WorkCenterDetail>();
        centers.MapPost(string.Empty, async (WorkCenterInput input, WorkCenterService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/production/work-centers/" + saved.Id, saved)))
            .RequirePermission("production.create").WithName("CreateWorkCenter").Produces<WorkCenterDetail>(StatusCodes.Status201Created);
        centers.MapPut("/{id:guid}", async (Guid id, WorkCenterInput input, WorkCenterService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("production.edit").WithName("UpdateWorkCenter").Produces<WorkCenterDetail>();
        centers.MapPost("/{id:guid}/inactivate", async (Guid id, WorkCenterService service, CancellationToken ct) => (await service.SetActiveAsync(id, false, ct)).ToHttpResult())
            .RequirePermission("production.edit").WithName("InactivateWorkCenter").Produces<WorkCenterDetail>();
        centers.MapPost("/{id:guid}/reactivate", async (Guid id, WorkCenterService service, CancellationToken ct) => (await service.SetActiveAsync(id, true, ct)).ToHttpResult())
            .RequirePermission("production.edit").WithName("ReactivateWorkCenter").Produces<WorkCenterDetail>();
        centers.MapDelete("/{id:guid}", async (Guid id, WorkCenterService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission("production.delete").WithName("DeleteWorkCenter");
    }

    private static void MapCalendars(RouteGroupBuilder calendars)
    {
        calendars.MapGet("/default", async (CalendarService service, CancellationToken ct) => Results.Ok(await service.GetDefaultAsync(ct)))
            .RequirePermission("production.view").WithName("GetDefaultCalendar").Produces<CalendarDetail>();
        calendars.MapPut("/{id:guid}", async (Guid id, CalendarInput input, CalendarService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("production.edit").WithName("UpdateCalendar").Produces<CalendarDetail>();

        var exceptions = calendars.MapGroup("/{calendarId:guid}/exceptions");
        exceptions.MapGet(string.Empty, async (Guid calendarId, int year, CalendarService service, CancellationToken ct) => (await service.ListExceptionsAsync(calendarId, year, ct)).ToHttpResult())
            .RequirePermission("production.view").WithName("ListCalendarExceptions").Produces<IReadOnlyList<CalendarExceptionDetail>>();
        exceptions.MapPost(string.Empty, async (Guid calendarId, CalendarExceptionInput input, CalendarService service, CancellationToken ct) =>
                (await service.CreateExceptionAsync(calendarId, input, ct)).ToHttpResult(saved => Results.Created($"/api/v1/production/calendars/{calendarId}/exceptions/{saved.Id}", saved)))
            .RequirePermission("production.edit").WithName("CreateCalendarException").Produces<CalendarExceptionDetail>(StatusCodes.Status201Created);
        exceptions.MapPut("/{exceptionId:guid}", async (Guid calendarId, Guid exceptionId, CalendarExceptionInput input, CalendarService service, CancellationToken ct) =>
                (await service.UpdateExceptionAsync(calendarId, exceptionId, input, ct)).ToHttpResult())
            .RequirePermission("production.edit").WithName("UpdateCalendarException").Produces<CalendarExceptionDetail>();
        exceptions.MapDelete("/{exceptionId:guid}", async (Guid calendarId, Guid exceptionId, CalendarService service, CancellationToken ct) =>
                (await service.DeleteExceptionAsync(calendarId, exceptionId, ct)).ToHttpResult())
            .RequirePermission("production.edit").WithName("DeleteCalendarException");

        calendars.MapGet("/{calendarId:guid}/holidays/{year:int}", async (Guid calendarId, int year, CalendarService service, CancellationToken ct) =>
                (await service.SuggestHolidaysAsync(calendarId, year, ct)).ToHttpResult())
            .RequirePermission("production.view").WithName("SuggestHolidays").Produces<IReadOnlyList<HolidaySuggestion>>();
        calendars.MapPost("/{calendarId:guid}/holidays", async (Guid calendarId, AddHolidaysInput input, CalendarService service, CancellationToken ct) =>
                (await service.AddHolidaysAsync(calendarId, input, ct)).ToHttpResult())
            .RequirePermission("production.edit").WithName("AddHolidays").Produces<AddHolidaysResult>();
    }
}
