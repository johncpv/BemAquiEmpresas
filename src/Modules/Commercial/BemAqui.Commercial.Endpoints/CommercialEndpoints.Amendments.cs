using BemAqui.BuildingBlocks.Web;
using BemAqui.Commercial.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Commercial.Endpoints;

internal static partial class CommercialEndpoints
{
    /// <summary>Aditivos (COM-07): registrar e enviar com projects.edit; aprovar e reprovar com projects.approve.</summary>
    private static void MapAmendments(RouteGroupBuilder contracts)
    {
        var amendments = contracts.MapGroup("/{contractId:guid}/amendments");
        amendments.MapGet(string.Empty, async (Guid contractId, AmendmentService service, CancellationToken ct) => Results.Ok(await service.ListAsync(contractId, ct)))
            .RequirePermission("projects.view").WithName("ListAmendments").Produces<IReadOnlyList<AmendmentSummary>>();
        amendments.MapGet("/context", async (Guid contractId, AmendmentService service, CancellationToken ct) => (await service.ContextAsync(contractId, ct)).ToHttpResult())
            .RequirePermission("projects.edit").WithName("GetAmendmentContext").Produces<AmendmentContext>();
        amendments.MapGet("/{id:guid}", async (Guid contractId, Guid id, AmendmentService service, CancellationToken ct) => (await service.GetAsync(contractId, id, ct)).ToHttpResult())
            .RequirePermission("projects.view").WithName("GetAmendment").Produces<AmendmentDetail>();
        amendments.MapPost(string.Empty, async (Guid contractId, AmendmentInput input, AmendmentService service, CancellationToken ct) =>
                (await service.CreateAsync(contractId, input, ct)).ToHttpResult(saved => Results.Created($"/api/v1/contracts/{contractId}/amendments/{saved.Id}", saved)))
            .RequirePermission("projects.edit").RequireIdempotencyKey().WithName("CreateAmendment").Produces<AmendmentDetail>(StatusCodes.Status201Created);
        amendments.MapPut("/{id:guid}", async (Guid contractId, Guid id, AmendmentInput input, AmendmentService service, CancellationToken ct) => (await service.UpdateAsync(contractId, id, input, ct)).ToHttpResult())
            .RequirePermission("projects.edit").WithName("UpdateAmendment").Produces<AmendmentDetail>();
        amendments.MapPost("/{id:guid}/submit", async (Guid contractId, Guid id, AmendmentVersionInput input, AmendmentService service, CancellationToken ct) => (await service.SubmitAsync(contractId, id, input.Version, ct)).ToHttpResult())
            .RequirePermission("projects.edit").WithName("SubmitAmendment").Produces<AmendmentDetail>();
        amendments.MapPost("/{id:guid}/approve", async (Guid contractId, Guid id, AmendmentVersionInput input, AmendmentService service, CancellationToken ct) => (await service.ApproveAsync(contractId, id, input.Version, ct)).ToHttpResult())
            .RequirePermission("projects.approve").WithName("ApproveAmendment").Produces<AmendmentDetail>();
        amendments.MapPost("/{id:guid}/reject", async (Guid contractId, Guid id, AmendmentCommentInput input, AmendmentService service, CancellationToken ct) => (await service.RejectAsync(contractId, id, input, ct)).ToHttpResult())
            .RequirePermission("projects.approve").WithName("RejectAmendment").Produces<AmendmentDetail>();
    }
}
