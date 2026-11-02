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
    /// Rescisão (FLH-05 CA 1 a 12): a aba do funcionário com <c>hr.view</c> (sem valor, salvo o saldo do FGTS com <c>hr.view_sensitive</c>);
    /// simular e calcular com <c>hr.edit</c> e <c>hr.view_sensitive</c> no serviço; o termo de conferência com <c>hr.view_sensitive</c>; o
    /// estorno do desligamento com <c>hr.edit</c>; fechar pela rota da folha (<c>hr.approve</c>, 2FA de novo, RH-02c).
    /// </summary>
    private static void MapTerminations(RouteGroupBuilder hr, RouteGroupBuilder employees)
    {
        employees.MapGet("/{id:guid}/termination", async (Guid id, TerminationService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("hr.view").WithName("GetEmployeeTermination").Produces<TerminationView>();
        employees.MapPost("/{id:guid}/termination/simulate", async (Guid id, TerminationRequestInput input, TerminationService service, CancellationToken ct) =>
                (await service.SimulateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("SimulateTermination").Produces<TerminationResultView>();
        employees.MapPost("/{id:guid}/termination/calculate", async (Guid id, TerminationRequestInput input, TerminationService service, CancellationToken ct) =>
                (await service.CalculateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").RequireIdempotencyKey().WithName("CalculateTermination").Produces<TerminationCalculationResult>();

        var terminations = hr.MapGroup("/terminations");
        terminations.MapGet("/{id:guid}/term", async (Guid id, TerminationService service, HttpContext http, CancellationToken ct) => File(http, await service.TermAsync(id, ct)))
            .RequirePermission("hr.view_sensitive").WithName("DownloadTerminationTerm").Produces(StatusCodes.Status200OK, contentType: PayslipDocumentService.PdfContentType);
        terminations.MapPost("/{id:guid}/cancel", async (Guid id, TerminationCancelInput input, TerminationService service, CancellationToken ct) => (await service.CancelAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("hr.edit").WithName("CancelTermination").Produces<TerminationView>();
    }
}
