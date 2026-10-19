using BemAqui.BuildingBlocks.Web;
using BemAqui.HR.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.HR.Endpoints;

internal static partial class HrEndpoints
{
    private static void MapSchedules(RouteGroupBuilder schedules)
    {
        schedules.MapGet(string.Empty, async (ScheduleService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .RequirePermission("hr.view").WithName("ListSchedules").Produces<IReadOnlyList<ScheduleDetail>>();
        schedules.MapPost(string.Empty, async (ScheduleInput input, ScheduleService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/hr/schedules/" + saved.Id, saved)))
            .RequirePermission("hr.create").WithName("CreateSchedule").Produces<ScheduleDetail>(StatusCodes.Status201Created);
        schedules.MapPut("/{id:guid}", async (Guid id, ScheduleInput input, ScheduleService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("UpdateSchedule").Produces<ScheduleDetail>();
        schedules.MapPost("/{id:guid}/inactivate", async (Guid id, ScheduleService service, CancellationToken ct) => (await service.SetActiveAsync(id, false, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("InactivateSchedule").Produces<ScheduleDetail>();
        schedules.MapPost("/{id:guid}/reactivate", async (Guid id, ScheduleService service, CancellationToken ct) => (await service.SetActiveAsync(id, true, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("ReactivateSchedule").Produces<ScheduleDetail>();
        schedules.MapDelete("/{id:guid}", async (Guid id, ScheduleService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.delete").WithName("DeleteSchedule");
    }

    private static void MapUnions(RouteGroupBuilder unions)
    {
        unions.MapGet(string.Empty, async (UnionService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .RequirePermission("hr.view").WithName("ListUnions").Produces<IReadOnlyList<UnionDetail>>();
        unions.MapPost(string.Empty, async (UnionInput input, UnionService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/hr/unions/" + saved.Id, saved)))
            .RequirePermission("hr.create").WithName("CreateUnion").Produces<UnionDetail>(StatusCodes.Status201Created);
        unions.MapPut("/{id:guid}", async (Guid id, UnionInput input, UnionService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("UpdateUnion").Produces<UnionDetail>();
        unions.MapPost("/{id:guid}/inactivate", async (Guid id, UnionService service, CancellationToken ct) => (await service.SetActiveAsync(id, false, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("InactivateUnion").Produces<UnionDetail>();
        unions.MapPost("/{id:guid}/reactivate", async (Guid id, UnionService service, CancellationToken ct) => (await service.SetActiveAsync(id, true, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("ReactivateUnion").Produces<UnionDetail>();
        unions.MapDelete("/{id:guid}", async (Guid id, UnionService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.delete").WithName("DeleteUnion");
    }
}
