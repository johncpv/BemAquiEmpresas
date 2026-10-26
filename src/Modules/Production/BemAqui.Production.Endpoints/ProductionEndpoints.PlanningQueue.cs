using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Production.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Production.Endpoints;

internal static partial class ProductionEndpoints
{
    /// <summary>
    /// Fila de prioridade (PLN-02 CA 1, 4, 6, 8 e 11), no grupo do módulo <c>production</c>: ver a fila e o histórico com
    /// <c>production.view</c>; a prévia (rate limit <c>planning-simulate</c>, CA 10) e o "Salvar ordem" com <c>production.edit</c>. Sem custo.
    /// </summary>
    private static void MapPlanningQueue(RouteGroupBuilder queue)
    {
        queue.MapGet(string.Empty, async (PlanningQueueService service, CancellationToken ct) => Results.Ok(await service.GetAsync(ct)))
            .RequirePermission("production.view").WithName("GetPlanningQueue").Produces<PlanningQueueView>();
        queue.MapPost("/preview", async (PlanningQueuePreviewInput input, PlanningQueueService service, CancellationToken ct) => (await service.PreviewAsync(input, ct)).ToHttpResult())
            .RequirePermission("production.edit").RequireRateLimiting(PlanningQueueService.SimulatePolicy).WithName("PreviewPlanningQueue").Produces<PlanningQueuePreview>();
        queue.MapPut(string.Empty, async (PlanningQueueSaveInput input, PlanningQueueService service, CancellationToken ct) => (await service.SaveAsync(input, ct)).ToHttpResult())
            .RequirePermission("production.edit").WithName("SavePlanningQueue").Produces<PlanningQueueSaved>();
        queue.MapGet("/changes", async (int? page, int? pageSize, PlanningQueueService service, CancellationToken ct) =>
                Results.Ok(await service.ChangesAsync(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)))
            .RequirePermission("production.view").WithName("ListPlanningQueueChanges").Produces<PagedList<PlanningQueueChangeView>>();
    }
}
