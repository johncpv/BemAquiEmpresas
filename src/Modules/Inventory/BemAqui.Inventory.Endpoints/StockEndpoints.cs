using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Inventory.Endpoints;

/// <summary>
/// Saldos, movimentos, lançamentos manuais e implantação do estoque (EST-01a). Módulo <c>inventory</c> com as três camadas (CA 30);
/// todo comando que cria movimento ou lançamento exige <c>Idempotency-Key</c>.
/// </summary>
internal static class StockEndpoints
{
    public const string ImmutableMessage = "Movimento de estoque não muda nem é apagado; estorne-o.";

    private static readonly string[] PostingHistoryChildren = [nameof(ManualPostingLine)];

    public static IEndpointRouteBuilder MapStockEndpoints(this IEndpointRouteBuilder api)
    {
        var inventory = api.MapGroup("/inventory").WithTags("Estoque").RequireModule("inventory");
        MapBalances(inventory);
        MapMovements(inventory);
        MapManualPostings(inventory.MapGroup("/manual-postings"));
        return api;
    }

    private static void MapBalances(RouteGroupBuilder inventory)
    {
        inventory.MapGet("/balances", async (string? q, Guid? locationId, Guid? categoryId, ItemType? type, StockSituation? situation, int? page, int? pageSize, StockQueryService service, CancellationToken ct) =>
                Results.Ok(await service.BalancesAsync(new BalanceListQuery(q, locationId, categoryId, type, situation), Page(page, pageSize), ct)))
            .RequirePermission("inventory.view").WithName("ListStockBalances").Produces<PagedList<BalanceRow>>();
        inventory.MapGet("/opening", async (ManualPostingService service, CancellationToken ct) => Results.Ok(await service.OpeningStatusAsync(ct)))
            .RequirePermission("inventory.view").WithName("GetStockOpening").Produces<StockOpeningStatus>();
        inventory.MapPost("/opening/complete", async (CompleteStockOpeningInput input, ManualPostingService service, CancellationToken ct) => (await service.CompleteOpeningAsync(input, ct)).ToHttpResult())
            .RequirePermission("inventory.approve").WithName("CompleteStockOpening").Produces<StockOpeningStatus>();
        inventory.MapPost("/opening-balances", async (OpeningBalanceInput input, ManualPostingService service, CancellationToken ct) =>
                (await service.CreateOpeningAsync(input, ct)).ToHttpResult(saved => Results.Created(ManualPostingService.Link + saved.Id, saved)))
            .RequirePermission("inventory.create").RequireIdempotencyKey().WithName("CreateOpeningBalance").Produces<ManualPostingDetail>(StatusCodes.Status201Created);
        inventory.MapPost("/opening-balances/batch", async (OpeningBatchInput input, ManualPostingService service, CancellationToken ct) =>
                (await service.CreateOpeningBatchAsync(input, ct)).ToHttpResult(saved => Results.Created(ManualPostingService.Link + saved.Id, saved)))
            .RequirePermission("inventory.create").RequireIdempotencyKey().WithName("CreateOpeningBalanceBatch").Produces<ManualPostingDetail>(StatusCodes.Status201Created);
        inventory.MapGet("/items/{itemId:guid}/stock", async (Guid itemId, StockQueryService service, CancellationToken ct) => (await service.ItemContextAsync(itemId, ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("GetItemStockContext").Produces<StockItemContext>();
        inventory.MapGet("/projects", async (string? q, StockQueryService service, CancellationToken ct) => Results.Ok(await service.ProjectsAsync(q, ct)))
            .RequirePermission("inventory.view").WithName("SearchStockProjects").Produces<IReadOnlyList<StockProjectOption>>();
    }

    private static void MapMovements(RouteGroupBuilder inventory)
    {
        inventory.MapGet("/movements", async (
                Guid? itemId, Guid? locationId, StockMovementType? type, DateOnly? from, DateOnly? to, Guid? projectId, Guid? productionOrderId, string? sourceType, Guid? sourceId,
                string? origin, string? number, int? page, int? pageSize, StockQueryService service, CancellationToken ct) =>
                Results.Ok(await service.MovementsAsync(new MovementListQuery(itemId, locationId, type, from, to, projectId, productionOrderId, sourceType, sourceId, origin, number), Page(page, pageSize), ct)))
            .RequirePermission("inventory.view").WithName("ListStockMovements").Produces<PagedList<MovementRow>>();
        inventory.MapGet("/movements/{id:guid}", async (Guid id, StockQueryService service, CancellationToken ct) => (await service.MovementAsync(id, ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("GetStockMovement").Produces<MovementDetail>();
        // CA 22: movimento é imutável; a API responde 405 com o caminho (estornar) e o trigger recusa no banco.
        inventory.MapPut("/movements/{id:guid}", (Guid id) => Immutable())
            .RequirePermission("inventory.edit").WithName("UpdateStockMovement").ProducesProblem(StatusCodes.Status405MethodNotAllowed);
        inventory.MapDelete("/movements/{id:guid}", (Guid id) => Immutable())
            .RequirePermission("inventory.delete").WithName("DeleteStockMovement").ProducesProblem(StatusCodes.Status405MethodNotAllowed);
        inventory.MapPost("/movements/{id:guid}/reverse", async (Guid id, ReverseMovementInput input, ManualPostingService service, CancellationToken ct) => (await service.ReverseAsync(id, input, ct)).ToHttpResult())
            .RequireAnyPermission("inventory.edit", "inventory.approve").RequireIdempotencyKey().WithName("ReverseStockMovement").Produces<MovementDetail>();
        inventory.MapGet("/items/{itemId:guid}/movements", async (Guid itemId, Guid? locationId, int? page, int? pageSize, StockQueryService service, CancellationToken ct) =>
                (await service.KardexAsync(itemId, locationId, Page(page, pageSize), ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("GetItemKardex").Produces<PagedList<MovementRow>>();
    }

    private static void MapManualPostings(RouteGroupBuilder postings)
    {
        postings.MapGet(string.Empty, async (ManualPostingStatus? status, bool? awaitingMe, ManualPostingKind? kind, int? page, int? pageSize, ManualPostingService service, CancellationToken ct) =>
                Results.Ok(await service.ListAsync(new ManualPostingListQuery(status, awaitingMe == true, kind), Page(page, pageSize), ct)))
            .RequirePermission("inventory.view").WithName("ListManualPostings").Produces<PagedList<ManualPostingSummary>>();
        postings.MapGet("/{id:guid}", async (Guid id, ManualPostingService service, CancellationToken ct) => (await service.DetailAsync(id, ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("GetManualPosting").Produces<ManualPostingDetail>();
        postings.MapGet("/{id:guid}/lines/{lineId:guid}", async (Guid id, Guid lineId, ManualPostingService service, CancellationToken ct) => (await service.LineAsync(id, lineId, ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("GetManualPostingLine").Produces<ManualPostingLineView>();
        postings.MapGet("/{id:guid}/history", async (Guid id, int? page, int? pageSize, AuditHistory history, CancellationToken ct) =>
                (await history.GetWithChildrenAsync(nameof(ManualPosting), id.ToString(), PostingHistoryChildren, "postingId", Page(page, pageSize), ct)).ToHttpResult())
            .RequirePermission("inventory.view").WithName("GetManualPostingHistory").Produces<PagedList<RelatedAuditEntryDto>>();
        postings.MapPost(string.Empty, async (ManualPostingInput input, ManualPostingService service, CancellationToken ct) =>
                (await service.CreateAsync(input, ct)).ToHttpResult(saved => Results.Created(ManualPostingService.Link + saved.Id, saved)))
            .RequirePermission("inventory.create").RequireIdempotencyKey().WithName("CreateManualPosting").Produces<ManualPostingDetail>(StatusCodes.Status201Created);
        postings.MapPost("/{id:guid}/approve", async (Guid id, ManualDecisionInput input, ManualPostingService service, CancellationToken ct) => (await service.ApproveAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("inventory.approve").RequireIdempotencyKey().WithName("ApproveManualPosting").Produces<ManualPostingDetail>();
        postings.MapPost("/{id:guid}/reject", async (Guid id, ManualDecisionInput input, ManualPostingService service, CancellationToken ct) => (await service.RejectAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("inventory.approve").WithName("RejectManualPosting").Produces<ManualPostingDetail>();
        postings.MapPost("/{id:guid}/cancel", async (Guid id, ManualCancelInput input, ManualPostingService service, CancellationToken ct) => (await service.CancelAsync(id, input, ct)).ToHttpResult())
            .RequirePermission("inventory.create").WithName("CancelManualPosting").Produces<ManualPostingDetail>();
    }

    private static IResult Immutable() =>
        Results.Problem(detail: ImmutableMessage, statusCode: StatusCodes.Status405MethodNotAllowed, title: "Operação não permitida",
            extensions: new Dictionary<string, object?> { ["code"] = "movement_immutable" });

    private static PageRequest Page(int? page, int? pageSize) => new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize();
}
