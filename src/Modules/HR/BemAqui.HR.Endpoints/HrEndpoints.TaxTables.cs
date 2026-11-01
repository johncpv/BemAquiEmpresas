using BemAqui.BuildingBlocks.Web;
using BemAqui.HR.Application;
using BemAqui.HR.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.HR.Endpoints;

internal static partial class HrEndpoints
{
    /// <summary>
    /// Tabelas legais da folha (FLH-01 CA 15): ver, simular, o impacto e a leitura pela competência com <c>hr.view</c>; cadastrar, editar e
    /// retificar com <c>hr.edit</c> (e <c>hr.view_sensitive</c>, conferida no serviço); conferir com <c>hr.approve</c>.
    /// </summary>
    private static void MapPayrollTaxTables(RouteGroupBuilder tables)
    {
        tables.MapGet(string.Empty, async (PayrollTaxTableService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .RequirePermission("hr.view").WithName("ListPayrollTaxTables").Produces<PayrollTaxTablesView>();
        tables.MapGet("/{id:guid}", async (Guid id, PayrollTaxTableService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view").WithName("GetPayrollTaxTable").Produces<PayrollTaxTableDetail>();
        tables.MapGet("/for-competence", async (PayrollTaxTableKind kind, string? competence, PayrollTaxTableService service, CancellationToken ct) =>
                PayrollRules.ParsePeriod(competence) is { } period
                    ? (await service.ForCompetenceAsync(kind, period, ct)).ToHttpResult()
                    : BemAqui.SharedKernel.Error.Validation(BemAqui.SharedKernel.Error.Codes.Validation, "Informe a competência (mês e ano).").ToProblem())
            .RequirePermission("hr.view").WithName("GetPayrollTaxTableForCompetence").Produces<PayrollTaxTableInForce>();
        tables.MapPost("/simulate", async (PayrollTaxSimulationInput input, PayrollTaxTableService service, CancellationToken ct) => (await service.SimulateAsync(input, ct)).ToHttpResult())
            .RequirePermission("hr.view").WithName("SimulatePayrollTaxTables").Produces<PayrollTaxSimulation>();
        tables.MapGet("/{id:guid}/impact", async (Guid id, PayrollTaxTableService service, CancellationToken ct) => (await service.ImpactAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view").WithName("GetPayrollTaxTableImpact").Produces<PayrollTaxTableImpact>();
        tables.MapPost(string.Empty, async (PayrollTaxTableInput input, PayrollTaxTableService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/hr/payroll-tax-tables/" + saved.Id, saved)))
            .RequirePermission("hr.edit").WithName("CreatePayrollTaxTable").Produces<PayrollTaxTableDetail>(StatusCodes.Status201Created);
        tables.MapPut("/{id:guid}", async (Guid id, PayrollTaxTableInput input, PayrollTaxTableService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("UpdatePayrollTaxTable").Produces<PayrollTaxTableDetail>();
        tables.MapPost("/{id:guid}/rectify", async (Guid id, PayrollTaxTableRectifyInput input, PayrollTaxTableService service, CancellationToken ct) =>
                (await service.RectifyAsync(id, input, ct)).ToHttpResult(saved => Results.Created("/api/v1/hr/payroll-tax-tables/" + saved.Id, saved)))
            .RequirePermission("hr.edit").WithName("RectifyPayrollTaxTable").Produces<PayrollTaxTableDetail>(StatusCodes.Status201Created);
        tables.MapPost("/{id:guid}/check", async (Guid id, PayrollTaxTableCheckInput input, PayrollTaxTableService service, CancellationToken ct) => (await service.CheckAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.approve").WithName("CheckPayrollTaxTable").Produces<PayrollTaxTableDetail>();
    }
}
