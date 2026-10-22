using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Production.Application;
using BemAqui.Production.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Production.Endpoints;

internal static partial class ProductionEndpoints
{
    /// <summary>
    /// Qualidade (PRD-05 CA 2 a 7, 13 e 14): fila, etapa, checklist, início da inspeção, aprovar e reprovar, todos com
    /// <c>production.approve_quality</c> (D6) e separados dos da supervisão, para a PRD-03b pôr só estes na lista permitida da sessão
    /// por PIN (ADR 0026 §4). Toque repetido não duplica (Idempotency-Key); decisão concorrente, só uma grava (xmin).
    /// </summary>
    private static void MapQuality(RouteGroupBuilder production)
    {
        var quality = production.MapGroup("/quality").WithTags("Qualidade");
        quality.MapGet("/queue", async (string? filter, QualityService service, CancellationToken ct) => Results.Ok(await service.QueueAsync(filter, ct)))
            .RequirePermission("production.approve_quality").AllowPinSession().WithName("GetQualityQueue").Produces<QualityQueueView>();
        var steps = quality.MapGroup("/steps/{stepId:guid}");
        steps.MapGet(string.Empty, async (Guid stepId, QualityService service, CancellationToken ct) => (await service.GetAsync(stepId, ct)).ToHttpResult())
            .RequirePermission("production.approve_quality").AllowPinSession().WithName("GetQualityStep").Produces<QualityStepDetail>();
        steps.MapPut("/checklist/{itemId:guid}", async (Guid stepId, Guid itemId, QualityChecklistInput input, QualityService service, CancellationToken ct) =>
                (await service.MarkAsync(stepId, itemId, input, ct)).ToHttpResult())
            .RequirePermission("production.approve_quality").AllowPinSession().WithName("MarkQualityChecklist").Produces<QualityStepDetail>();
        steps.MapPost("/start", async (Guid stepId, QualityService service, CancellationToken ct) => (await service.StartAsync(stepId, ct)).ToHttpResult())
            .RequirePermission("production.approve_quality").RequireIdempotencyKey().AllowPinSession().WithName("StartQualityInspection").Produces<QualityStepDetail>();
        steps.MapPost("/approve", async (Guid stepId, QualityApproveInput input, QualityService service, CancellationToken ct) => (await service.ApproveAsync(stepId, input, ct)).ToHttpResult())
            .RequirePermission("production.approve_quality").RequireIdempotencyKey().AllowPinSession().WithName("ApproveQuality").Produces<QualityDecisionResult>();
        steps.MapPost("/reject", async (Guid stepId, QualityRejectInput input, QualityService service, CancellationToken ct) => (await service.RejectAsync(stepId, input, ct)).ToHttpResult())
            .RequirePermission("production.approve_quality").RequireIdempotencyKey().AllowPinSession().WithName("RejectQuality").Produces<QualityDecisionResult>();

        // Aba Qualidade da OS (CA 7) e a NC pela OS (IDOR, CA 12), com production.view.
        production.MapGet("/production-orders/{id:guid}/quality", async (Guid id, QualityService service, NonConformityService nonConformities, CancellationToken ct) =>
                (await service.OrderAsync(id, nonConformities, ct)).ToHttpResult())
            .RequirePermission("production.view").WithName("GetOrderQuality").Produces<OrderQualityView>();
        production.MapGet("/production-orders/{id:guid}/non-conformities/{ncId:guid}", async (Guid id, Guid ncId, NonConformityService service, CancellationToken ct) =>
                (await service.GetAsync(ncId, id, ct)).ToHttpResult())
            .RequirePermission("production.view").WithName("GetOrderNonConformity").Produces<NonConformityDetail>();
        MapNonConformities(production.MapGroup("/non-conformities").WithTags("Qualidade"));
    }

    /// <summary>
    /// Não conformidades (CA 8 a 12, 15): ver com production.view, mudar com production.edit, custo com projects.view_cost (no serviço),
    /// anexos do dono <c>NonConformity</c> (escrita com production.edit ou production.approve_quality, no serviço de anexos).
    /// </summary>
    private static void MapNonConformities(RouteGroupBuilder ncs)
    {
        ncs.MapGet(string.Empty, async (string? filter, string? situation, Guid? orderId, Guid? projectId, int? page, int? pageSize, NonConformityService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new NonConformityListQuery(filter, situation, orderId, projectId), new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)))
            .RequirePermission("production.view").WithName("ListNonConformities").Produces<NonConformityPage>();
        ncs.MapGet("/{id:guid}", async (Guid id, NonConformityService service, CancellationToken ct) => (await service.GetAsync(id, null, ct)).ToHttpResult())
            .RequirePermission("production.view").WithName("GetNonConformity").Produces<NonConformityDetail>();
        ncs.MapPut("/{id:guid}", async (Guid id, NonConformityUpdateInput input, NonConformityService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("production.edit").WithName("UpdateNonConformity").Produces<NonConformityDetail>();
        ncs.MapPost("/{id:guid}/start-action", async (Guid id, NonConformityActionInput input, NonConformityService service, CancellationToken ct) =>
                (await service.StartActionAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("production.edit").WithName("StartNonConformityAction").Produces<NonConformityDetail>();
        ncs.MapPost("/{id:guid}/close", async (Guid id, NonConformityCloseInput input, NonConformityService service, CancellationToken ct) => (await service.CloseAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("production.edit").WithName("CloseNonConformity").Produces<NonConformityDetail>();
        ncs.MapPut("/{id:guid}/cost", async (Guid id, NonConformityCostInput input, NonConformityService service, CancellationToken ct) => (await service.SetCostAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("production.edit").WithName("SetNonConformityCost").Produces<NonConformityDetail>();
        ncs.MapGet("/{id:guid}/history", async (Guid id, int? page, int? pageSize, AuditHistory history, CancellationToken ct) =>
                (await history.GetAsync(nameof(NonConformity), id.ToString(), new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)).ToHttpResult())
            .RequirePermission("production.view").WithName("GetNonConformityHistory").Produces<PagedList<AuditEntryDto>>();

        var attachments = ncs.MapGroup("/{id:guid}/attachments");
        attachments.MapGet(string.Empty, async (Guid id, NonConformityService service, AttachmentService files, CancellationToken ct) =>
                await service.ExistsAsync(id, ct) ? (await files.ListAsync(NonConformityService.AttachmentOwner, id, ct)).ToHttpResult() : NonConformityService.NotFound().ToProblem())
            .RequirePermission("production.view").WithName("ListNonConformityAttachments").Produces<IReadOnlyList<AttachmentDto>>();
        attachments.MapPost(string.Empty, async (Guid id, IFormFile file, AttachmentService files, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                return (await files.UploadAsync(NonConformityService.AttachmentOwner, id, file.FileName, content, ct)).ToHttpResult(dto => Results.Created($"/api/v1/attachments/{dto.Id}", dto));
            })
            .DisableAntiforgery() // CSRF por Origin + header (FND-04), como o upload genérico
            .RequirePermission("production.view").AllowPinSession().WithName("UploadNonConformityAttachment").Produces<AttachmentDto>(StatusCodes.Status201Created);
        attachments.MapGet("/{attachmentId:guid}", async (Guid id, Guid attachmentId, AttachmentService files, HttpContext http, CancellationToken ct) =>
            {
                var result = await files.DownloadCheckedAsync(NonConformityService.AttachmentOwner, id, attachmentId, ct);
                if (result.IsFailure)
                {
                    return result.Error!.ToProblem();
                }

                AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
                return Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
            })
            .RequirePermission("production.view").WithName("DownloadNonConformityAttachment");
    }
}