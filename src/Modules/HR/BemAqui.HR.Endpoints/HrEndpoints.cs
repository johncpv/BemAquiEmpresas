using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.HR.Application;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.HR.Endpoints;

/// <summary>
/// Cargos, jornadas e sindicatos (CAD-04a). Módulo <c>hr</c> com as três camadas de acesso (CA 18); todas as respostas com
/// <c>no-store</c>, e a faixa salarial só pela rota <c>/sensitive</c>, com <c>hr.view_sensitive</c> (CA 10).
/// </summary>
internal static partial class HrEndpoints
{
    public static IEndpointRouteBuilder MapHrEndpoints(this IEndpointRouteBuilder api)
    {
        var hr = api.MapGroup("/hr").WithTags("RH").RequireModule("hr")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });
        MapPositions(hr.MapGroup("/positions"));
        MapSchedules(hr.MapGroup("/schedules"));
        MapUnions(hr.MapGroup("/unions"));
        // LGPD-01b CA 4: funcionário anonimizado é só leitura; toda escrita na rota dele responde 409 employee_anonymized.
        var employees = hr.MapGroup("/employees").AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method)
                && (http.Request.RouteValues.TryGetValue("id", out var raw) || http.Request.RouteValues.TryGetValue("employeeId", out raw))
                && Guid.TryParse(raw?.ToString(), out var employeeId)
                && await http.RequestServices.GetRequiredService<IHrDb>().Employees.IgnoreQueryFilters().AnyAsync(e => e.Id == employeeId && e.AnonymizedAt != null, http.RequestAborted))
            {
                return Result.Failure(Error.Conflict("employee_anonymized", "Cadastro anonimizado a pedido do titular: não pode ser alterado.")).ToHttpResult();
            }

            return await next(context);
        });
        MapEmployees(employees);
        MapEmployeeComplement(employees);
        MapPayrollItems(hr.MapGroup("/payroll-items"));
        MapHrParameters(hr.MapGroup("/parameters"));
        var runs = hr.MapGroup("/payroll-runs");
        MapPayrollRuns(runs);
        MapPayslipDocuments(runs);
        MapAccountantExports(hr.MapGroup("/accountant-exports"));
        MapTimeEntries(hr, employees);
        MapTimeAdjustments(hr, employees);
        MapTimesheets(hr, employees, runs);

        // Autoatendimento do funcionário (RH-02b, ADR 0035): camada 1 no grupo; o vínculo ativo em cada endpoint.
        var me = api.MapGroup("/me/hr").WithTags("Autoatendimento").RequireModule("hr")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });
        MapMyPayslips(me);
        MapMyTimeClock(me);
        MapMyTimeAdjustments(me);
        MapMyTimesheets(me);
        return api;
    }

    private static void MapPositions(RouteGroupBuilder positions)
    {
        positions.MapGet(string.Empty, async (string? q, bool? includeInactive, int? page, int? pageSize, string? sort, PositionService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new PositionListQuery(q, includeInactive == true), new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize, sort).Normalize(), ct)))
            .RequirePermission("hr.view").WithName("ListPositions").Produces<PagedList<PositionDetail>>();
        positions.MapGet("/summary", async (PositionService service, CancellationToken ct) => Results.Ok(await service.SummaryAsync(ct)))
            .RequirePermission("hr.view").WithName("GetPositionsSummary").Produces<PositionSummary>();
        positions.MapGet("/work-centers", async (PositionService service, CancellationToken ct) => Results.Ok(await service.WorkCenterChoicesAsync(ct)))
            .RequirePermission("hr.view").WithName("ListPositionWorkCenters").Produces<IReadOnlyList<WorkCenterChoice>>();
        positions.MapGet("/{id:guid}", async (Guid id, PositionService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view").WithName("GetPosition").Produces<PositionDetail>();
        positions.MapGet("/{id:guid}/sensitive", async (Guid id, PositionService service, CancellationToken ct) => (await service.GetSalaryAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("GetPositionSalary").Produces<PositionSalaryDetail>();
        positions.MapPost(string.Empty, async (PositionInput input, PositionService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/hr/positions/" + saved.Id, saved)))
            .RequirePermission("hr.create").WithName("CreatePosition").Produces<PositionDetail>(StatusCodes.Status201Created);
        positions.MapPut("/{id:guid}", async (Guid id, PositionInput input, PositionService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("UpdatePosition").Produces<PositionDetail>();
        // A faixa exige também hr.view_sensitive, conferido no serviço (CA 11).
        positions.MapPut("/{id:guid}/salary", async (Guid id, PositionSalaryInput input, PositionService service, CancellationToken ct) => (await service.UpdateSalaryAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("UpdatePositionSalary").Produces<PositionDetail>();
        positions.MapPut("/{id:guid}/hourly-cost", async (Guid id, HourlyCostInput input, PositionService service, CancellationToken ct) => (await service.SetManualCostAsync(id, input.Value, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("SetPositionHourlyCost").Produces<PositionDetail>();
        positions.MapDelete("/{id:guid}/hourly-cost", async (Guid id, PositionService service, CancellationToken ct) => (await service.SetManualCostAsync(id, null, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("ResetPositionHourlyCost").Produces<PositionDetail>();
        positions.MapPost("/{id:guid}/inactivate", async (Guid id, PositionService service, CancellationToken ct) => (await service.SetActiveAsync(id, false, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("InactivatePosition").Produces<PositionDetail>();
        positions.MapPost("/{id:guid}/reactivate", async (Guid id, PositionService service, CancellationToken ct) => (await service.SetActiveAsync(id, true, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("ReactivatePosition").Produces<PositionDetail>();
        positions.MapDelete("/{id:guid}", async (Guid id, PositionService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.delete").WithName("DeletePosition");
    }
}
