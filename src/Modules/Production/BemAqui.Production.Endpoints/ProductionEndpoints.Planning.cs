using BemAqui.BuildingBlocks.Web;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Production.Endpoints;

internal static partial class ProductionEndpoints
{
    /// <summary>
    /// Planejamento (PLN-01a CA 14, 16 e 18), no grupo do módulo <c>production</c>: o cabeçalho e as previsões das OS com
    /// <c>production.view</c>; "Recalcular agora" com <c>production.edit</c>. Nenhuma resposta tem custo.
    /// </summary>
    private static void MapSchedule(RouteGroupBuilder schedule)
    {
        schedule.MapGet(string.Empty, async (PlanningService service, CancellationToken ct) => Results.Ok(await service.SummaryAsync(ct)))
            .RequirePermission("production.view").WithName("GetPlanningSummary").Produces<PlanningSummary>();
        schedule.MapGet("/orders", async (Guid[]? ids, PlanningService service, CancellationToken ct) =>
                Results.Ok(await service.ForOrdersAsync([.. (ids ?? []).Distinct().Take(500)], ct)))
            .RequirePermission("production.view").WithName("ListOrderForecasts").Produces<IReadOnlyList<OrderForecastInfo>>();
        schedule.MapPost("/recalculate", async (PlanningService service, CancellationToken ct) => Results.Ok(await service.RecalculateNowAsync(ct)))
            .RequirePermission("production.edit").WithName("RecalculateSchedule").Produces<ScheduleRecalculation>();
    }
}
