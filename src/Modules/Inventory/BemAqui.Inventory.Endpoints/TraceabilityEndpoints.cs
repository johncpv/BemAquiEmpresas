using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Inventory.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Inventory.Endpoints;

/// <summary>
/// Lotes e rastreabilidade (EST-04): busca "Série ou lote", série → origem, lote → destino e a exportação CSV com <c>inventory.view</c>; a
/// correção do lote com <c>inventory.edit</c>. Módulo <c>inventory</c> no grupo (403 <c>module_not_released</c>), <c>perm:</c> em cada rota e
/// IDOR pelo item (lote ou série de outro item respondem 404). Nenhum DTO traz custo; o cliente sai pelo <c>CustomerNameMasker</c>.
/// </summary>
internal static class TraceabilityEndpoints
{
    public static IEndpointRouteBuilder MapTraceabilityEndpoints(this IEndpointRouteBuilder api)
    {
        var inventory = api.MapGroup("/inventory").WithTags("Estoque").RequireModule("inventory");
        inventory.MapGet("/traceability", async (string? q, TraceabilityService service, CancellationToken ct) => (await service.SearchAsync(q, ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("SearchTraceability").Produces<TraceSearchResult>();

        var lots = inventory.MapGroup("/items/{itemId:guid}/lots");
        lots.MapGet(string.Empty, async (Guid itemId, LotService service, CancellationToken ct) => (await service.ListAsync(itemId, ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("ListItemLots").Produces<IReadOnlyList<ItemLotRow>>();
        lots.MapGet("/{lotId:guid}", async (Guid itemId, Guid lotId, LotService service, CancellationToken ct) => (await service.GetAsync(itemId, lotId, ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("GetItemLot").Produces<ItemLotRow>();
        lots.MapPut("/{lotId:guid}", async (Guid itemId, Guid lotId, LotCorrectionInput input, LotService service, CancellationToken ct) =>
                (await service.CorrectAsync(itemId, lotId, input, ct)).ToHttpResult())
            .RequirePermission("inventory.edit").WithName("CorrectItemLot").Produces<ItemLotRow>();
        lots.MapGet("/{lotId:guid}/trace", async (Guid itemId, Guid lotId, TraceabilityService service, CancellationToken ct) => (await service.LotAsync(itemId, lotId, ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("GetLotTrace").Produces<LotTrace>();
        lots.MapGet("/{lotId:guid}/trace/export", async (Guid itemId, Guid lotId, TraceabilityService service, HttpContext http, CancellationToken ct) =>
            {
                var result = await service.ExportLotAsync(itemId, lotId, ct);
                if (result.IsFailure)
                {
                    return result.Error!.ToProblem();
                }

                AttachmentEndpoints.ApplyDownloadHeaders(http.Response);
                return Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
            })
            .RequirePermission("inventory.view").WithName("ExportLotTrace");

        inventory.MapGet("/items/{itemId:guid}/serials/{serialId:guid}/trace", async (Guid itemId, Guid serialId, TraceabilityService service, CancellationToken ct) =>
                (await service.SerialAsync(itemId, serialId, ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("GetSerialTrace").Produces<SerialTrace>();
        return api;
    }
}
