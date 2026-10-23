using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Production.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Production.Endpoints;

internal static partial class ProductionEndpoints
{
    /// <summary>
    /// App do operador (PRD-03a CA 28): módulo <c>shopfloor</c> (sem projeto próprio), ver com <c>shopfloor.view</c> e apontar com
    /// <c>shopfloor.create</c>. O funcionário é sempre o do usuário logado (CA 29); toque repetido não duplica (Idempotency-Key, CA 30).
    /// </summary>
    public static IEndpointRouteBuilder MapShopfloorEndpoints(this IEndpointRouteBuilder api)
    {
        var shopfloor = api.MapGroup("/shopfloor").WithTags("App do operador").RequireModule("shopfloor");
        shopfloor.MapGet("/queue", async (ShopfloorService service, CancellationToken ct) => (await service.QueueAsync(ct)).ToHttpResult())
            .RequirePermission("shopfloor.view").AllowPinSession().WithName("GetShopfloorQueue").Produces<ShopfloorQueueView>();
        shopfloor.MapGet("/orders", async (string? number, ShopfloorService service, CancellationToken ct) => (await service.SearchAsync(number, ct)).ToHttpResult())
            .RequirePermission("shopfloor.view").AllowPinSession().WithName("SearchShopfloorOrder").Produces<IReadOnlyList<ShopfloorQueueStep>>();

        var steps = shopfloor.MapGroup("/steps/{stepId:guid}");
        steps.MapGet(string.Empty, async (Guid stepId, ShopfloorService service, CancellationToken ct) => (await service.GetStepAsync(stepId, ct)).ToHttpResult())
            .RequirePermission("shopfloor.view").AllowPinSession().WithName("GetShopfloorStep").Produces<ShopfloorStepDetail>();
        steps.MapPost("/start", async (Guid stepId, ShopfloorService service, CancellationToken ct) => (await service.StartAsync(stepId, ct)).ToHttpResult())
            .RequirePermission("shopfloor.create").RequireIdempotencyKey().AllowPinSession().WithName("StartShopfloorStep").Produces<ShopfloorActionResult>();
        steps.MapPost("/pause", async (Guid stepId, ShopfloorPauseInput input, ShopfloorService service, CancellationToken ct) => (await service.PauseAsync(stepId, input, ct)).ToHttpResult())
            .RequirePermission("shopfloor.create").RequireIdempotencyKey().AllowPinSession().WithName("PauseShopfloorStep").Produces<ShopfloorActionResult>();
        steps.MapPost("/resume", async (Guid stepId, ShopfloorService service, CancellationToken ct) => (await service.ResumeAsync(stepId, ct)).ToHttpResult())
            .RequirePermission("shopfloor.create").RequireIdempotencyKey().AllowPinSession().WithName("ResumeShopfloorStep").Produces<ShopfloorActionResult>();
        steps.MapPost("/leave", async (Guid stepId, ShopfloorService service, CancellationToken ct) => (await service.LeaveAsync(stepId, ct)).ToHttpResult())
            .RequirePermission("shopfloor.create").RequireIdempotencyKey().AllowPinSession().WithName("LeaveShopfloorStep").Produces<ShopfloorActionResult>();
        steps.MapPost("/finish", async (Guid stepId, ShopfloorFinishInput input, ShopfloorService service, CancellationToken ct) => (await service.FinishAsync(stepId, input, ct)).ToHttpResult())
            .RequirePermission("shopfloor.create").RequireIdempotencyKey().AllowPinSession().WithName("FinishShopfloorStep").Produces<ShopfloorActionResult>();
        steps.MapPut("/checklist/{itemId:guid}", async (Guid stepId, Guid itemId, ShopfloorChecklistInput input, ShopfloorService service, CancellationToken ct) =>
                (await service.MarkChecklistAsync(stepId, itemId, input, ct)).ToHttpResult())
            .RequirePermission("shopfloor.create").AllowPinSession().WithName("MarkShopfloorChecklist").Produces<ShopfloorStepDetail>();

        // CA 6 (CA 14 da PRD-01): anexos técnicos da OS pela fila do operador, só leitura; fora da fila, 404.
        steps.MapGet("/attachments", async (Guid stepId, ShopfloorService service, CancellationToken ct) => (await service.AttachmentsAsync(stepId, ct)).ToHttpResult())
            .RequirePermission("shopfloor.view").AllowPinSession().WithName("ListShopfloorAttachments").Produces<IReadOnlyList<ShopfloorAttachment>>();
        steps.MapGet("/attachments/{attachmentId:guid}", async (Guid stepId, Guid attachmentId, ShopfloorService service, HttpContext http, CancellationToken ct) =>
            {
                var result = await service.DownloadAsync(stepId, attachmentId, ct);
                if (result.IsFailure)
                {
                    return result.Error!.ToProblem();
                }

                AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
                return Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
            })
            .RequirePermission("shopfloor.view").AllowPinSession().WithName("DownloadShopfloorAttachment");
        return api;
    }

    /// <summary>
    /// Supervisor (PRD-03a CA 15, 18 a 22, 31): aba Apontamentos com production.view; "Apontar por funcionário" com production.edit
    /// (o serviço confere shopfloor.create); corrigir e aprovar etapa com production.approve; abertos há muito tempo com production.view.
    /// </summary>
    private static void MapLabor(RouteGroupBuilder production)
    {
        var entries = production.MapGroup("/production-orders/{id:guid}/labor-entries");
        entries.MapGet(string.Empty, async (Guid id, SupervisorLaborService service, CancellationToken ct) => (await service.ListAsync(id, ct)).ToHttpResult())
            .RequirePermission("production.view").WithName("ListOrderLaborEntries").Produces<OrderLaborView>();
        entries.MapPost(string.Empty, async (Guid id, LaborEntryInput input, SupervisorLaborService service, CancellationToken ct) => (await service.RecordAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("production.edit").RequireIdempotencyKey().WithName("RecordLaborEntry").Produces<OrderLaborView>();
        entries.MapPost("/{entryId:guid}/correct", async (Guid id, Guid entryId, LaborCorrectionInput input, SupervisorLaborService service, CancellationToken ct) =>
                (await service.CorrectAsync(id, entryId, input, ct)).ToHttpResult())
            .RequirePermission("production.approve").WithName("CorrectLaborEntry").Produces<OrderLaborView>();
        production.MapPost("/production-orders/{id:guid}/steps/{stepId:guid}/approve", async (Guid id, Guid stepId, ApproveStepInput input, SupervisorLaborService service, CancellationToken ct) =>
                (await service.ApproveStepAsync(id, stepId, input, ct)).ToHttpResult())
            .RequirePermission("production.approve").WithName("ApproveFabricationStep").Produces<OrderLaborView>();
        production.MapGet("/labor-entries/open", async (SupervisorLaborService service, CancellationToken ct) => Results.Ok(await service.OpenAsync(ct)))
            .RequirePermission("production.view").WithName("ListOpenLaborEntries").Produces<OpenLaborList>();
        production.MapGet("/labor-entries/employees", async (SupervisorLaborService service, CancellationToken ct) => Results.Ok(await service.EmployeesAsync(ct)))
            .RequirePermission("production.edit").WithName("ListLaborEmployees").Produces<IReadOnlyList<LaborEmployeeOption>>();
        MapLaborCosts(production);
    }

    /// <summary>
    /// Custo de mão de obra (PRD-04 CA 6, 14, 16 e 17): consultas de custo no módulo <c>production</c> (grupo), com
    /// <c>production.view</c> e <c>projects.view_cost</c> (a policy responde <c>module_not_released</c> com <c>projects</c> não liberado); recálculo do "Sem custo-hora" com <c>production.approve</c>.
    /// </summary>
    private static void MapLaborCosts(RouteGroupBuilder production)
    {
        production.MapGet("/production-orders/{id:guid}/labor-costs", async (Guid id, LaborCostService service, CancellationToken ct) => (await service.OrderAsync(id, ct)).ToHttpResult())
            .RequirePermission("production.view").RequirePermission("projects.view_cost").WithName("GetOrderLaborCosts").Produces<OrderLaborCostView>();
        production.MapGet("/production-orders/{id:guid}/labor-entries/{entryId:guid}/cost-history", async (Guid id, Guid entryId, LaborCostService service, CancellationToken ct) =>
                (await service.HistoryAsync(id, entryId, ct)).ToHttpResult())
            .RequirePermission("production.view").RequirePermission("projects.view_cost").WithName("GetLaborEntryCostHistory").Produces<IReadOnlyList<LaborCostHistoryView>>();
        production.MapGet("/labor-costs/missing", async (Guid? positionId, Guid? orderId, LaborCostService service, CancellationToken ct) => Results.Ok(await service.MissingAsync(positionId, orderId, ct)))
            .RequirePermission("production.approve").WithName("GetMissingLaborCosts").Produces<LaborCostMissingView>();
        production.MapPost("/labor-costs/recalculate", async (LaborRecalculationInput input, LaborCostService service, CancellationToken ct) => (await service.RecalculateAsync(input, ct)).ToHttpResult())
            .RequirePermission("production.approve").WithName("RecalculateMissingLaborCosts").Produces<LaborRecalculationResult>();
    }
}