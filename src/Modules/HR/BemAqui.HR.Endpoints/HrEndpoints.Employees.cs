using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.HR.Application;
using BemAqui.HR.Domain;
using BemAqui.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.HR.Endpoints;

internal static partial class HrEndpoints
{
    /// <summary>Funcionários (CAD-04b). Os comandos sensíveis conferem hr.view_sensitive no serviço; o vínculo confere identity.edit.</summary>
    private static void MapEmployees(RouteGroupBuilder employees)
    {
        employees.MapGet(string.Empty, async (string? q, EmployeeStatus? status, Guid? positionId, Guid? workCenterId, int? page, int? pageSize, string? sort, EmployeeService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new EmployeeListQuery(q, status, positionId, workCenterId), new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize, sort).Normalize(), ct)))
            .RequirePermission("hr.view").WithName("ListEmployees").Produces<PagedList<EmployeeDetail>>();
        employees.MapGet("/linkable-users", async (EmployeeService service, CancellationToken ct) => (await service.LinkableUsersAsync(ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("ListLinkableUsers").Produces<IReadOnlyList<LinkableUser>>();
        employees.MapGet("/cep/{cep}", async (string cep, BemAqui.BuildingBlocks.Lookups.ICepLookup lookup, CancellationToken ct) => (await lookup.LookupAsync(cep, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("LookupEmployeeCep").Produces<BemAqui.BuildingBlocks.Lookups.AddressData>();
        employees.MapGet("/{id:guid}", async (Guid id, EmployeeService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view").WithName("GetEmployee").Produces<EmployeeDetail>();
        employees.MapGet("/{id:guid}/sensitive", async (Guid id, EmployeeService service, CancellationToken ct) => (await service.GetSensitiveAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("GetEmployeeSensitive").Produces<EmployeeSensitiveDetail>();
        employees.MapGet("/{id:guid}/timeline", async (Guid id, EmployeeService service, CancellationToken ct) => (await service.TimelineAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view").WithName("GetEmployeeTimeline").Produces<IReadOnlyList<TimelineEntry>>();
        employees.MapPost(string.Empty, async (EmployeeCreateInput input, EmployeeService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/hr/employees/" + saved.Employee.Id, saved)))
            .RequirePermission("hr.create").WithName("CreateEmployee").Produces<EmployeeCreated>(StatusCodes.Status201Created);
        employees.MapPut("/{id:guid}", async (Guid id, EmployeeGeneralInput input, EmployeeService service, CancellationToken ct) => (await service.UpdateGeneralAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("UpdateEmployee").Produces<EmployeeDetail>();
        employees.MapPut("/{id:guid}/personal", async (Guid id, EmployeePersonalInput input, EmployeeService service, CancellationToken ct) => (await service.UpdatePersonalAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("UpdateEmployeePersonal").Produces<EmployeeDetail>();
        employees.MapPost("/{id:guid}/salary", async (Guid id, SalaryChangeInput input, EmployeeService service, CancellationToken ct) => (await service.ChangeSalaryAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("ChangeEmployeeSalary").Produces<EmployeeDetail>();
        employees.MapPost("/{id:guid}/position", async (Guid id, PositionChangeInput input, EmployeeService service, CancellationToken ct) => (await service.ChangePositionAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("ChangeEmployeePosition").Produces<EmployeeDetail>();
        employees.MapPost("/{id:guid}/termination", async (Guid id, TerminationInput input, EmployeeService service, CancellationToken ct) => (await service.TerminateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("TerminateEmployee").Produces<EmployeeDetail>();
        // Afastamentos (CAD-04c1): registrar exige hr.edit e, no serviço, hr.view_sensitive; tipo, datas e motivo só pela rota /sensitive.
        employees.MapPost("/{id:guid}/leaves", async (Guid id, LeaveInput input, EmployeeService service, CancellationToken ct) => (await service.RegisterLeaveAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("RegisterEmployeeLeave").Produces<EmployeeDetail>();
        employees.MapGet("/{id:guid}/leaves/sensitive", async (Guid id, EmployeeService service, CancellationToken ct) => (await service.LeavesAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("ListEmployeeLeavesSensitive").Produces<IReadOnlyList<LeaveDetail>>();
        employees.MapDelete("/{id:guid}", async (Guid id, EmployeeService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.delete").WithName("DeleteEmployee");
        employees.MapGet("/{id:guid}/user", async (Guid id, EmployeeService service, CancellationToken ct) => (await service.LinkedUserAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view").WithName("GetEmployeeUser").Produces<LinkedUserView>();
        employees.MapPut("/{id:guid}/user", async (Guid id, UserLinkInput input, EmployeeService service, CancellationToken ct) => (await service.LinkUserAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("LinkEmployeeUser").Produces<LinkableUser>();
        employees.MapDelete("/{id:guid}/user", async (Guid id, EmployeeService service, CancellationToken ct) => (await service.UnlinkUserAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("UnlinkEmployeeUser");
    }
}
