using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Web;
using BemAqui.HR.Application;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.HR.Endpoints;

internal static partial class HrEndpoints
{
    /// <summary>
    /// Conferência paralela da folha (FLH-06): a tela, o histórico e o de-para sem valor com <c>hr.view</c>; comparar, ver os valores, classificar,
    /// o termo e os casos de correção com <c>hr.view_sensitive</c>; o de-para editado com <c>hr.edit</c>. Anexar o termo, aceitar, virar e voltar
    /// exigem <c>hr.view_sensitive</c> na rota e o Proprietário no serviço (403 <c>forbidden</c>).
    /// </summary>
    private static void MapPayrollParallel(RouteGroupBuilder parallel)
    {
        parallel.MapGet(string.Empty, async (PayrollParallelService service, CancellationToken ct) => Results.Ok(await service.OverviewAsync(ct)))
            .RequirePermission("hr.view").WithName("GetPayrollParallelOverview").Produces<PayrollParallelOverview>();
        parallel.MapGet("/history", async (PayrollParallelService service, CancellationToken ct) => Results.Ok(await service.HistoryAsync(ct)))
            .RequirePermission("hr.view").WithName("ListPayrollParallelHistory").Produces<IReadOnlyList<PayrollParallelHistoryEntry>>();
        parallel.MapPost("/compare", async (PayrollParallelCompareInput input, PayrollParallelService service, CancellationToken ct) => (await service.CompareAsync(input, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("ComparePayrollParallel").Produces<PayrollParallelDetail>();
        parallel.MapGet("/{id:guid}/sensitive", async (Guid id, PayrollParallelService service, CancellationToken ct) => (await service.DetailAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("GetPayrollParallelSensitive").Produces<PayrollParallelDetail>();
        parallel.MapPut("/{id:guid}/differences/{differenceId:guid}", async (Guid id, Guid differenceId, PayrollDifferenceInput input, PayrollParallelService service, CancellationToken ct) =>
                (await service.ClassifyAsync(id, differenceId, input, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").WithName("ClassifyPayrollDifference").Produces<PayrollDifferenceResult>();
        parallel.MapGet("/{id:guid}/correction-cases", async (Guid id, PayrollParallelService service, HttpContext http, CancellationToken ct) => ParallelFile(http, await service.CorrectionCasesAsync(id, ct)))
            .RequirePermission("hr.view_sensitive").WithName("DownloadPayrollCorrectionCases").Produces(StatusCodes.Status200OK, contentType: PayrollParallelService.JsonContentType);
        parallel.MapGet("/{id:guid}/term", async (Guid id, PayrollParallelService service, HttpContext http, CancellationToken ct) => ParallelFile(http, await service.TermAsync(id, ct)))
            .RequirePermission("hr.view_sensitive").WithName("DownloadPayrollParallelTerm").Produces(StatusCodes.Status200OK, contentType: PayrollParallelService.PdfContentType);
        parallel.MapPost("/{id:guid}/term", async (Guid id, IFormFile file, PayrollParallelService service, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                return (await service.AttachTermAsync(id, file.FileName, content, ct)).ToHttpResult(dto => Results.Created($"/api/v1/hr/payroll-parallel/{id}/term/{dto.Id}", dto));
            })
            .RequirePermission("hr.view_sensitive").DisableAntiforgery().WithName("AttachPayrollParallelTerm").Produces<AttachmentDto>(StatusCodes.Status201Created);
        parallel.MapPost("/{id:guid}/accept", async (Guid id, PayrollParallelAcceptInput input, PayrollParallelService service, CancellationToken ct) => (await service.AcceptAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").RequireIdempotencyKey().WithName("AcceptPayrollParallel").Produces<PayrollParallelDetail>();
        parallel.MapPost("/switch", async (PayrollModeSwitchInput input, PayrollParallelService service, CancellationToken ct) => (await service.SwitchAsync(input, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").RequireIdempotencyKey().WithName("SwitchPayrollToCalculated").Produces<PayrollModeResult>();
        parallel.MapPost("/revert", async (PayrollModeRevertInput input, PayrollParallelService service, CancellationToken ct) => (await service.RevertAsync(input, ct)).ToHttpResult())
            .RequirePermission("hr.view_sensitive").RequireIdempotencyKey().WithName("RevertPayrollToEntered").Produces<PayrollModeResult>();

        parallel.MapGet("/mappings", async (PayrollParallelService service, CancellationToken ct) => Results.Ok(await service.MappingsListAsync(ct)))
            .RequirePermission("hr.view").WithName("ListPayrollItemMappings").Produces<IReadOnlyList<PayrollItemMappingRow>>();
        parallel.MapPut("/mappings", async (PayrollItemMappingInput input, PayrollParallelService service, CancellationToken ct) => (await service.SaveMappingAsync(input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("SavePayrollItemMapping").Produces<IReadOnlyList<PayrollItemMappingRow>>();
        parallel.MapDelete("/mappings/{id:guid}", async (Guid id, PayrollParallelService service, CancellationToken ct) => (await service.DeleteMappingAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("DeletePayrollItemMapping").Produces<IReadOnlyList<PayrollItemMappingRow>>();
    }

    private static IResult ParallelFile(HttpContext http, Result<PayrollParallelFile> result)
    {
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
        return Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }
}
