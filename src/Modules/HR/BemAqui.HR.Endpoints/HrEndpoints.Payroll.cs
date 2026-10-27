using BemAqui.BuildingBlocks.Web;
using BemAqui.HR.Application;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.HR.Endpoints;

internal static partial class HrEndpoints
{
    /// <summary>Rubricas (RH-02a CA 1 e 2): ler com <c>hr.view</c>; criar, editar, inativar, excluir e conferir com <c>hr.edit</c>.</summary>
    private static void MapPayrollItems(RouteGroupBuilder items)
    {
        items.MapGet(string.Empty, async (PayrollItemService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .RequirePermission("hr.view").WithName("ListPayrollItems").Produces<IReadOnlyList<PayrollItemDetail>>();
        items.MapPost(string.Empty, async (PayrollItemInput input, PayrollItemService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/hr/payroll-items/" + saved.Id, saved)))
            .RequirePermission("hr.edit").WithName("CreatePayrollItem").Produces<PayrollItemDetail>(StatusCodes.Status201Created);
        items.MapPut("/{id:guid}", async (Guid id, PayrollItemInput input, PayrollItemService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("UpdatePayrollItem").Produces<PayrollItemDetail>();
        items.MapPost("/{id:guid}/inactivate", async (Guid id, PayrollItemService service, CancellationToken ct) => (await service.SetActiveAsync(id, false, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("InactivatePayrollItem").Produces<PayrollItemDetail>();
        items.MapPost("/{id:guid}/reactivate", async (Guid id, PayrollItemService service, CancellationToken ct) => (await service.SetActiveAsync(id, true, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("ReactivatePayrollItem").Produces<PayrollItemDetail>();
        items.MapPost("/{id:guid}/confirm", async (Guid id, PayrollItemService service, CancellationToken ct) => (await service.ConfirmAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("ConfirmPayrollItem").Produces<PayrollItemDetail>();
        items.MapDelete("/{id:guid}", async (Guid id, PayrollItemService service, CancellationToken ct) => (await service.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("DeletePayrollItem");
    }

    /// <summary>Parâmetros do RH (CA 3, ADR 0016 M6): ler com <c>hr.view</c>; editar e "Marcar como conferido" com <c>settings.edit</c>.</summary>
    private static void MapHrParameters(RouteGroupBuilder parameters)
    {
        parameters.MapGet(string.Empty, async (HrParametersService service, CancellationToken ct) => Results.Ok(await service.GetAsync(ct)))
            .RequirePermission("hr.view").WithName("GetHrParameters").Produces<HrParametersView>();
        parameters.MapPut(string.Empty, async (HrParametersInput input, HrParametersService service, CancellationToken ct) => (await service.UpdateAsync(input, ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("UpdateHrParameters").Produces<HrParametersView>();
        parameters.MapPost("/confirm", async (HrParameterConfirmInput input, HrParametersService service, CancellationToken ct) => (await service.ConfirmAsync(input.Key, ct)).ToHttpResult())
            .RequirePermission("settings.edit").WithName("ConfirmHrParameter").Produces<HrParametersView>();
        parameters.MapGet("/payees", async (string? q, HrParametersService service, CancellationToken ct) => Results.Ok(await service.PayeesAsync(q, ct)))
            .RequirePermission("hr.view").WithName("SearchPayrollPayees").Produces<IReadOnlyList<PayrollPayeeOption>>();
    }

    /// <summary>
    /// Folha lançada (CA 4 a 16): o resumo com <c>hr.view</c>; valores só pelas rotas <c>/sensitive</c> (<c>hr.view_sensitive</c>, com
    /// <c>SensitiveRead</c>); criar com <c>hr.create</c> e editar, importar e as guias com <c>hr.edit</c>, os dois com
    /// <c>hr.view_sensitive</c> conferido no serviço. Holerite e guia precisam ser da folha da rota (IDOR, 404).
    /// </summary>
    private static void MapPayrollRuns(RouteGroupBuilder runs)
    {
        runs.MapGet(string.Empty, async (PayrollService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .RequirePermission("hr.view").WithName("ListPayrollRuns").Produces<IReadOnlyList<PayrollRunSummary>>();
        runs.MapGet("/sensitive", async (PayrollService service, CancellationToken ct) => Results.Ok(await service.TotalsAsync(ct)))
            .RequirePermission("hr.view_sensitive").WithName("ListPayrollRunTotalsSensitive").Produces<IReadOnlyList<PayrollRunTotals>>();
        runs.MapGet("/eligible", async (string? period, PayrollService service, CancellationToken ct) => (await service.EligibleAsync(period, ct)).ToHttpResult())
            .RequirePermission("hr.create").WithName("ListPayrollEligibleEmployees").Produces<IReadOnlyList<PayrollEligibleEmployee>>();
        runs.MapPost(string.Empty, async (PayrollRunCreateInput input, PayrollService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/hr/payroll-runs/" + saved.Id, saved)))
            .RequirePermission("hr.create").RequireIdempotencyKey().WithName("CreatePayrollRun").Produces<PayrollRunSummary>(StatusCodes.Status201Created);
        runs.MapGet("/{id:guid}", async (Guid id, PayrollService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view").WithName("GetPayrollRun").Produces<PayrollRunSummary>();

        runs.MapGet("/{id:guid}/payslips/sensitive", async (Guid id, PayrollService service, CancellationToken ct) => (await service.PayslipsAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("ListPayslipsSensitive").Produces<IReadOnlyList<PayslipRow>>();
        runs.MapGet("/{id:guid}/payslips/{payslipId:guid}/sensitive", async (Guid id, Guid payslipId, PayrollService service, CancellationToken ct) => (await service.PayslipAsync(id, payslipId, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("GetPayslipSensitive").Produces<PayslipDetail>();
        runs.MapGet("/{id:guid}/payslips/{payslipId:guid}/previous/sensitive", async (Guid id, Guid payslipId, PayrollService service, CancellationToken ct) => (await service.PreviousAsync(id, payslipId, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("GetPreviousPayslipSensitive").Produces<PayslipPrevious>();
        runs.MapPut("/{id:guid}/payslips/{payslipId:guid}", async (Guid id, Guid payslipId, PayslipInput input, PayrollService service, CancellationToken ct) => (await service.SavePayslipAsync(id, payslipId, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("SavePayslip").Produces<PayslipDetail>();

        runs.MapGet("/{id:guid}/charges/sensitive", async (Guid id, PayrollService service, CancellationToken ct) => (await service.ChargesAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("ListPayrollChargesSensitive").Produces<PayrollChargesView>();
        runs.MapPost("/{id:guid}/charges", async (Guid id, PayrollChargeInput input, PayrollService service, CancellationToken ct) => (await service.AddChargeAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("AddPayrollCharge").Produces<PayrollChargesView>();
        runs.MapPut("/{id:guid}/charges/{chargeId:guid}", async (Guid id, Guid chargeId, PayrollChargeInput input, PayrollService service, CancellationToken ct) => (await service.UpdateChargeAsync(id, chargeId, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("UpdatePayrollCharge").Produces<PayrollChargesView>();
        runs.MapDelete("/{id:guid}/charges/{chargeId:guid}", async (Guid id, Guid chargeId, PayrollService service, CancellationToken ct) => (await service.DeleteChargeAsync(id, chargeId, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("DeletePayrollCharge").Produces<PayrollChargesView>();

        runs.MapPost("/{id:guid}/import/preview", async (Guid id, IFormFile file, PayrollService service, CancellationToken ct) =>
                await ReadCsvAsync(file, ct) is { } content ? (await service.PreviewImportAsync(id, content, ct)).ToHttpResult() : TooLarge())
            .RequirePermission("hr.edit").DisableAntiforgery().WithName("PreviewPayrollImport").Produces<PayrollImportPreview>();
        runs.MapPost("/{id:guid}/import", async (Guid id, IFormFile file, PayrollService service, CancellationToken ct) =>
                await ReadCsvAsync(file, ct) is { } content ? (await service.ImportAsync(id, content, ct)).ToHttpResult() : TooLarge())
            .RequirePermission("hr.edit").DisableAntiforgery().WithName("ImportPayrollEntries").Produces<PayrollImportResult>();

        runs.MapGet("/{id:guid}/review/sensitive", async (Guid id, PayrollService service, CancellationToken ct) => (await service.ReviewAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("GetPayrollReviewSensitive").Produces<PayrollReview>();
    }

    /// <summary>
    /// Lê o arquivo em memória até 2 MB (CA 10): passou, null (o arquivo nunca vai para disco nem fica guardado). O conteúdo nunca é
    /// executado; só o leitor <see cref="PayrollCsv"/> o interpreta como texto.
    /// </summary>
    private static async Task<byte[]?> ReadCsvAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length > PayrollCsv.MaxBytes)
        {
            return null;
        }

        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > PayrollCsv.MaxBytes)
            {
                return null;
            }
        }

        return buffer.ToArray();
    }

    private static IResult TooLarge() =>
        Error.Validation("payroll_import_too_large", "O arquivo passa de 2 MB. Divida em partes menores.").ToProblem();
}
