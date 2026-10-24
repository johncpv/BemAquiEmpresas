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
    // PRD-06b CA 17: as linhas e as séries entram no histórico do romaneio (antes e depois, com placa e motorista mascarados).
    private static readonly string[] ShipmentHistoryChildren = [nameof(ShipmentLine), nameof(ShipmentLineSerial)];

    /// <summary>
    /// Romaneio de expedição (PRD-06b CA 15): ver com production.view; montar e alterar o rascunho com production.create (anexos também,
    /// dono Shipment); confirmar e cancelar com production.edit; confirmar o estoque geral com inventory.approve (outra pessoa, no serviço);
    /// estornar com production.approve. A linha é conferida pelo id do romaneio (IDOR). Fora do /fab (sem PIN).
    /// </summary>
    private static void MapShipments(RouteGroupBuilder shipments)
    {
        shipments.MapGet(string.Empty, async (Guid? projectId, string? filter, string? situation, int? page, int? pageSize, ShipmentService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new ShipmentListQuery(projectId, filter, situation), new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)))
            .RequirePermission("production.view").WithName("ListShipments").Produces<ShipmentPage>();
        shipments.MapGet("/ready", async (ShipmentService service, CancellationToken ct) => Results.Ok(await service.ReadyAsync(ct)))
            .RequirePermission("production.view").WithName("ListReadyToShip").Produces<IReadOnlyList<ReadyToShipProject>>();
        shipments.MapGet("/form", async (Guid projectId, Guid? productionOrderId, ShipmentService service, CancellationToken ct) => (await service.FormAsync(projectId, productionOrderId, ct)).ToHttpResult())
            .RequirePermission("production.create").WithName("GetShipmentForm").Produces<ShipmentForm>();
        shipments.MapGet("/general-stock", async (string? q, ShipmentService service, CancellationToken ct) => Results.Ok(await service.GeneralStockAsync(q, ct)))
            .RequirePermission("production.create").WithName("ListShipmentGeneralStock").Produces<IReadOnlyList<ReadyToShipLine>>();
        shipments.MapGet("/carriers", async (string? q, ShipmentService service, CancellationToken ct) => Results.Ok(await service.CarriersAsync(q, null, ct)))
            .RequirePermission("production.create").WithName("ListShipmentCarriers").Produces<IReadOnlyList<CarrierOption>>();
        shipments.MapPost(string.Empty, async (ShipmentInput input, ShipmentService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created("/api/v1/production/shipments/" + saved.Id, saved)))
            .RequirePermission("production.create").RequireIdempotencyKey().WithName("CreateShipment").Produces<ShipmentDetail>(StatusCodes.Status201Created);
        shipments.MapGet("/{id:guid}", async (Guid id, ShipmentService service, CancellationToken ct) => (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission("production.view").WithName("GetShipment").Produces<ShipmentDetail>();
        shipments.MapPut("/{id:guid}", async (Guid id, ShipmentInput input, ShipmentService service, CancellationToken ct) => (await service.UpdateAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("production.create").WithName("UpdateShipment").Produces<ShipmentDetail>();
        shipments.MapGet("/{id:guid}/lines/{lineId:guid}", async (Guid id, Guid lineId, ShipmentService service, CancellationToken ct) => (await service.LineAsync(id, lineId, ct)).ToHttpResult())
            .RequirePermission("production.view").WithName("GetShipmentLine").Produces<ShipmentLineView>();
        shipments.MapGet("/{id:guid}/impact", async (Guid id, ShipmentService service, CancellationToken ct) => (await service.ImpactAsync(id, ct)).ToHttpResult())
            .RequirePermission("production.view").WithName("GetShipmentImpact").Produces<ShipmentImpact>();
        shipments.MapGet("/{id:guid}/pdf", async (Guid id, ShipmentService service, HttpContext http, CancellationToken ct) =>
            {
                var result = await service.PdfAsync(id, ct);
                if (result.IsFailure)
                {
                    return result.Error!.ToProblem();
                }

                AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
                return Results.File(result.Value.Content, "application/pdf", result.Value.FileName);
            })
            .RequirePermission("production.view").WithName("DownloadShipmentPdf");
        shipments.MapPost("/{id:guid}/confirm", async (Guid id, ShipmentDecisionInput input, ShipmentService service, CancellationToken ct) => (await service.ConfirmAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("production.edit").WithName("ConfirmShipment").Produces<ShipmentDetail>();
        shipments.MapPost("/{id:guid}/confirm-stock", async (Guid id, ShipmentDecisionInput input, ShipmentService service, CancellationToken ct) =>
                (await service.ConfirmStockAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("inventory.approve").WithName("ConfirmShipmentGeneralStock").Produces<ShipmentDetail>();
        shipments.MapPost("/{id:guid}/cancel", async (Guid id, ShipmentReasonInput input, ShipmentService service, CancellationToken ct) => (await service.CancelAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("production.edit").WithName("CancelShipment").Produces<ShipmentDetail>();
        shipments.MapPost("/{id:guid}/reverse", async (Guid id, ShipmentReasonInput input, ShipmentService service, CancellationToken ct) => (await service.ReverseAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("production.approve").WithName("ReverseShipment").Produces<ShipmentDetail>();
        shipments.MapGet("/{id:guid}/history", async (Guid id, int? page, int? pageSize, AuditHistory history, CancellationToken ct) =>
                (await history.GetWithChildrenAsync(nameof(Shipment), id.ToString(), ShipmentHistoryChildren, "shipmentId", new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)).ToHttpResult())
            .RequirePermission("production.view").WithName("GetShipmentHistory").Produces<PagedList<RelatedAuditEntryDto>>();
    }
}
