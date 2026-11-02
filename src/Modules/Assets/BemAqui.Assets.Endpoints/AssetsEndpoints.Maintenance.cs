using BemAqui.Assets.Application;
using BemAqui.Assets.Contracts;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Assets.Endpoints;

/// <summary>
/// Manutenção (PAT-03a CA 13): ler com <c>assets.view</c>; abrir a OS com <c>assets.create</c> (com <c>Idempotency-Key</c>); plano, horímetro,
/// início, checklist, conclusão e cancelamento com <c>assets.edit</c>. O plano é lido pela rota do bem e o anexo pela rota da OS (IDOR: de outro,
/// 404). Fora da sessão por PIN (sem <c>[PinSessionAllowed]</c>).
/// </summary>
internal static partial class AssetsEndpoints
{
    private static void MapMaintenance(RouteGroupBuilder root)
    {
        var assets = root.MapGroup("/assets");
        assets.MapGet("/{id:guid}/maintenance", async (Guid id, MaintenanceService service, CancellationToken ct) => (await service.ForAssetAsync(id, ct)).ToHttpResult())
            .RequirePermission("assets.view").WithName("GetAssetMaintenance").Produces<AssetMaintenanceView>();
        assets.MapGet("/{id:guid}/plans/{planId:guid}", async (Guid id, Guid planId, MaintenanceService service, CancellationToken ct) => (await service.GetPlanAsync(id, planId, ct)).ToHttpResult())
            .RequirePermission("assets.view").WithName("GetMaintenancePlan").Produces<MaintenancePlanView>();
        assets.MapPost("/{id:guid}/plans", async (Guid id, MaintenancePlanInput input, MaintenanceService service, CancellationToken ct) =>
                (await service.CreatePlanAsync(id, input, ct)).ToHttpResult(saved => Results.Created($"/api/v1/assets/assets/{id}/plans/{saved.Id}", saved)))
            .RequirePermission("assets.edit").WithName("CreateMaintenancePlan").Produces<MaintenancePlanView>(StatusCodes.Status201Created);
        assets.MapPut("/{id:guid}/plans/{planId:guid}", async (Guid id, Guid planId, MaintenancePlanInput input, MaintenanceService service, CancellationToken ct) =>
                (await service.UpdatePlanAsync(id, planId, input, ct)).ToHttpResult())
            .RequirePermission("assets.edit").WithName("UpdateMaintenancePlan").Produces<MaintenancePlanView>();
        assets.MapGet("/{id:guid}/meter-readings", async (Guid id, MaintenanceService service, CancellationToken ct) => (await service.ReadingsAsync(id, ct)).ToHttpResult())
            .RequirePermission("assets.view").WithName("ListAssetMeterReadings").Produces<IReadOnlyList<MeterReadingView>>();
        assets.MapPost("/{id:guid}/meter-readings", async (Guid id, MeterReadingInput input, MaintenanceService service, CancellationToken ct) =>
                (await service.AddReadingAsync(id, input, ct)).ToHttpResult(saved => Results.Created($"/api/v1/assets/assets/{id}/meter-readings", saved)))
            .RequirePermission("assets.edit").WithName("AddAssetMeterReading").Produces<MeterReadingResult>(StatusCodes.Status201Created);

        var orders = root.MapGroup("/maintenance-orders");
        orders.MapGet(string.Empty, async (Guid? assetId, Guid? workCenterId, MaintenanceOrderType? type, MaintenanceOrderStatus? status, MaintenanceService service, CancellationToken ct) =>
                Results.Ok(await service.OverviewAsync(new MaintenanceListQuery(assetId, workCenterId, type, status), ct)))
            .RequirePermission("assets.view").WithName("ListMaintenanceOrders").Produces<MaintenanceOverview>();
        orders.MapGet("/{id:guid}", async (Guid id, MaintenanceService service, CancellationToken ct) => (await service.GetOrderAsync(id, ct)).ToHttpResult())
            .RequirePermission("assets.view").WithName("GetMaintenanceOrder").Produces<MaintenanceOrderDetail>();
        orders.MapPost(string.Empty, async (MaintenanceOrderInput input, MaintenanceService service, CancellationToken ct) =>
                (await service.OpenAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/assets/maintenance-orders/" + saved.Id, saved)))
            .RequirePermission("assets.create").RequireIdempotencyKey().WithName("OpenMaintenanceOrder").Produces<MaintenanceOrderDetail>(StatusCodes.Status201Created);
        orders.MapPost("/{id:guid}/start", async (Guid id, MaintenanceStartInput input, MaintenanceService service, CancellationToken ct) => (await service.StartAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("assets.edit").WithName("StartMaintenanceOrder").Produces<MaintenanceOrderDetail>();
        orders.MapPut("/{id:guid}/checklist/{itemId:guid}", async (Guid id, Guid itemId, MaintenanceChecklistMarkInput input, MaintenanceService service, CancellationToken ct) =>
                (await service.MarkAsync(id, itemId, input, ct)).ToHttpResult())
            .RequirePermission("assets.edit").WithName("MarkMaintenanceChecklistItem").Produces<MaintenanceOrderDetail>();
        orders.MapPost("/{id:guid}/complete", async (Guid id, MaintenanceCompleteInput input, MaintenanceService service, CancellationToken ct) => (await service.CompleteAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("assets.edit").WithName("CompleteMaintenanceOrder").Produces<MaintenanceOrderDetail>();
        orders.MapPost("/{id:guid}/cancel", async (Guid id, MaintenanceCancelInput input, MaintenanceService service, CancellationToken ct) => (await service.CancelAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("assets.edit").WithName("CancelMaintenanceOrder").Produces<MaintenanceOrderDetail>();

        // PAT-03b: sugestões da OS, reagendar/planejar (valor combinado, horas paradas), custo por natureza e o relatório, tudo com assets.edit
        // (as sugestões com assets.view, o valor sugerido só com assets.edit).
        orders.MapGet("/defaults", async (Guid assetId, Guid? planId, MaintenanceService service, CancellationToken ct) => (await service.DefaultsAsync(assetId, planId, ct)).ToHttpResult())
            .RequirePermission("assets.view").WithName("GetMaintenanceOrderDefaults").Produces<MaintenanceDefaults>();
        orders.MapPut("/{id:guid}/schedule", async (Guid id, MaintenanceScheduleInput input, MaintenanceService service, CancellationToken ct) => (await service.ScheduleAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("assets.edit").WithName("ScheduleMaintenanceOrder").Produces<MaintenanceOrderDetail>();
        orders.MapGet("/{id:guid}/costs", async (Guid id, MaintenanceCostService service, CancellationToken ct) => (await service.ListAsync(id, ct)).ToHttpResult())
            .RequirePermission("assets.edit").WithName("ListMaintenanceOrderCosts").Produces<MaintenanceCostsView>();
        orders.MapPost("/{id:guid}/costs", async (Guid id, MaintenanceCostInput input, MaintenanceCostService service, CancellationToken ct) => (await service.AddAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("assets.edit").WithName("AddMaintenanceOrderCost").Produces<MaintenanceCostsView>();
        orders.MapDelete("/{id:guid}/costs/{costId:guid}", async (Guid id, Guid costId, uint? version, MaintenanceCostService service, CancellationToken ct) =>
                (await service.RemoveAsync(id, costId, version, ct)).ToHttpResult())
            .RequirePermission("assets.edit").WithName("RemoveMaintenanceOrderCost").Produces<MaintenanceCostsView>();

        var costs = root.MapGroup("/maintenance-costs");
        costs.MapGet(string.Empty, async (string? from, string? to, Guid? assetId, Guid? workCenterId, MaintenanceCostService service, CancellationToken ct) =>
                (await service.ReportAsync(new MaintenanceCostQuery(from, to, assetId, workCenterId), ct)).ToHttpResult())
            .RequirePermission("assets.edit").WithName("GetMaintenanceCostReport").Produces<MaintenanceCostReport>();
        costs.MapGet("/orders", async (string? from, string? to, string? month, Guid? assetId, Guid? workCenterId, MaintenanceOrderType? type, MaintenanceCostService service, CancellationToken ct) =>
                (await service.OrdersAsync(new MaintenanceCostQuery(from, to, assetId, workCenterId, type, month), ct)).ToHttpResult())
            .RequirePermission("assets.edit").WithName("ListMaintenanceCostOrders").Produces<MaintenanceCostOrders>();
        assets.MapGet("/{id:guid}/maintenance-cost", async (Guid id, MaintenanceCostService service, CancellationToken ct) => Results.Ok(await service.ForAssetAsync(id, ct)))
            .RequirePermission("assets.edit").WithName("GetAssetMaintenanceCost").Produces<AssetMaintenanceCost>();

        orders.MapGet("/{id:guid}/attachments", async (Guid id, MaintenanceService service, CancellationToken ct) => (await service.FilesAsync(id, ct)).ToHttpResult())
            .RequirePermission("assets.view").WithName("ListMaintenanceOrderFiles").Produces<MaintenanceOrderFiles>();
        orders.MapPost("/{id:guid}/attachments", async (Guid id, IFormFile file, MaintenanceService service, CancellationToken ct) =>
            {
                await using var content = file.OpenReadStream();
                return (await service.UploadAsync(id, file.FileName, content, ct)).ToHttpResult(dto => Results.Created($"/api/v1/assets/maintenance-orders/{id}/attachments/{dto.Id}/content", dto));
            })
            .RequirePermission("assets.edit").DisableAntiforgery().WithName("UploadMaintenanceOrderFile").Produces<AttachmentDto>(StatusCodes.Status201Created);
        orders.MapGet("/{id:guid}/attachments/{attachmentId:guid}/content", async (Guid id, Guid attachmentId, MaintenanceService service, HttpContext http, CancellationToken ct) =>
            {
                var result = await service.FileAsync(id, attachmentId, ct);
                if (result.IsFailure)
                {
                    return result.Error!.ToProblem();
                }

                AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
                return Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
            })
            .RequirePermission("assets.view").WithName("DownloadMaintenanceOrderFile");
    }
}
